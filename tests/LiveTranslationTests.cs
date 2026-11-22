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
    private static void Invoke(CaptionSession session,string method,params object[] args)=>typeof(CaptionSession).GetMethod(method,Private)!.Invoke(session,args);
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
        var clock=new Stopwatch();double? firstSource=null,firstTranslation=null;bool beforeCommit=false;
        var preCommitDisplays=new ConcurrentDictionary<string,byte>(StringComparer.Ordinal);
        transcriber.Partial+=p=>{firstSource??=clock.Elapsed.TotalSeconds;session.OnTranscriptPreview(p);};
        transcriber.Completed+=session.OnRawTranscript;
        session.TranslationPreview+=p=>
        {
            if(string.IsNullOrWhiteSpace(p.Text))return;
            var preCommit=transcriber.CommittedTurns==0;
            if(firstTranslation==null){firstTranslation=clock.Elapsed.TotalSeconds;beforeCommit=preCommit;}
            if(preCommit)preCommitDisplays.TryAdd(p.Text,0);
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
        if(!beforeCommit||preCommitDisplays.Count<2||rows.Count!=1||rows[0].State!="done"||!rows[0].Korean.Any(c=>c>='가'&&c<='힣'))
            throw new IOException("Expected at least two distinct translation displays before source commit and one finalized Korean caption.");
        return JsonSerializer.Serialize(new{passed=true,actualApi=true,firstSourceSeconds=firstSource,firstTranslationSeconds=firstTranslation,translationBeforeSourceCommit=beforeCommit,preCommitTranslationDisplays=preCommitDisplays.Count,finalCaptions=rows.Count,finalTranslation=rows[0].Korean,folder,source="public 11-second speech paced in real time; no user recording or system audio"});
    }
    private sealed record Request(string Json,string Source,long Started);
    private sealed class Replies(int firstDelayMilliseconds=0,string? blockedSource=null,TaskCompletionSource? releaseBlockedSource=null,Func<string,int,string>? translate=null,Task? firstCompletion=null) : HttpMessageHandler
    {
        public readonly ConcurrentQueue<Request> Requests=new();
        public readonly TaskCompletionSource FirstRequestStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource BlockedSourceStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Active,Maximum,MaximumRequests,CanceledRequests;
        private int requestSequence,inFlight;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var number=Interlocked.Increment(ref requestSequence);
            UpdateMaximum(ref MaximumRequests,Interlocked.Increment(ref inFlight));
            try
            {
                if(request.Headers.Authorization?.ToString()!="Bearer test-key")throw new Exception("Shared key missing");
                var json=await request.Content!.ReadAsStringAsync(token);using var body=JsonDocument.Parse(json);
                using var input=JsonDocument.Parse(body.RootElement.GetProperty("input").GetString()!);
                var text=input.RootElement.GetProperty("current").GetString()!;
                Requests.Enqueue(new(json,text,Stopwatch.GetTimestamp()));
                if(number==1)FirstRequestStarted.TrySetResult();
                if(firstDelayMilliseconds>0&&number==1)await Task.Delay(firstDelayMilliseconds,token);
                if(text==blockedSource)
                {
                    BlockedSourceStarted.TrySetResult();
                    await releaseBlockedSource!.Task.WaitAsync(token);
                }
                var translated=translate?.Invoke(text,number)??(text==FinalSource?"최종 번역":"미리보기 "+number);
                var sse="data: "+JsonSerializer.Serialize(new{type="response.output_text.delta",delta=translated})+"\n\n";
                return new(HttpStatusCode.OK){Content=new StreamContent(new ReplyStream(this,Encoding.UTF8.GetBytes(sse+Completed),Encoding.UTF8.GetByteCount(sse),number==1?firstCompletion:null))};
            }
            catch(OperationCanceledException){Interlocked.Increment(ref CanceledRequests);throw;}
            finally{Interlocked.Decrement(ref inFlight);}
        }
        private static void UpdateMaximum(ref int maximum,int value)
        {
            int previous;do{previous=Volatile.Read(ref maximum);if(value<=previous)return;}while(Interlocked.CompareExchange(ref maximum,value,previous)!=previous);
        }
        private sealed class ReplyStream : MemoryStream
        {
            private readonly Replies owner;private int disposed;
            private readonly int deltaLength;
            private readonly Task? completion;
            public ReplyStream(Replies owner,byte[] bytes,int deltaLength,Task? completion):base(bytes)
            {
                this.deltaLength=deltaLength;this.completion=completion;
                this.owner=owner;int active=Interlocked.Increment(ref owner.Active);
                UpdateMaximum(ref owner.Maximum,active);
            }
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)
            {
                if (completion != null && !completion.IsCompleted)
                {
                    if (Position < deltaLength) return await base.ReadAsync(buffer[..Math.Min(buffer.Length,deltaLength-(int)Position)],token);
                    await completion.WaitAsync(token);
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
        const string updatedFuture="A newer utterance follows the confirmed sentence with one more useful detail";
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
            Invoke(session,"OnTranscript",new Caption(1,"first",0,6,FinalSource));
            session.OnTranscriptPreview(new("A stale source snapshot must be ignored",5.5,1));
            var channel=(Channel<Caption>)typeof(CaptionSession).GetField("translations",Private)!.GetValue(session)!;channel.Writer.TryComplete();
            await Run(session,"TranslateLoopAsync");
            Check(reply.Requests.Count==4&&reply.Requests.ToArray()[3].Source==FinalSource,"The completed source is finalized once while a newer live utterance remains eligible");
            session.OnTranscriptPreview(new(updatedFuture,8.7,2));
            await nextTurn.Task.WaitAsync(TimeSpan.FromSeconds(5));
            requests=reply.Requests.ToArray();
            Check(requests[4].Source==updatedFuture,"The completed older turn does not suppress updates for a later live turn");
            using(var body=JsonDocument.Parse(requests[4].Json))using(var input=JsonDocument.Parse(body.RootElement.GetProperty("input").GetString()!))
                Check(input.RootElement.GetProperty("context").GetArrayLength()==2,"The next live preview receives newly confirmed translation context");
            Check(store.Read().Single().Korean=="최종 번역"&&store.Read().Single().State=="done","Only the final translation is persisted/exported");
        }
        finally{await session.StopAsync();}

        var releaseFirstCompletion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progressive=new Replies(firstCompletion:releaseFirstCompletion.Task,translate:(source,_)=>source switch
        {
            "We learned about KB."=>"KB를 배웠습니다.",
            "It is really easy to use"=>"사용하기 정말 쉽습니다.",
            _=>"새로운 구절입니다."
        });
        var progressiveFinal=new OpenAiTranslator(settings,"test-key",progressive);var progressivePreview=new OpenAiTranslator(settings,"test-key",progressive);
        ((Queue<Caption>)typeof(OpenAiTranslator).GetField("context",Private)!.GetValue(progressiveFinal)!).Enqueue(new(1,"confirmed",0,1,"Earlier context","확정 문맥"));
        var progressiveStore=new SessionStore(Path.Combine(Environment.CurrentDirectory,"artifacts","live-translation-tests",Guid.NewGuid().ToString("N")));
        var progressiveSession=Session(settings,progressiveFinal,progressivePreview,progressiveStore);
        var firstClause=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var secondClause=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        progressiveSession.TranslationPreview+=p=>
        {
            if(p.Text=="KB를 배웠습니다.")firstClause.TrySetResult();
            if(p.Text=="KB를 배웠습니다."+Environment.NewLine+"사용하기 정말 쉽습니다.")secondClause.TrySetResult();
        };
        try
        {
            progressiveSession.OnTranscriptPreview(new("We learned about KB. It is really easy to use",2,1));
            await firstClause.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Check(progressive.Requests.Count==1&&!releaseFirstCompletion.Task.IsCompleted,"A readable clause appears before the preview response completes");
            releaseFirstCompletion.TrySetResult();
            await secondClause.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var requests=progressive.Requests.ToArray();
            Check(requests.Length==2&&requests[0].Source=="We learned about KB."&&requests[1].Source=="It is really easy to use","Live translation commits a completed phrase and translates the unfinished tail as more words arrive");
            using(var body=JsonDocument.Parse(requests[1].Json))using(var input=JsonDocument.Parse(body.RootElement.GetProperty("input").GetString()!))
            {
                var context=input.RootElement.GetProperty("context");
                Check(context.GetArrayLength()==2&&context[0].GetProperty("translated").GetString()=="확정 문맥"&&context[1].GetProperty("translated").GetString()=="KB를 배웠습니다.","The next phrase uses both confirmed history and the earlier live translation as context");
                Check(body.RootElement.GetProperty("instructions").GetString()!.Contains(settings.CustomInstruction),"Clause-based previews preserve the existing Custom Instruction");
            }
            Check(progressiveStore.Read().Count==0,"Progressive phrases remain provisional until the source caption is finalized");
        }
        finally{releaseFirstCompletion.TrySetResult();await progressiveSession.StopAsync();}

        var slow=new Replies(firstDelayMilliseconds:2600);var slowSession=Session(settings,new(settings,"test-key",slow),new(settings,"test-key",slow),new SessionStore(Path.Combine(Environment.CurrentDirectory,"artifacts","live-translation-tests",Guid.NewGuid().ToString("N"))));
        var oldText=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var replacement=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        slowSession.TranslationPreview+=p=>{if(p.Text=="미리보기 1")oldText.TrySetResult();if(p.Text=="미리보기 2")replacement.TrySetResult();};
        const string newest="The first draft is still being translated with substantially more context";
        try
        {
            slowSession.OnTranscriptPreview(new("The first draft is still being translated",2,1));
            await slow.FirstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(4));
            slowSession.OnTranscriptPreview(new(newest,5,1));
            Check(!oldText.Task.IsCompleted,"A newer source snapshot arrives while the current request is still working");
            await oldText.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await replacement.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var requests=slow.Requests.ToArray();
            Check(slow.CanceledRequests==0&&requests.Length==2&&requests[1].Source==newest&&slow.Maximum==1,"A slow request completes, then the worker translates the newest coalesced snapshot");
        }
        finally{await slowSession.StopAsync();}

        var releaseFinal=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var independent=new Replies(blockedSource:FinalSource,releaseBlockedSource:releaseFinal);
        var independentStore=new SessionStore(Path.Combine(Environment.CurrentDirectory,"artifacts","live-translation-tests",Guid.NewGuid().ToString("N")));
        var independentSession=Session(settings,new(settings,"test-key",independent),new(settings,"test-key",independent),independentStore);
        var laterPreview=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        independentSession.TranslationPreview+=p=>{if(p.TurnId==2&&!string.IsNullOrWhiteSpace(p.Text))laterPreview.TrySetResult();};
        Invoke(independentSession,"OnTranscript",new Caption(1,"final",0,6,FinalSource));
        var independentChannel=(Channel<Caption>)typeof(CaptionSession).GetField("translations",Private)!.GetValue(independentSession)!;
        independentChannel.Writer.TryComplete();
        var finalTask=Run(independentSession,"TranslateLoopAsync");
        try
        {
            await independent.BlockedSourceStarted.Task.WaitAsync(TimeSpan.FromSeconds(4));
            independentSession.OnTranscriptPreview(new("A later sentence keeps translating while an earlier result waits",8,2));
            await laterPreview.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(independent.MaximumRequests>=2&&independent.Requests.Any(request=>request.Source==FinalSource)&&independent.Requests.Any(request=>request.Source.StartsWith("A later sentence",StringComparison.Ordinal)),"A blocked final translation does not stall previews from the next live turn");
        }
        finally
        {
            releaseFinal.TrySetResult();
            await finalTask.WaitAsync(TimeSpan.FromSeconds(5));
            Check(independentStore.Read().Single().State=="done","The released final response still persists the completed caption");
            await independentSession.StopAsync();
        }

        var cold=new Replies(firstDelayMilliseconds:3000);var coldSession=Session(settings,new(settings,"test-key",cold),new(settings,"test-key",cold),new SessionStore(Path.Combine(Environment.CurrentDirectory,"artifacts","live-translation-tests",Guid.NewGuid().ToString("N"))));
        var coldFirst=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var coldNext=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coldSession.TranslationPreview+=p=>{if(p.Text=="미리보기 1")coldFirst.TrySetResult();if(p.Text=="미리보기 2")coldNext.TrySetResult();};
        try
        {
            coldSession.OnTranscriptPreview(new("A cold model is still preparing its first response",2,1));
            await Task.Delay(2150);coldSession.OnTranscriptPreview(new("A cold model is still preparing its first response while more words arrive",5,1));
            await coldFirst.Task.WaitAsync(TimeSpan.FromSeconds(4));await coldNext.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Check(cold.CanceledRequests==0&&cold.Requests.Count==2,"Slow first-token responses are not repeatedly canceled before any translation can appear");
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
