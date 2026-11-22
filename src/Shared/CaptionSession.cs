using System.Threading.Channels;
using System.Diagnostics;
using System.Text.RegularExpressions;
using NAudio.Wave;

namespace CatchLessons;

public sealed record ApiTranslationPreview(long Revision, double End, string Text, int TurnId = 0);

public sealed class CaptionSession
{
    private sealed record LiveClause(string SourcePrefix, Caption Translation);
    private static readonly Regex clauseBoundary = new(
        """[。！？；।॥]+["'”’)\]」』]*|(?:[!?;؟؛]+|(?<!\d)(?<!\b[A-Za-z])(?<!\b(?:Mr|Ms|Dr|St|Jr|Sr|vs))\.)["'”’)\]」』]*(?=\s|$)""",
        RegexOptions.IgnoreCase);
    private static readonly TimeSpan previewSpacing = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan previewTimeout = TimeSpan.FromSeconds(8);

    private readonly Settings settings;
    private readonly IAudioCapture capture = AudioPlatform.Create();
    private readonly ITranscriber transcriber;
    private readonly CancellationTokenSource captureStop = new();
    private readonly CancellationTokenSource workStop = new();
    private readonly Channel<AudioFrame> audio = Channel.CreateBounded<AudioFrame>(100);
    private readonly Channel<Caption> translations = Channel.CreateBounded<Caption>(100);
    private readonly Channel<Caption> rawTranscripts = Channel.CreateBounded<Caption>(100);
    private Task? pump, sender, translatorTask, assemblerTask;
    private WaveFileWriter? recording;
    private SessionStore? store;
    private CodexTranslator? translator;
    private OpenAiTranslator? apiTranslator;
    private OpenAiTranslator? apiPreviewTranslator;
    private readonly object apiPreviewGate=new();
    private CancellationTokenSource? apiPreviewCancellation;
    private CancellationTokenSource? activePreviewCancellation;
    private Task? apiPreviewTask;
    private double apiPreviewFinalThrough;
    private int apiPreviewFinalTurn;
    private long apiPreviewRevision;
    private ApiTranslationPreview? lastPublishedApiPreview;
    private Caption? queuedApiPreview, lastApiPreview, activeApiPreview;
    private long apiPreviewStarted;
    private bool apiPreviewStopping;
    private readonly List<LiveClause> liveClauses = new();
    private int liveClauseTurn;
    public CodexTranslator? Agent => translator;
    public event Action<string>? SessionStarted;
    private readonly Dictionary<int, Caption> pending = new();
    private readonly object gate = new();
    private bool stopping;
    private bool paused;
    private long capturedSamples;
    private readonly object pauseGate=new();
    public bool IsPaused {get{lock(pauseGate)return paused;}}
    public void SetPaused(bool value)
    {
        lock(pauseGate)
        {
            if(value&&!paused&&!audio.Writer.TryWrite(new AudioFrame([],capturedSamples/16000d,capturedSamples/16000d,0)))throw new IOException("오디오 대기열이 가득 차 일시정지하지 못했습니다.");
            paused=value;
        }
    }
    public void FlushCaptions()=>store?.Flush();
    private int failedTranslations;
    public string? StopWarning { get; private set; }
    public string? Folder => store?.Folder;
    public event Action<string>? Status;
    public event Action<TranscriptPreview>? Partial;
    public event Action<ApiTranslationPreview>? TranslationPreview;
    public event Action<Caption>? Updated;
    public event Action<float>? Level;
    public event Action<Exception>? Failed;
    public event Action<string[]>? LanguagesDetected;

