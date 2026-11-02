using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using NAudio.Wave;

namespace CatchLessons;

internal static class CloudTranscriberTests
{
    internal static async Task<string> LiveSmokeAsync()
    {
        var settings=Settings.Load();
        if(string.IsNullOrWhiteSpace(settings.GetKey()))return JsonSerializer.Serialize(new{skipped=true,reason="No saved OpenAI API key",cloudRequests=0});
        var fixture=Path.Combine(Environment.CurrentDirectory,"artifacts","jfk.wav");
        if(!File.Exists(fixture))throw new IOException("The public jfk.wav speech fixture is missing.");
        settings.SpeechEngine="openai";settings.SourceLanguage="auto";settings.StreamingPreview=true;settings.CaptionPace="balanced";
        settings.Topic="Public English speech transcription test";settings.Terms="";
        await using var transcriber=new CloudTranscriber();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var clock=new Stopwatch();var drafts=new ConcurrentQueue<TranscriptPreview>();var captions=new ConcurrentQueue<Caption>();
        double? firstDelta=null;bool beforeCommit=false;
        transcriber.Partial+=p=>{if(firstDelta==null){firstDelta=clock.Elapsed.TotalSeconds;beforeCommit=transcriber.CommittedTurns==0;}drafts.Enqueue(p);};
        transcriber.Completed+=captions.Enqueue;
        await transcriber.InitializeAsync(settings,timeout.Token);
        using var reader=new WaveFileReader(fixture);
        if(reader.WaveFormat.SampleRate!=16000||reader.WaveFormat.Channels!=1||reader.WaveFormat.BitsPerSample!=16)throw new IOException("Fixture must be mono PCM16 at 16 kHz.");
        clock.Start();long bytes=0;var buffer=new byte[1600];int count;
        while((count=reader.Read(buffer,0,buffer.Length))>0)
        {
            var pcm=buffer[..count];var peak=LocalTranscriber.Samples(pcm).Select(Math.Abs).DefaultIfEmpty().Max();
            await transcriber.PushAsync(new(pcm,bytes/32000d,(bytes+count)/32000d,peak),timeout.Token);bytes+=count;
            var remaining=TimeSpan.FromSeconds(bytes/32000d)-clock.Elapsed;if(remaining>TimeSpan.Zero)await Task.Delay(remaining,timeout.Token);
        }
        await transcriber.DrainAsync(timeout.Token);
        var transcript=string.Join(" ",captions.Select(c=>c.Original));
        if(!beforeCommit||drafts.Count<2||!transcript.Contains("country",StringComparison.OrdinalIgnoreCase))throw new IOException("Live transcription did not provide progressive source text before commit or match the public speech fixture.");
        return JsonSerializer.Serialize(new{passed=true,model=CloudTranscriber.Model,firstDeltaSeconds=firstDelta,partialBeforeCommit=beforeCommit,previewEvents=drafts.Count,finalCaptions=captions.Count,transcript,audioSeconds=reader.TotalTime.TotalSeconds,actualApi=true,source="public prerecorded speech, paced in real time; no system audio captured"});
    }

