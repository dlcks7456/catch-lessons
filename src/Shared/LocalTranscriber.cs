using System.Threading.Channels;
using Whisper.net;

namespace CatchLessons;

public sealed record TranscriptPreview(string Text, double End);

public sealed class LocalTranscriber : ITranscriber
{
    private WhisperFactory? factory;
    private WhisperProcessor? processor;
    private WhisperVadFactory? vadFactory;
    private WhisperVadProcessor? vad;
    private readonly Channel<AudioClip> clips = Channel.CreateBounded<AudioClip>(9);
    private readonly Queue<AudioFrame> preroll = new();
    private readonly MemoryStream active = new();
    private readonly CancellationTokenSource lifetime = new();
    private Settings settings = new();
    private Task? worker;
    private double start, end, lastVoice;
    private double lastPreviewEnd;
    private int previewQueued;
    private int nextId, queued;
    internal int PendingClips => Volatile.Read(ref queued);
    internal int PendingPreviews => Volatile.Read(ref previewQueued);
    private Exception? failure;
    public event Action<Caption>? Completed;
    public event Action<TranscriptPreview>? Partial;
    public event Action<string[]>? LanguagesDetected;
    public event Action<string>? Status;
    public event Action<Exception>? Failed;
    public string RuntimeInfo { get; private set; } = "";
    private sealed record AudioClip(int Id, float[] Samples, double Start, double End, bool Preview = false);