    public CaptionSession(Settings settings)
    {
        if(settings.SpeechEngine=="whisper"&&!LocalModels.WhisperEnabled)throw new InvalidOperationException("Local Whisper was not installed. Select OpenAI API or reinstall with Whisper.");
        this.settings = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        transcriber = settings.SpeechEngine == "openai" ? new CloudTranscriber() : new LocalTranscriber();
    }
    internal void OnTranscriptPreview(TranscriptPreview preview)
    {
        Partial?.Invoke(preview);
        if(settings.ApiTranslation&&settings.StreamingPreview)
            QueueApiPreview(new(preview.TurnId>0?preview.TurnId:1,"draft",0,preview.End,preview.Text));
    }
    private bool IsCovered(Caption caption) => caption.End <= apiPreviewFinalThrough || caption.Id <= apiPreviewFinalTurn;
    private static bool HasPreviewText(string text) => text.Length >= 20 || (text.Length >= 8 && clauseBoundary.IsMatch(text));
    private static int CompleteClauseEnd(string text)
    {
        var boundaries = clauseBoundary.Matches(text);
        return boundaries.Count == 0 ? 0 : boundaries[^1].Index + boundaries[^1].Length;
    }
    private bool HasNewPreviewText(Caption caption) => lastApiPreview == null || caption.Id != lastApiPreview.Id ||
        (!string.Equals(caption.Original, lastApiPreview.Original, StringComparison.Ordinal) &&
         (!caption.Original.StartsWith(lastApiPreview.Original, StringComparison.Ordinal) ||
          caption.Original.Length - lastApiPreview.Original.Length >= 12 ||
          clauseBoundary.IsMatch(caption.Original, lastApiPreview.Original.Length)));

    private void QueueApiPreview(Caption caption)
    {
        caption = caption with { Original = caption.Original.Trim() };
        lock (apiPreviewGate)
        {
            if (apiPreviewStopping || workStop.IsCancellationRequested || !HasPreviewText(caption.Original) || IsCovered(caption) ||
                (lastApiPreview != null && caption.Id < lastApiPreview.Id) ||
                (activeApiPreview != null && caption.Id < activeApiPreview.Id) ||
                (queuedApiPreview != null && caption.Id < queuedApiPreview.Id)) return;
            queuedApiPreview = caption;
            StartApiPreviewWorker();
        }
    }

