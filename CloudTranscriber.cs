using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CatchLessons;

public sealed class CloudTranscriber : ITranscriber
{
    private readonly ClientWebSocket socket = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<(int Id,double Start,double End)> commits = new();
    private readonly Dictionary<string,(int Id,double Start,double End)> items = new();
    private readonly Dictionary<string,string> drafts = new();
    private readonly SortedDictionary<int,Caption> completed = new();
    private readonly Queue<AudioFrame> preroll = new();
    private readonly BufferedWaveProvider buffer = new(AudioCapture.Format) { ReadFully = true };
    private WdlResamplingSampleProvider? resampler;
    private Settings settings = new();
    private Task? receiver;
    private Exception? failure;
    private bool active;
    private int nextId, nextComplete = 1, pending;
    private double start,end,lastVoice;
    public string RuntimeInfo => "OpenAI Realtime transcription";
    public event Action<Caption>? Completed;
    public event Action<TranscriptPreview>? Partial;
    public event Action<string[]>? LanguagesDetected;
    public event Action<string>? Status;
    public event Action<Exception>? Failed;
    public async Task InitializeAsync(Settings configuration, CancellationToken token)
    {
        settings = configuration;
        var key = settings.GetKey();
        if (string.IsNullOrWhiteSpace(key)) throw new IOException("설정에서 OpenAI API 키를 입력하세요.");
        resampler = new WdlResamplingSampleProvider(buffer.ToSampleProvider(),24000);
        socket.Options.SetRequestHeader("Authorization","Bearer " + key);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
        Status?.Invoke("OpenAI 음성 인식 연결 중…");
        await socket.ConnectAsync(new Uri("wss://api.openai.com/v1/realtime?intent=transcription"),timeout.Token);
        receiver=Task.Run(ReceiveAsync);
        var transcription = new Dictionary<string,object> { ["model"] = settings.SourceLanguage=="auto" ? "gpt-transcribe" : "gpt-live-transcribe", ["prompt"]=settings.Topic, ["keywords"]=settings.Keywords };
        if(settings.SourceLanguage!="auto") { transcription["languages"]=new[]{settings.SourceLanguage}; transcription["delay"]="low"; }
        await Send(new { type="session.update",session=new { type="transcription",audio=new { input=new { format=new { type="audio/pcm",rate=24000 },transcription,turn_detection=(object?)null } } } },timeout.Token);
        await ready.Task.WaitAsync(timeout.Token);
    }
    public async Task PushAsync(AudioFrame frame,CancellationToken token)
    {
        if(Volatile.Read(ref failure) is { } error)throw new IOException("OpenAI speech recognition connection failed.",error);
        bool voice=frame.Peak>=.008f;
        if(!active)
        {
            preroll.Enqueue(frame); while(preroll.Count>6) preroll.Dequeue(); if(!voice)return;
            active=true; start=preroll.Peek().Start; lastVoice=frame.End;
            foreach(var f in preroll) await Append(f,token); preroll.Clear();
        }
        else await Append(frame,token);
        end=frame.End; if(voice) lastVoice=end;
        if(end-lastVoice>=settings.Segmentation.Silence || end-start>=settings.Segmentation.Maximum) await Commit(token);
    }
    private async Task Append(AudioFrame frame,CancellationToken token)
    {
        buffer.AddSamples(frame.Pcm,0,frame.Pcm.Length);
        var samples=new float[frame.Pcm.Length/2*3/2]; int count=resampler!.Read(samples,0,samples.Length); var pcm=new byte[count*2];
        for(int i=0;i<count;i++) System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i*2,2),(short)(Math.Clamp(samples[i],-1,1)*32767));
        await Send(new { type="input_audio_buffer.append",audio=Convert.ToBase64String(pcm) },token);
    }
    private async Task Commit(CancellationToken token)
    {
        if(!active)return;
        if(pending>=20)throw new IOException("음성 인식 응답이 밀렸습니다.");
        if(end-start<.1) await Send(new { type="input_audio_buffer.append",audio=Convert.ToBase64String(new byte[4800]) },token);
        commits.Enqueue((++nextId,start,end)); Interlocked.Increment(ref pending); active=false;
        await Send(new { type="input_audio_buffer.commit" },token);
    }
    public async Task DrainAsync(CancellationToken token)
    {
        if(Volatile.Read(ref failure) is { } error)throw new IOException("OpenAI speech recognition connection failed.",error);
        await Commit(token);
        while(Volatile.Read(ref pending)>0){if(Volatile.Read(ref failure) is { } pendingError)throw new IOException("OpenAI speech recognition connection failed.",pendingError);await Task.Delay(50,token);}
    }
    public async Task FlushPendingAsync(CancellationToken token){await Commit(token);preroll.Clear();}
    private Task Send(object value,CancellationToken token) => socket.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(value)),WebSocketMessageType.Text,true,token);
    private async Task ReceiveAsync()
    {
        var bytes=new byte[16384];
        try
        {
            while(!lifetime.IsCancellationRequested)
            {
                using var message=new MemoryStream(); WebSocketReceiveResult part;
                do { part=await socket.ReceiveAsync(new ArraySegment<byte>(bytes),lifetime.Token); if(part.MessageType==WebSocketMessageType.Close)throw new IOException("OpenAI 연결이 종료됐습니다."); message.Write(bytes,0,part.Count); if(message.Length>2000000)throw new IOException("응답 크기 초과"); } while(!part.EndOfMessage);
                using var json=JsonDocument.Parse(message.ToArray()); var e=json.RootElement; var type=e.GetProperty("type").GetString();
                if(type=="session.updated")ready.TrySetResult();
                else if(type=="error")throw new IOException("OpenAI: "+e.GetProperty("error").GetProperty("message").GetString());
                else if(type=="input_audio_buffer.committed") { if(!commits.TryDequeue(out var span))throw new IOException("전사 구간 불일치"); items[e.GetProperty("item_id").GetString()!]=span; }
                else if(type=="conversation.item.input_audio_transcription.delta")
                {
                    var id=e.GetProperty("item_id").GetString()!; drafts.TryGetValue(id,out var text); drafts[id]=text+e.GetProperty("delta").GetString();
                    if(settings.StreamingPreview)Partial?.Invoke(new(drafts[id],items.TryGetValue(id,out var span)?span.End:end));
                }
                else if(type=="conversation.item.input_audio_transcription.completed")
                {
                    var id=e.GetProperty("item_id").GetString()!; if(!items.Remove(id,out var span))throw new IOException("전사 결과 불일치"); drafts.Remove(id);
                    completed[span.Id]=new(span.Id,id,span.Start,span.End,e.GetProperty("transcript").GetString()??"");
                    while(completed.Remove(nextComplete,out var caption)) { nextComplete++; if(!string.IsNullOrWhiteSpace(caption.Original))Completed?.Invoke(caption); }
                    if(e.TryGetProperty("languages",out var languages))LanguagesDetected?.Invoke(languages.EnumerateArray().Select(l=>l.GetProperty("code").GetString()!).ToArray());
                    Interlocked.Decrement(ref pending);
                }
                else if(type=="conversation.item.input_audio_transcription.failed")throw new IOException("OpenAI 전사 실패");
            }
        }
        catch(OperationCanceledException) when(lifetime.IsCancellationRequested) { }
        catch(Exception ex) { if(!lifetime.IsCancellationRequested){Interlocked.CompareExchange(ref failure,ex,null);ready.TrySetException(ex); Failed?.Invoke(ex);} }
    }
    public async ValueTask DisposeAsync() { lifetime.Cancel(); socket.Abort(); if(receiver!=null)await receiver;socket.Dispose();lifetime.Dispose(); }
}