    private sealed class LiveSocket : WebSocket
    {
        private readonly Channel<byte[]> incoming=Channel.CreateUnbounded<byte[]>();
        private WebSocketState state=WebSocketState.Open;
        public int Appends { get; private set; }
        public int Commits { get; private set; }
        public bool EmitDrafts { get; init; }=true;
        public bool AutoComplete { get; init; }=true;
        public JsonElement Configuration { get; private set; }
        public override WebSocketState State=>state;
        public override WebSocketCloseStatus? CloseStatus=>null;
        public override string? CloseStatusDescription=>null;
        public override string? SubProtocol=>null;
        public void Emit(object value)=>incoming.Writer.TryWrite(JsonSerializer.SerializeToUtf8Bytes(value));
        public override Task SendAsync(ArraySegment<byte> buffer,WebSocketMessageType messageType,bool endOfMessage,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var json=JsonDocument.Parse(buffer.AsMemory());var e=json.RootElement;
            switch(e.GetProperty("type").GetString())
            {
                case "session.update":
                    Configuration=e.GetProperty("session").Clone();Emit(new{type="session.updated"});break;
                case "input_audio_buffer.append":
                    Appends++;
                    if(EmitDrafts&&Appends<=2)Emit(new{type="conversation.item.input_audio_transcription.delta",item_id="turn_1",delta=Appends==1?"We are":" reviewing a sentence"});
                    break;
                case "input_audio_buffer.commit":
                    var id="turn_"+(++Commits);Emit(new{type="input_audio_buffer.committed",item_id=id});
                    if(AutoComplete)Emit(new{type="conversation.item.input_audio_transcription.completed",item_id=id,transcript="We are reviewing a sentence."});
                    break;
            }
            return Task.CompletedTask;
        }
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer,CancellationToken token)
        {
            var bytes=await incoming.Reader.ReadAsync(token);bytes.AsSpan().CopyTo(buffer.AsSpan());
            return new(bytes.Length,WebSocketMessageType.Text,true);
        }
        public override void Abort(){state=WebSocketState.Aborted;incoming.Writer.TryComplete();}
        public override void Dispose(){state=WebSocketState.Closed;incoming.Writer.TryComplete();}
        public override Task CloseAsync(WebSocketCloseStatus status,string? description,CancellationToken token){Dispose();return Task.CompletedTask;}
        public override Task CloseOutputAsync(WebSocketCloseStatus status,string? description,CancellationToken token){Dispose();return Task.CompletedTask;}
    }

    internal static async Task<string> RunAsync()
    {
        int passed=0;void Check(bool result,string description){if(!result)throw new Exception(description);passed++;}
        var settings=new Settings{SpeechEngine="openai",TranslationProvider="openai",SourceLanguage="auto",Topic="TypeScript",Terms="dictionary\nresponse"};
        settings.SetKey("test-key");
        Check(settings.Segmentation==(1.2,12),"Live API finalization uses natural utterances instead of three-second cuts");
        foreach(var pace in new[]{"fast","balanced","context"})
        {
            var local=new Settings{CaptionPace=pace};var cloud=new Settings{SpeechEngine="openai",TranslationProvider="openai",CaptionPace=pace};
            Check(local.Segmentation==cloud.Segmentation,"Whisper and Agent boundary presets remain unchanged");
        }
        var socket=new LiveSocket();
        await using(var transcriber=new CloudTranscriber(socket))
        {
            var captions=new ConcurrentQueue<Caption>();var previews=new ConcurrentQueue<TranscriptPreview>();
            var draft=new TaskCompletionSource<TranscriptPreview>(TaskCreationOptions.RunContinuationsAsynchronously);
            var final=new TaskCompletionSource<Caption>(TaskCreationOptions.RunContinuationsAsynchronously);
            transcriber.Partial+=p=>{previews.Enqueue(p);if(p.Text=="We are reviewing a sentence")draft.TrySetResult(p);};
            transcriber.Completed+=c=>{captions.Enqueue(c);final.TrySetResult(c);};
            await transcriber.InitializeAsync(settings,CancellationToken.None);
            var input=socket.Configuration.GetProperty("audio").GetProperty("input");var config=input.GetProperty("transcription");
            Check(config.GetProperty("model").GetString()==CloudTranscriber.Model&&config.GetProperty("delay").GetString()=="low","Automatic language mode selects the live model and low delay");
            Check(!config.TryGetProperty("languages",out _)&&config.GetProperty("keywords").GetArrayLength()==2&&config.GetProperty("prompt").GetString()=="TypeScript","Auto mode omits language hints while preserving topic and vocabulary");
            Check(input.GetProperty("format").GetProperty("rate").GetInt32()==24000&&input.GetProperty("turn_detection").ValueKind==JsonValueKind.Null,"Live session uses supported PCM format and client-side finalization");
            for(int i=0;i<80;i++)await transcriber.PushAsync(new(new byte[1600],i*.05,(i+1)*.05,.1f),CancellationToken.None);
            var beforeCommit=await draft.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(socket.Commits==0&&captions.IsEmpty&&beforeCommit.End>0,"Incremental source text reaches the UI before any commit or final caption");
            Check(previews.Any(p=>p.Text=="We are")&&previews.Any(p=>p.Text=="We are reviewing a sentence"),"Incoming deltas grow the same source draft");
            for(int i=80;i<106;i++)await transcriber.PushAsync(new(new byte[1600],i*.05,(i+1)*.05,0),CancellationToken.None);
            var result=await final.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(socket.Commits==1&&captions.Count==1&&result.Id==1&&result.Start==0&&result.End>5,"A natural pause creates one final caption with the original timeline");
            Check(result.Original=="We are reviewing a sentence."&&previews.Any(p=>p.Text==result.Original),"Final source replaces the provisional text without creating another history row");
            await transcriber.DrainAsync(CancellationToken.None);
            Check(socket.Commits==1,"Draining an already finalized utterance does not commit twice");
        }
        var orderedSocket=new LiveSocket{AutoComplete=false,EmitDrafts=false};
        settings.SourceLanguage="en";settings.StreamingPreview=false;
        await using(var transcriber=new CloudTranscriber(orderedSocket))
        {
            var captions=new ConcurrentQueue<Caption>();int previewCount=0;
            var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            transcriber.Partial+=_=>Interlocked.Increment(ref previewCount);
            transcriber.Completed+=c=>{captions.Enqueue(c);if(captions.Count==2)done.TrySetResult();};
            await transcriber.InitializeAsync(settings,CancellationToken.None);
            Check(orderedSocket.Configuration.GetProperty("audio").GetProperty("input").GetProperty("transcription").GetProperty("languages")[0].GetString()=="en","Fixed source language remains an explicit hint for the live model");
            await transcriber.PushAsync(new(new byte[3200],0,.1,.1f),CancellationToken.None);await transcriber.FlushPendingAsync(CancellationToken.None);
            await transcriber.PushAsync(new(new byte[3200],10,10.1,.1f),CancellationToken.None);await transcriber.FlushPendingAsync(CancellationToken.None);
            orderedSocket.Emit(new{type="conversation.item.input_audio_transcription.completed",item_id="turn_2",transcript="Second."});
            orderedSocket.Emit(new{type="conversation.item.input_audio_transcription.completed",item_id="turn_1",transcript="First."});
            await done.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var rows=captions.ToArray();
            Check(rows.Select(c=>c.Original).SequenceEqual(new[]{"First.","Second."})&&rows[0].End==.1&&rows[1].Start==10,"Out-of-order final events retain committed item order and audio spans");
            Check(previewCount==0,"Turning previews off suppresses interim text but keeps final captions");
            await transcriber.DrainAsync(CancellationToken.None);
        }
        return JsonSerializer.Serialize(new{passed,transport="mock live WebSocket",cloudRequests=0});
    }
}