    // Called under apiPreviewGate. Final requests use their own translator and do not stall this worker.
    private void StartApiPreviewWorker()
    {
        if (apiPreviewStopping || workStop.IsCancellationRequested || apiPreviewTask != null || queuedApiPreview == null ||
            apiTranslator is not { } confirmedTranslator || apiPreviewTranslator is not { } previewTranslator || !HasNewPreviewText(queuedApiPreview)) return;
        var worker = CancellationTokenSource.CreateLinkedTokenSource(workStop.Token);
        apiPreviewCancellation = worker;
        apiPreviewTask = Task.Run(async () =>
        {
            try
            {
                while (!worker.IsCancellationRequested)
                {
                    var delay = apiPreviewStarted == 0 ? TimeSpan.Zero : previewSpacing - Stopwatch.GetElapsedTime(apiPreviewStarted);
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, worker.Token);

                    Caption snapshot;
                    LiveClause[] prefix;
                    long revision;
                    using var request = CancellationTokenSource.CreateLinkedTokenSource(worker.Token);
                    lock (apiPreviewGate)
                    {
                        if (worker.IsCancellationRequested || queuedApiPreview == null || IsCovered(queuedApiPreview) || !HasNewPreviewText(queuedApiPreview)) return;
                        snapshot = queuedApiPreview;
                        queuedApiPreview = null;
                        activeApiPreview = snapshot;
                        activePreviewCancellation = request;
                        revision = ++apiPreviewRevision;
                        prefix = liveClauseTurn == snapshot.Id
                            ? liveClauses.TakeWhile(clause => snapshot.Original.StartsWith(clause.SourcePrefix, StringComparison.Ordinal)).ToArray()
                            : [];
                    }
                    try
                    {
                        var prefixLength = prefix.Length == 0 ? 0 : prefix[^1].SourcePrefix.Length;
                        var remaining = snapshot.Original[prefixLength..].TrimStart();
                        if (!HasPreviewText(remaining)) continue;
                        // shortcut: punctuation can split uncommon abbreviations, use ASR word timings if it disrupts captions.
                        var clauseEnd = CompleteClauseEnd(remaining);
                        var translationInput = snapshot with { Original = clauseEnd > 0 ? remaining[..clauseEnd] : remaining };
                        var untranslatedTail = clauseEnd > 0 ? remaining[clauseEnd..].TrimStart() : "";
                        var readablePrefix = string.Join(Environment.NewLine, prefix.Select(clause => clause.Translation.Korean));

                        apiPreviewStarted = Stopwatch.GetTimestamp();
                        request.CancelAfter(previewTimeout);
                        previewTranslator.ResetPreviewContext(confirmedTranslator, prefix.Select(clause => clause.Translation));
                        var text = await previewTranslator.TranslateAsync(translationInput, partial =>
                        {
                            var readableEnd = CompleteClauseEnd(partial);
                            if (readableEnd >= 8)
                                PublishApiPreview(snapshot, revision, readablePrefix, partial[..readableEnd], request.Token, inProgress: true);
                        }, request.Token);
                        lock (apiPreviewGate)
                        {
                            if (request.IsCancellationRequested || IsCovered(snapshot)) continue;
                            lastApiPreview = snapshot;
                            liveClauseTurn = snapshot.Id;
                            liveClauses.Clear();
                            liveClauses.AddRange(prefix);
                            if (clauseEnd > 0)
                            {
                                var sourcePrefix = snapshot.Original[..(snapshot.Original.Length - remaining.Length + clauseEnd)];
                                liveClauses.Add(new(sourcePrefix, translationInput with { Korean = text }));
                                lastApiPreview = snapshot with { Original = sourcePrefix };
                                if (HasPreviewText(untranslatedTail))
                                    queuedApiPreview ??= snapshot;
                            }
                        }
                        PublishApiPreview(snapshot, revision, readablePrefix, text, request.Token, inProgress: untranslatedTail.Length > 0);
                    }
                    catch (OperationCanceledException) when (request.IsCancellationRequested) { }
                    catch (Exception)
                    {
                        // A failed preview leaves the last readable text in place. The final request reports its own errors.
                    }
                    finally
                    {
                        lock (apiPreviewGate)
                        {
                            activeApiPreview = null;
                            activePreviewCancellation = null;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (worker.IsCancellationRequested) { }
            finally
            {
                lock (apiPreviewGate)
                {
                    apiPreviewCancellation = null;
                    apiPreviewTask = null;
                    StartApiPreviewWorker();
                }
                worker.Dispose();
            }
        });
    }

    private void PublishApiPreview(Caption snapshot, long revision, string prefix, string text, CancellationToken token, bool inProgress)
    {
        var display = prefix.Length == 0 ? text.Trim() : prefix + Environment.NewLine + text.Trim();
        lock (apiPreviewGate)
        {
            if (token.IsCancellationRequested || IsCovered(snapshot)) return;
            if (lastPublishedApiPreview is { } previous && previous.TurnId == snapshot.Id &&
                (display == previous.Text || (inProgress && display.Length < previous.Text.Length))) return;
            var update = new ApiTranslationPreview(revision, snapshot.End, display, snapshot.Id);
            lastPublishedApiPreview = update;
            TranslationPreview?.Invoke(update);
        }
    }

    private void MarkApiPreviewFinal(Caption caption)
    {
        lock (apiPreviewGate)
        {
            apiPreviewFinalThrough = Math.Max(apiPreviewFinalThrough, caption.End);
            apiPreviewFinalTurn = Math.Max(apiPreviewFinalTurn, caption.Id);
            if (queuedApiPreview != null && IsCovered(queuedApiPreview)) queuedApiPreview = null;
            if (activeApiPreview != null && IsCovered(activeApiPreview)) activePreviewCancellation?.Cancel();
        }
    }
    private async Task StopApiPreviewAsync()
    {
        Task? preview;
        lock (apiPreviewGate)
        {
            apiPreviewStopping = true;
            queuedApiPreview = null;
            apiPreviewCancellation?.Cancel();
            preview = apiPreviewTask;
        }
        if (preview != null) await preview;
    }
    public async Task StartAsync(CancellationToken token = default)
    {
        if (settings.ApiTranslation)
        {
            apiTranslator = new OpenAiTranslator(settings);
            apiPreviewTranslator = apiTranslator.CreatePreviewTranslator();
        }
        else if(settings.RequiresAgentForCapture) translator = CodexTranslator.Create(settings, Path.Combine(Settings.Root, "agent-work"));
        transcriber.LanguagesDetected += codes => LanguagesDetected?.Invoke(codes);
        transcriber.Status += text => Status?.Invoke(text);
        transcriber.Partial += OnTranscriptPreview;
        transcriber.Completed += OnRawTranscript;
        transcriber.Failed += ex => Failed?.Invoke(ex);
        Status?.Invoke("음성 인식 준비 중…");
        await transcriber.InitializeAsync(settings, token);
        store = new(settings.OutputFolder, settings.TargetLanguage, !settings.TranscriptionOnly);
        if (translator != null)
        {
            Status?.Invoke("새 Agent 세션을 여는 중…");
            await translator.StartSessionAsync(store.InternalFolder, token);
            SessionStarted?.Invoke(translator.SessionId!);
        }
        File.WriteAllText(Path.Combine(store.InternalFolder, "session.json"), System.Text.Json.JsonSerializer.Serialize(new { sourceLanguage = settings.SourceLanguage, targetLanguage = settings.TargetLanguage, mode = settings.TranscriptionOnly ? "transcription" : "translation", translationProvider = settings.TranslationProvider, translationModel = settings.ApiTranslation ? OpenAiTranslator.Model : settings.AgentModel, engine = settings.SpeechEngine, model = settings.SpeechEngine == "openai" ? CloudTranscriber.Model : LocalModels.Resolve(settings.LocalModel).File, runtime = transcriber.RuntimeInfo }));
        if (settings.RecordAudio) recording = new WaveFileWriter(Path.Combine(store.Folder, "audio.wav"), AudioCapture.Format);
        await capture.StartAsync(settings.DeviceId,token);
        if (!settings.TranscriptionOnly) translatorTask = Task.Run(TranslateLoopAsync);
        assemblerTask = Task.Run(AssembleAsync);
        sender = Task.Run(SendLoopAsync);
        pump = Task.Run(PumpAsync);
        Status?.Invoke("시스템 오디오를 듣고 있습니다");
    }
    internal void OnRawTranscript(Caption caption)
    {
        if(rawTranscripts.Writer.TryWrite(caption))
        {
            if(settings.ApiTranslation&&settings.StreamingPreview&&CaptionAssembler.NeedsContinuation(caption.Original))QueueApiPreview(caption);
            return;
        }
        store!.Save(caption with{State="queue_full"});
        Failed?.Invoke(new IOException("전사 대기열이 가득 찼습니다. 원본 인식 결과는 저장했습니다."));
    }
    private async Task PumpAsync()
    {
        try
        {
            await capture.PumpAsync(frame =>
            {
                QueueCaptured(frame);
                return Task.CompletedTask;
            }, captureStop.Token);
        }
        catch (OperationCanceledException) when (captureStop.IsCancellationRequested) { }
        catch (Exception ex) { Failed?.Invoke(ex); }
        finally { audio.Writer.TryComplete(); }
    }
    internal void QueueCaptured(AudioFrame frame)
    {
        lock(pauseGate)
        {
            if(paused)return;
            Level?.Invoke(frame.Peak);recording?.Write(frame.Pcm,0,frame.Pcm.Length);
            var start=capturedSamples/16000d;capturedSamples+=frame.Pcm.Length/2;
            if(!audio.Writer.TryWrite(frame with{Start=start,End=capturedSamples/16000d}))throw new IOException("오디오 처리가 밀렸습니다. 마지막 구간이 누락될 수 있어 중단합니다.");
        }
    }
    private async Task SendLoopAsync()
    {
        try
        {
            await foreach (var frame in audio.Reader.ReadAllAsync(workStop.Token))
                if(frame.Pcm.Length==0)await transcriber.FlushPendingAsync(workStop.Token);else await transcriber.PushAsync(frame, workStop.Token);
        }
        catch (OperationCanceledException) when (workStop.IsCancellationRequested) { }
        catch (Exception ex) { Failed?.Invoke(ex); }
    }
    private void OnTranscript(Caption caption)
    {
        if (settings.TranscriptionOnly)
        {
            caption = caption with { State = "transcribed" };
            store!.Save(caption);
            Updated?.Invoke(caption);
            return;
        }
        store!.Save(caption);
        lock (gate) pending[caption.Id] = caption;
        if (settings.ApiTranslation) MarkApiPreviewFinal(caption);
        Updated?.Invoke(caption);
        if (!translations.Writer.TryWrite(caption))
        {
            store.Save(caption with { State = "queue_full" });
            Failed?.Invoke(new IOException("번역 대기열이 가득 찼습니다. 원문은 저장했습니다."));
        }
    }
    private async Task AssembleAsync()
    {
        try
        {
            var captions=settings.ApiTranslation?CaptionAssembler.ReadAsync(rawTranscripts.Reader,workStop.Token,3.35):CaptionAssembler.ReadAsync(rawTranscripts.Reader,workStop.Token);
            await foreach (var caption in captions) OnTranscript(caption);
        }
        catch (OperationCanceledException) when (workStop.IsCancellationRequested) { }
        catch (Exception ex) { Failed?.Invoke(ex); }
        finally { translations.Writer.TryComplete(); }
    }
    private async Task TranslateLoopAsync()
    {
        try
        {
            await foreach (var caption in translations.Reader.ReadAllAsync(workStop.Token))
            {
                // Batch accumulated segments so CLI startup does not compound subtitle lag.
                var batch = new List<Caption> { caption };
                while (apiTranslator == null && batch.Count < 6 && translations.Reader.TryRead(out var next)) batch.Add(next);
                batch.Sort((a, b) => a.Start.CompareTo(b.Start));
                Dictionary<int, string>? translated = null;
                try
                {
                    if (apiTranslator != null)
                    {
                        var text = await apiTranslator.TranslateAsync(caption,
                            text => Updated?.Invoke(caption with { Korean = text, State = "translating" }), workStop.Token);
                        translated = new() { [caption.Id] = text };
                    }
                    else translated=await translator!.TranslateBatchAsync(batch,[],workStop.Token);
                }
                catch (OperationCanceledException) when (workStop.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    Status?.Invoke(ex.Message);
                    try{File.AppendAllText(Path.Combine(store!.InternalFolder,"translation-errors.jsonl"),System.Text.Json.JsonSerializer.Serialize(new{time=DateTimeOffset.Now,ids=batch.Select(c=>c.Id),error=ex.Message})+Environment.NewLine);}catch(IOException){}
                }
                foreach (var c in batch)
                {
                    var result = translated == null ? c with { State = "failed" } : c with { Korean = translated[c.Id], State = "done" };
                    if (translated == null) Interlocked.Increment(ref failedTranslations);
                    store!.Save(result);
                    lock (gate) pending.Remove(c.Id);
                    Updated?.Invoke(result);
                }
            }
        }
        catch (OperationCanceledException) when (workStop.IsCancellationRequested) { }
        catch (Exception ex) { Failed?.Invoke(ex); }
    }
    public async Task StopAsync()
    {
        if (stopping) return;
        stopping = true;
        try
        {
            captureStop.Cancel();
            if (pump != null) await pump;
            await capture.DisposeAsync();
            recording?.Dispose();
            recording = null;
            if (sender != null)
            {
                try
                {
                    await sender.WaitAsync(TimeSpan.FromSeconds(8));
                    Status?.Invoke("남은 음성 인식 처리 중… (최대 120초)");
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                    await transcriber.DrainAsync(timeout.Token);
                }
                catch (Exception) { StopWarning = "마지막 음성 구간의 전사 완료를 확인하지 못했습니다."; }
            }
            await transcriber.DisposeAsync();
            rawTranscripts.Writer.TryComplete();
            if (assemblerTask != null) await assemblerTask;
            else translations.Writer.TryComplete();
            if(settings.ApiTranslation)await StopApiPreviewAsync();
            if (translatorTask != null)
            {
                Status?.Invoke("남은 번역 저장 중… (최대 15초)");
                try { await translatorTask.WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (TimeoutException) { StopWarning = "일부 번역이 미완료입니다. 원문은 저장했습니다."; }
            }
        }
        finally
        {
            workStop.Cancel();
            if(settings.ApiTranslation)await StopApiPreviewAsync();
            if (sender != null) await sender;
            if (assemblerTask != null) await assemblerTask;
            if (translatorTask != null) await translatorTask;
            apiTranslator?.Dispose();
            apiPreviewTranslator?.Dispose();
            lock (gate)
                foreach (var c in pending.Values)
                {
                    var interrupted = c with { State = "interrupted" };
                    store?.Save(interrupted);
                    if(settings.ApiTranslation)Updated?.Invoke(interrupted);
                }
            if (failedTranslations > 0)
                StopWarning = $"{failedTranslations}개 구간의 번역이 실패했습니다. 원문과 처리 상태는 저장했습니다.";
            store?.Dispose();
            captureStop.Dispose();
            workStop.Dispose();
        }
    }
}




