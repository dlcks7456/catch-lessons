using System.Threading.Channels;
using NAudio.Wave;

namespace CatchLessons;

public sealed record ApiTranslationPreview(long Revision, double End, string Text);

public sealed class CaptionSession
{
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
    private readonly SemaphoreSlim apiRequestGate=new(1,1);
    private readonly object apiPreviewGate=new();
    private CancellationTokenSource? apiPreviewCancellation;
    private Task? apiPreviewTask;
    private int apiPreviewGroupId, pendingApiFinalTranslations;
    private double apiPreviewEnd;
    private long apiPreviewRevision;
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
    private void StartApiPreview(Caption caption)
    {
        long revision;CancellationTokenSource request;
        lock(apiPreviewGate)
        {
            if(apiPreviewGroupId!=0||pendingApiFinalTranslations!=0||apiTranslator==null||apiPreviewTranslator==null)return;
            apiPreviewGroupId=caption.Id;apiPreviewEnd=caption.End;revision=++apiPreviewRevision;
            request=CancellationTokenSource.CreateLinkedTokenSource(workStop.Token);apiPreviewCancellation=request;
            TranslationPreview?.Invoke(new(revision,caption.End,""));
            apiPreviewTask=Task.Run(async()=>
            {
                var acquired=false;
                try
                {
                    await apiRequestGate.WaitAsync(request.Token);acquired=true;
                    if(IsCurrentApiPreview(caption.Id,revision,request))
                    {
                        apiPreviewTranslator!.ResetPreviewContext(apiTranslator!);
                        await apiPreviewTranslator.TranslateAsync(caption,text=>
                            {if(IsCurrentApiPreview(caption.Id,revision,request))TranslationPreview?.Invoke(new(revision,caption.End,text));},request.Token);
                    }
                }
                catch(OperationCanceledException)when(request.IsCancellationRequested||workStop.IsCancellationRequested){}
                catch(Exception){}
                finally{if(acquired)apiRequestGate.Release();}
            });
        }
    }
    private bool IsCurrentApiPreview(int captionId,long revision,CancellationTokenSource request)
    {
        lock(apiPreviewGate)return apiPreviewGroupId==captionId&&apiPreviewRevision==revision&&ReferenceEquals(apiPreviewCancellation,request)&&!request.IsCancellationRequested;
    }
    private async Task BeginApiFinalTranslationAsync(Caption caption)
    {
        CancellationTokenSource? request;Task? preview;long revision;double end;
        lock(apiPreviewGate)
        {
            pendingApiFinalTranslations++;revision=++apiPreviewRevision;end=apiPreviewEnd;
            request=apiPreviewCancellation;apiPreviewCancellation=null;apiPreviewGroupId=0;
            preview=apiPreviewTask;apiPreviewTask=null;
        }
        request?.Cancel();
        if(request!=null)TranslationPreview?.Invoke(new(revision,Math.Max(caption.End,end),""));
        if(preview!=null)await preview;
        request?.Dispose();
    }
    private async Task StopApiPreviewAsync()
    {
        CancellationTokenSource? request;Task? preview;long revision;double end;
        lock(apiPreviewGate)
        {
            request=apiPreviewCancellation;apiPreviewCancellation=null;preview=apiPreviewTask;apiPreviewTask=null;
            apiPreviewGroupId=0;revision=++apiPreviewRevision;end=apiPreviewEnd;
        }
        request?.Cancel();
        if(request!=null)TranslationPreview?.Invoke(new(revision,end,""));
        if(preview!=null)await preview;
        request?.Dispose();
    }
    private void CompleteApiFinalTranslation(){lock(apiPreviewGate)pendingApiFinalTranslations--;}
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
        transcriber.Partial += text => Partial?.Invoke(text);
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
        File.WriteAllText(Path.Combine(store.InternalFolder, "session.json"), System.Text.Json.JsonSerializer.Serialize(new { sourceLanguage = settings.SourceLanguage, targetLanguage = settings.TargetLanguage, mode = settings.TranscriptionOnly ? "transcription" : "translation", translationProvider = settings.TranslationProvider, translationModel = settings.ApiTranslation ? OpenAiTranslator.Model : settings.AgentModel, engine = settings.SpeechEngine, model = settings.SpeechEngine == "openai" ? (settings.SourceLanguage == "auto" ? "gpt-transcribe" : "gpt-live-transcribe") : LocalModels.Resolve(settings.LocalModel).File, runtime = transcriber.RuntimeInfo }));
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
            if(settings.ApiTranslation&&settings.StreamingPreview&&caption.Original.Length>=20&&CaptionAssembler.NeedsContinuation(caption.Original))StartApiPreview(caption);
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
    private async Task OnTranscriptAsync(Caption caption)
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
        Updated?.Invoke(caption);
        if(settings.ApiTranslation)await BeginApiFinalTranslationAsync(caption);
        if (!translations.Writer.TryWrite(caption))
        {
            if(settings.ApiTranslation)CompleteApiFinalTranslation();
            store.Save(caption with { State = "queue_full" });
            Failed?.Invoke(new IOException("번역 대기열이 가득 찼습니다. 원문은 저장했습니다."));
        }
    }
    private async Task AssembleAsync()
    {
        try
        {
            var captions=settings.ApiTranslation?CaptionAssembler.ReadAsync(rawTranscripts.Reader,workStop.Token,3.35):CaptionAssembler.ReadAsync(rawTranscripts.Reader,workStop.Token);
            await foreach (var caption in captions)await OnTranscriptAsync(caption);
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
                bool apiRequestQueued=false;
                try
                {
                    if (apiTranslator != null)
                    {
                        apiRequestQueued=true;
                        await apiRequestGate.WaitAsync(workStop.Token);
                        try
                        {
                            var text = await apiTranslator.TranslateAsync(caption,
                                text => Updated?.Invoke(caption with { Korean = text, State = "translating" }), workStop.Token);
                            translated = new() { [caption.Id] = text };
                        }
                        finally{apiRequestGate.Release();}
                    }
                    else translated=await translator!.TranslateBatchAsync(batch,[],workStop.Token);
                }
                catch (OperationCanceledException) when (workStop.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    Status?.Invoke(ex.Message);
                    try{File.AppendAllText(Path.Combine(store!.InternalFolder,"translation-errors.jsonl"),System.Text.Json.JsonSerializer.Serialize(new{time=DateTimeOffset.Now,ids=batch.Select(c=>c.Id),error=ex.Message})+Environment.NewLine);}catch(IOException){}
                }
                finally{if(apiRequestQueued)CompleteApiFinalTranslation();}
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
            apiRequestGate.Dispose();
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




