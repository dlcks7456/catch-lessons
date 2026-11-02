using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using System.Windows;

namespace CatchLessons;

internal static class ApiCaptionFlowTests
{
    public static async Task<string> RunAsync()
    {
        int passed=0;
        void Check(bool value,string name){if(!value)throw new Exception(name);passed++;}

        static async Task<List<Caption>> AssembleAsync(double lookAhead)
        {
            var channel=Channel.CreateUnbounded<Caption>();
            channel.Writer.TryWrite(new(1,"first",0,2.6,"We need to review"));
            var next=Task.Run(async()=>{await Task.Delay(2250);channel.Writer.TryWrite(new(2,"next",2.7,4.8,"the draft today."));channel.Writer.TryComplete();});
            var captions=new List<Caption>();
            await foreach(var caption in CaptionAssembler.ReadAsync(channel.Reader,CancellationToken.None,lookAhead))captions.Add(caption);
            await next;
            return captions;
        }
        var agent=await AssembleAsync(2);
        Check(agent.Count==2,"Agent's default caption look-ahead remains unchanged");
        var api=await AssembleAsync(3.35);
        Check(api.Count==1&&api[0].Original=="We need to review the draft today."&&api[0].Id==1&&api[0].End==4.8,"API look-ahead assembles neighboring incomplete clips into one final translation unit");

        var window=new MainWindow();
        window.ApplyPreview(new("We need to review",2.6));
        window.ApplyTranslationPreview(new(1,2.6,"문장 초안"));
        Check(window.History.Items.Count==0&&window.PartialText.Text=="We need to review"&&window.TranslationPreviewText.Text=="문장 초안","Interim source/translation remain in the draft area, outside finalized caption history");
        var final=new Caption(1,"first+next",0,4.8,"We need to review the draft today.","확정 번역","done");
        window.ApplyCaption(final);
        Check(window.History.Items.Count==1&&((CaptionRow)window.History.Items[0]).Value.Korean=="확정 번역"&&window.TranslationPreviewText.Text=="","Final caption replaces the preview and appears once in history");
        window.ApplyTranslationPreview(new(1,2.6,"낡은 초안"));
        Check(window.TranslationPreviewText.Text=="","A late update from the superseded revision cannot cover a final caption");
        for(int i=0;i<20;i++)window.ApplyCaption(final with{Korean="stream delta "+i,State="translating"});
        window.ApplyTranslationPreview(new(2,9,"이어서 나온 초안"));
        Check(window.History.Items.Count==1&&window.TranslationPreviewText.Text=="이어서 나온 초안","Streaming Caption updates do not advance the API preview revision");
        window.ResetCaptionDisplay();window.ApplyTranslationPreview(new(1,11,"다음 세션 초안"));
        Check(window.History.Items.Count==0&&window.TranslationPreviewText.Text=="다음 세션 초안","A new session accepts its own first preview revision");
        window.Close();

        var settings=new Settings{TranslationProvider="openai",SpeechEngine="openai"};
        using(var translator=new OpenAiTranslator(settings,"test-key",null))
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var finalized=(Queue<Caption>)typeof(OpenAiTranslator).GetField("context",flags)!.GetValue(translator)!;
            finalized.Enqueue(new(1,"final",0,1,"Confirmed original","확정 번역"));
            using var preview=translator.CreatePreviewTranslator();
            var previewHistory=(Queue<Caption>)typeof(OpenAiTranslator).GetField("context",flags)!.GetValue(preview)!;
            Check(previewHistory.SequenceEqual(finalized),"Tentative translation starts from confirmed context only");
            previewHistory.Enqueue(new(2,"draft",1,2,"Tentative text","임시 번역"));
            Check(finalized.Count==1&&finalized.Peek().ItemId=="final","Preview results never pollute final translation context");
        }
        return JsonSerializer.Serialize(new{passed,mockedApiCalls=0,liveAudio=false});
    }
}
