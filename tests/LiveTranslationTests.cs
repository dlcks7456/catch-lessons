using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using NAudio.Wave;

namespace CatchLessons;

internal static class LiveTranslationTests
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private const string FinalSource="We need to review the complete sentence.";
    private const string Completed="data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\"}}\n\n";
    private static void Set(CaptionSession session,string name,object value)=>typeof(CaptionSession).GetField(name,Private)!.SetValue(session,value);
    private static Task Run(CaptionSession session,string method,params object[] args)=>(Task)typeof(CaptionSession).GetMethod(method,Private)!.Invoke(session,args)!;
    private static CaptionSession Session(Settings settings,OpenAiTranslator final,OpenAiTranslator preview,SessionStore store)
    {
        var session=new CaptionSession(settings);Set(session,"apiTranslator",final);Set(session,"apiPreviewTranslator",preview);Set(session,"store",store);return session;
    }
    internal static async Task<string> LiveSmokeAsync()
    {
        var settings=Settings.Load();
        if(string.IsNullOrWhiteSpace(settings.GetKey()))return JsonSerializer.Serialize(new{skipped=true,reason="No saved OpenAI API key",actualApi=false});
        settings.SpeechEngine="openai";settings.TranslationProvider="openai";settings.TranscriptionOnly=false;
        settings.SourceLanguage="auto";settings.TargetLanguage="ko";settings.StreamingPreview=true;settings.CaptionPace="balanced";
        settings.Topic="Public English speech";settings.Terms="";settings.CustomInstruction="Use polite Korean. Preserve names, negation and meaning.";
        var root=Path.Combine(Environment.CurrentDirectory,"artifacts","live-translation-api",Guid.NewGuid().ToString("N"));
        var store=new SessionStore(root,"ko");var folder=store.Folder;
        var final=new OpenAiTranslator(settings);var session=Session(settings,final,final.CreatePreviewTranslator(),store);
        await using var transcriber=new CloudTranscriber();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var clock=new Stopwatch();double? firstSource=null,firstTranslation=null;bool beforeCommit=false;string previous="";
        var generations=new ConcurrentDictionary<long,byte>();
        transcriber.Partial+=p=>{firstSource??=clock.Elapsed.TotalSeconds;session.OnTranscriptPreview(p);};
        transcriber.Completed+=session.OnRawTranscript;
        session.TranslationPreview+=p=>
        {
            if(string.IsNullOrWhiteSpace(p.Text))return;
            if(firstTranslation==null){firstTranslation=clock.Elapsed.TotalSeconds;beforeCommit=transcriber.CommittedTurns==0;}
            if(p.Text!=previous){generations.TryAdd(p.Revision,0);previous=p.Text;}
        };
        Set(session,"assemblerTask",Run(session,"AssembleAsync"));Set(session,"translatorTask",Run(session,"TranslateLoopAsync"));
        try
        {
            await transcriber.InitializeAsync(settings,timeout.Token);
            using var reader=new WaveFileReader(Path.Combine(Environment.CurrentDirectory,"artifacts","jfk.wav"));
            if(reader.WaveFormat.SampleRate!=16000||reader.WaveFormat.Channels!=1||reader.WaveFormat.BitsPerSample!=16)throw new IOException("Fixture must be mono PCM16 at 16 kHz.");
            clock.Start();long bytes=0;var buffer=new byte[1600];int count;
            while((count=reader.Read(buffer,0,buffer.Length))>0)
            {
                var pcm=buffer[..count];var peak=LocalTranscriber.Samples(pcm).Select(Math.Abs).DefaultIfEmpty().Max();
                await transcriber.PushAsync(new(pcm,bytes/32000d,(bytes+count)/32000d,peak),timeout.Token);bytes+=count;
                var remaining=TimeSpan.FromSeconds(bytes/32000d)-clock.Elapsed;if(remaining>TimeSpan.Zero)await Task.Delay(remaining,timeout.Token);
            }
            await transcriber.DrainAsync(timeout.Token);
        }
        finally{await session.StopAsync();}
        using var saved=SessionStore.OpenExisting(folder);var rows=saved.Read();
        if(!beforeCommit||generations.Count<2||rows.Count!=1||rows[0].State!="done"||!rows[0].Korean.Any(c=>c>='가'&&c<='힣'))
            throw new IOException("Expected multiple live translation updates before source commit and one finalized Korean caption.");
        return JsonSerializer.Serialize(new{passed=true,actualApi=true,firstSourceSeconds=firstSource,firstTranslationSeconds=firstTranslation,translationBeforeSourceCommit=beforeCommit,translationGenerations=generations.Count,finalCaptions=rows.Count,finalTranslation=rows[0].Korean,folder,source="public 11-second speech paced in real time; no user recording or system audio"});
    }
    private sealed record Request(string Json,string Source,long Started);
    private sealed class Replies(bool blockFirst=false,int firstDelayMilliseconds=0) : HttpMessageHandler
    {
        public readonly ConcurrentQueue<Request> Requests=new();
        public readonly TaskCompletionSource Canceled=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Active,Maximum;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.Headers.Authorization?.ToString()!="Bearer test-key")throw new Exception("Shared key missing");
            var json=await request.Content!.ReadAsStringAsync(token);using var body=JsonDocument.Parse(json);
            using var input=JsonDocument.Parse(body.RootElement.GetProperty("input").GetString()!);
            var text=input.RootElement.GetProperty("current").GetString()!;
            Requests.Enqueue(new(json,text,Stopwatch.GetTimestamp()));int number=Requests.Count;
            if(number==1&&firstDelayMilliseconds>0)
            {
                try{await Task.Delay(firstDelayMilliseconds,token);}catch(OperationCanceledException){Canceled.TrySetResult();throw;}
            }
            var translated=text==FinalSource?"최종 번역":"미리보기 "+number;
            var sse="data: "+JsonSerializer.Serialize(new{type="response.output_text.delta",delta=translated})+"\n\n";
            bool blocked=blockFirst&&number==1;
            return new(HttpStatusCode.OK){Content=new StreamContent(new ReplyStream(this,Encoding.UTF8.GetBytes(sse+(blocked?"":Completed)),blocked))};
        }
        private sealed class ReplyStream : MemoryStream
        {
            private readonly Replies owner;private readonly bool blocked;private int disposed;
            public ReplyStream(Replies owner,byte[] bytes,bool blocked):base(bytes)
            {
                this.owner=owner;this.blocked=blocked;int active=Interlocked.Increment(ref owner.Active);
                int maximum;do{maximum=Volatile.Read(ref owner.Maximum);if(active<=maximum)break;}while(Interlocked.CompareExchange(ref owner.Maximum,active,maximum)!=maximum);
            }
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)
            {
                if(blocked&&Position>=Length)
                {
                    try{await Task.Delay(Timeout.Infinite,token);}catch(OperationCanceledException){owner.Canceled.TrySetResult();throw;}
                }
                return await base.ReadAsync(buffer,token);
            }
            protected override void Dispose(bool disposing){if(Interlocked.Exchange(ref disposed,1)==0)Interlocked.Decrement(ref owner.Active);base.Dispose(disposing);}
        }
    }

    internal static async Task<string> RunAsync()
    {
        int passed=0;void Check(bool value,string message){if(!value)throw new Exception(message);passed++;}
        var settings=new Settings{SpeechEngine="openai",TranslationProvider="openai",Topic="Software design",Terms="CatchLessons",CustomInstruction="Use polite Korean and preserve identifiers."};
        var reply=new Replies();var final=new OpenAiTranslator(settings,"test-key",reply);var preview=new OpenAiTranslator(settings,"test-key",reply);
        var history=(Queue<Caption>)typeof(OpenAiTranslator).GetField("context",Private)!.GetValue(final)!;
        history.Enqueue(new(1,"confirmed",0,1,"Earlier context","확정 문맥"));
        var store=new SessionStore(Path.Combine(Environment.CurrentDirectory,"artifacts","live-translation-tests",Guid.NewGuid().ToString("N")));
        var session=Session(settings,final,preview,store);
        var first=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var second=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var future=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var nextTurn=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int sourceEvents=0;session.Partial+=_=>sourceEvents++;
        session.TranslationPreview+=p=>{if(p.Text=="미리보기 1")first.TrySetResult();if(p.Text=="미리보기 2")second.TrySetResult();if(p.Text=="미리보기 3")future.TrySetResult();if(p.Text=="미리보기 5")nextTurn.TrySetResult();};
        try
        {
            session.OnTranscriptPreview(new("We need to review the first version",2,1));
            await first.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Check(reply.Requests.Count==1&&store.Read().Count==0,"Translation starts from an uncommitted source preview without creating saved captions");
            string latest="";
            for(int i=0;i<20;i++){latest="We need to review the first version "+new string('x',30+i*10);session.OnTranscriptPreview(new(latest,3+i*.1,1));}
            await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var requests=reply.Requests.ToArray();
            Check(requests.Length==2&&requests[1].Source==latest,"Many incoming source deltas coalesce into one newest translation snapshot");
            Check(Stopwatch.GetElapsedTime(requests[0].Started,requests[1].Started).TotalSeconds>=1.9,"Preview requests are paced at least two seconds apart");
            Check(sourceEvents==21&&reply.Maximum==1,"Audio/source updates continue while only one translation stream is in flight");
            foreach(var request in requests)
            {
                using var body=JsonDocument.Parse(request.Json);using var input=JsonDocument.Parse(body.RootElement.GetProperty("input").GetString()!);
                Check(body.RootElement.GetProperty("instructions").GetString()!.Contains(settings.CustomInstruction)&&input.RootElement.GetProperty("Topic").GetString()==settings.Topic,"Live requests preserve Custom Instruction and topic");
                Check(input.RootElement.GetProperty("context").GetArrayLength()==1&&input.RootElement.GetProperty("context")[0].GetProperty("translated").GetString()=="확정 문맥","Each interim request uses only the confirmed context snapshot");
            }
            Check(history.Count==1,"Repeated interim translations do not enter confirmed context");
            session.OnTranscriptPreview(new("A newer utterance follows the confirmed sentence",8,2));
            await future.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Run(session,"OnTranscriptAsync",new Caption(1,"first",0,6,FinalSource));
            session.OnTranscriptPreview(new("A stale source snapshot must be ignored",5.5,1));
            await Task.Delay(2100);
            Check(reply.Requests.Count==3,"Final translation reservation suppresses previews, retains the future utterance and rejects the finalized source");
            var channel=(Channel<Caption>)typeof(CaptionSession).GetField("translations",Private)!.GetValue(session)!;channel.Writer.TryComplete();
            await Run(session,"TranslateLoopAsync");await nextTurn.Task.WaitAsync(TimeSpan.FromSeconds(5));
            requests=reply.Requests.ToArray();
            Check(requests[3].Source==FinalSource&&requests[4].Source.StartsWith("A newer utterance",StringComparison.Ordinal),"An older finalized turn preempts but does not discard the newer live utterance");
            using(var body=JsonDocument.Parse(requests[4].Json))using(var input=JsonDocument.Parse(body.RootElement.GetProperty("input").GetString()!))
                Check(input.RootElement.GetProperty("context").GetArrayLength()==2,"The next live preview receives newly confirmed translation context");
            Check(store.Read().Single().Korean=="최종 번역"&&store.Read().Single().State=="done","Only the final translation is persisted/exported");
            Check(reply.Maximum==1,"Final and preview requests never overlap");
        }
        finally{await session.StopAsync();}

        var blocked=new Replies(blockFirst:true);var canceledSession=Session(settings,new(settings,"test-key",blocked),new(settings,"test-key",blocked),new SessionStore(Path.Combine(Environment.CurrentDirectory,"artifacts","live-translation-tests",Guid.NewGuid().ToString("N"))));
        var oldText=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var replacement=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        canceledSession.TranslationPreview+=p=>{if(p.Text=="미리보기 1")oldText.TrySetResult();if(p.Text=="미리보기 2")replacement.TrySetResult();};
        try
        {
            canceledSession.OnTranscriptPreview(new("The first draft is still being translated",2,1));await oldText.Task.WaitAsync(TimeSpan.FromSeconds(4));
            await Task.Delay(2100);canceledSession.OnTranscriptPreview(new("The first draft is still being translated with substantially more context",5,1));
            await replacement.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(blocked.Canceled.Task.IsCompleted&&blocked.Requests.Count==2&&blocked.Maximum==1,"Superseding a slow request cancels its stream before starting the newest snapshot");
        }
        finally{await canceledSession.StopAsync();}

        var cold=new Replies(firstDelayMilliseconds:3000);var coldSession=Session(settings,new(settings,"test-key",cold),new(settings,"test-key",cold),new SessionStore(Path.Combine(Environment.CurrentDirectory,"artifacts","live-translation-tests",Guid.NewGuid().ToString("N"))));
        var coldFirst=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var coldNext=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coldSession.TranslationPreview+=p=>{if(p.Text=="미리보기 1")coldFirst.TrySetResult();if(p.Text=="미리보기 2")coldNext.TrySetResult();};
        try
        {
            coldSession.OnTranscriptPreview(new("A cold model is still preparing its first response",2,1));
            await Task.Delay(2150);coldSession.OnTranscriptPreview(new("A cold model is still preparing its first response while more words arrive",5,1));
            await coldFirst.Task.WaitAsync(TimeSpan.FromSeconds(4));await coldNext.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Check(!cold.Canceled.Task.IsCompleted&&cold.Requests.Count==2,"Slow first-token responses are not repeatedly canceled before any translation can appear");
        }
        finally{await coldSession.StopAsync();}

        foreach(bool useApi in new[]{true,false})
        {
            var disabled=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;
            disabled.StreamingPreview=!useApi;disabled.TranslationProvider=useApi?"openai":"agent";
            var unused=new Replies();var quiet=Session(disabled,new(disabled,"test-key",unused),new(disabled,"test-key",unused),new SessionStore(Path.Combine(Environment.CurrentDirectory,"artifacts","live-translation-tests",Guid.NewGuid().ToString("N"))));
            try{quiet.OnTranscriptPreview(new("This draft must not produce a preview request",2,1));await Task.Delay(80);Check(unused.Requests.Count==0,"Disabled previews and Agent translation keep their existing behavior");}
            finally{await quiet.StopAsync();}
        }
        return JsonSerializer.Serialize(new{passed,cloudRequests=0,transport="mock live source + SSE"});
    }
}