    public async Task InitializeAsync(Settings configuration, CancellationToken token)
    {
        settings = configuration;
        if (settings.LocalModel == "fast" && settings.SourceLanguage == "yue")
            throw new InvalidOperationException("광둥어 고정 인식에는 균형 또는 품질 우선 모델을 선택하세요.");
        var path = await LocalModels.EnsureAsync(LocalModels.Resolve(settings.LocalModel), s => Status?.Invoke(s), token);
        var vadPath = await LocalModels.EnsureAsync(LocalModels.Vad, s => Status?.Invoke(s), token);
        Status?.Invoke("로컬 음성 모델을 메모리에 올리고 있습니다…");
        await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            try
            {
                factory = WhisperFactory.FromPath(path, new() { UseGpu = !settings.LocalCpuOnly, UseFlashAttention = !settings.LocalCpuOnly });
                processor = BuildProcessor(factory);
            }
            catch when (!settings.LocalCpuOnly && !token.IsCancellationRequested)
            {
                factory?.Dispose();
                Status?.Invoke("GPU 초기화 실패 · CPU로 다시 준비하고 있습니다…");
                factory = WhisperFactory.FromPath(path, new() { UseGpu = false });
                processor = BuildProcessor(factory);
            }
            vadFactory = WhisperVadFactory.FromPath(vadPath);
            vad = vadFactory.CreateBuilder().WithUseGpu(false).WithThreads(2)
                .WithThreshold(.5f).WithMinSpeechDuration(TimeSpan.FromMilliseconds(250)).Build();
            RuntimeInfo = $"Runtime={Whisper.net.LibraryLoader.RuntimeOptions.LoadedLibrary}; CpuOnly={settings.LocalCpuOnly}; " + WhisperFactory.GetRuntimeInfo();
        }, token);
        if (!settings.LocalCpuOnly && Whisper.net.LibraryLoader.RuntimeOptions.LoadedLibrary == Whisper.net.LibraryLoader.RuntimeLibrary.Vulkan)
        {
            Status?.Invoke("GPU 음성 엔진 예열 중… 준비가 끝나면 녹음이 시작됩니다.");
            await foreach (var ignored in processor!.ProcessAsync(new float[16000], token)) { }
        }
        token.ThrowIfCancellationRequested();
        worker = Task.Run(RecognizeLoopAsync);
    }
    private WhisperProcessor BuildProcessor(WhisperFactory model) => model.CreateBuilder()
        .WithLanguage(settings.SourceLanguage == "auto" ? "auto" : NativeLanguage(settings.SourceLanguage))
        .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 2, 8)).WithNoContext()
        .WithPrompt(string.Join(". ", new[] { settings.Topic }.Concat(settings.Keywords)))
        .WithNoSpeechThreshold(.6f).Build();
    internal static string NativeLanguage(string code) => code == "jv" ? "jw" : code;
    internal static float[] Samples(byte[] pcm)
    {
        if (pcm.Length % 2 != 0) throw new ArgumentException("PCM16 byte count must be even");
        var result = new float[pcm.Length / 2];
        for (int i = 0; i < result.Length; i++) result[i] = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i * 2, 2)) / 32768f;
        return result;
    }
    public Task PushAsync(AudioFrame frame, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (failure != null) throw new IOException("로컬 음성 인식이 중단됐습니다.", failure);
        // Energy only starts buffering; the trained Silero VAD validates each clip before ASR.
        bool voice = frame.Peak >= .0005f;
        if (active.Length == 0)
        {
            preroll.Enqueue(frame);
            while (preroll.Count > 6) preroll.Dequeue();
            if (!voice) return Task.CompletedTask;
            start = preroll.Peek().Start; lastVoice = frame.End;
            lastPreviewEnd = start;
            foreach (var buffered in preroll) active.Write(buffered.Pcm);
            preroll.Clear();
        }
        else active.Write(frame.Pcm);
        end = frame.End;
        if (voice) lastVoice = end;
        var timing = settings.Segmentation;
        if (end - lastVoice >= timing.Silence || end - start >= timing.Maximum) Commit();
        else if (settings.StreamingPreview && end - start >= 2 && end - lastPreviewEnd >= 1.5 &&
                 Volatile.Read(ref queued) == 0 && Interlocked.CompareExchange(ref previewQueued, 1, 0) == 0)
        {
            // One in-flight snapshot only. Final clips are never displaced by speculative work.
            var preview = new AudioClip(nextId + 1, Samples(active.ToArray()), start, end, true);
            if (!clips.Writer.TryWrite(preview)) Interlocked.Exchange(ref previewQueued, 0);
            lastPreviewEnd = end;
        }
        return Task.CompletedTask;
    }
    private void Commit()
    {
        if (active.Length == 0) return;
        if (Volatile.Read(ref queued) >= 8)
            throw new IOException("로컬 전사가 재생 속도를 따라가지 못합니다. 더 가벼운 모델을 선택하세요.");
        var clip = new AudioClip(++nextId, Samples(active.ToArray()), start, end);
        Interlocked.Increment(ref queued);
        if (!clips.Writer.TryWrite(clip))
        {
            Interlocked.Decrement(ref queued);
            throw new IOException("로컬 전사가 재생 속도를 따라가지 못합니다. 더 가벼운 모델을 선택하세요. 마지막 구간은 처리되지 않았습니다.");
        }
        active.SetLength(0); active.Position = 0;
    }
    public Task FlushPendingAsync(CancellationToken token){token.ThrowIfCancellationRequested();Commit();preroll.Clear();return Task.CompletedTask;}
    private async Task RecognizeLoopAsync()
    {
        try
        {
            await foreach (var clip in clips.Reader.ReadAllAsync(lifetime.Token))
            {
                try
                {
                    if (clip.Preview && Volatile.Read(ref nextId) >= clip.Id) continue;
                    NormalizeQuiet(clip.Samples);
                    var speech = vad!.DetectSpeech(clip.Samples);
                    if (speech.Count == 0) continue;
                    if (!clip.Preview) Status?.Invoke($"PC에서 원문 인식 중 · 대기 {Math.Max(0, queued - 1)}개 구간");
                    if (settings.SourceLanguage == "auto")
                    {
                        // ProcessAsync already detects the language; a separate detection pass doubles encoder work.
                        processor!.ChangeLanguage("auto");
                    }
                    var texts = new List<string>();
                    var languages = new HashSet<string>();
                    await foreach (var segment in processor!.ProcessAsync(clip.Samples, lifetime.Token))
                        if (!string.IsNullOrWhiteSpace(segment.Text) && segment.NoSpeechProbability < .6f)
                        {
                            texts.Add(segment.Text.Trim());
                            if (!string.IsNullOrEmpty(segment.Language)) languages.Add(segment.Language == "jw" ? "jv" : segment.Language);
                        }
                    if (texts.Count > 0)
                    {
                        var text = string.Join(" ", texts);
                        if (clip.Preview)
                        {
                            if (Volatile.Read(ref nextId) < clip.Id) Partial?.Invoke(new(text, clip.End));
                            continue;
                        }
                        Partial?.Invoke(new(text, clip.End));
                        if (settings.SourceLanguage == "auto") LanguagesDetected?.Invoke(speech.Sum(s => (s.End - s.Start).TotalSeconds) >= 1 ? languages.ToArray() : []);
                        Completed?.Invoke(new(clip.Id, $"local-{clip.Id}", clip.Start, clip.End, text));
                    }
                }
                finally
                {
                    if (clip.Preview) Interlocked.Exchange(ref previewQueued, 0);
                    else Interlocked.Decrement(ref queued);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { failure = ex; clips.Writer.TryComplete(ex); Failed?.Invoke(ex); }
    }
    internal static void NormalizeQuiet(float[] samples)
    {
        var peak=0f;foreach(var sample in samples)peak=Math.Max(peak,Math.Abs(sample));
        if(peak<.0005f||peak>=.05f)return;
        var gain=Math.Min(64f,.15f/peak);
        for(int i=0;i<samples.Length;i++)samples[i]=Math.Clamp(samples[i]*gain,-1f,1f);
    }
    public async Task DrainAsync(CancellationToken token)
    {
        Commit(); clips.Writer.TryComplete();
        if (worker == null) return;
        try { await worker.WaitAsync(token); if (failure != null) throw new IOException("로컬 전사 실패", failure); }
        catch (OperationCanceledException) { lifetime.Cancel(); await worker; throw; }
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); clips.Writer.TryComplete();
        try { if (worker != null) await worker; }
        finally
        {
            if (processor != null) await processor.DisposeAsync();
            if (vad != null) await vad.DisposeAsync();
            vadFactory?.Dispose(); factory?.Dispose(); active.Dispose(); lifetime.Dispose();
        }
    }
}
