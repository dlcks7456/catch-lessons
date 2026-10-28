using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using System.Diagnostics;

namespace CatchLessons.Desktop;

internal sealed class DesktopHistory:Window
{
    public DesktopHistory(Settings settings,Func<string,bool>? isBusy=null)
    {
        Title="녹음 기록";Width=900;Height=650;
        var root=new DockPanel{Margin=new Thickness(20)};Content=root;
        var folders=Directory.Exists(settings.OutputFolder)?new DirectoryInfo(settings.OutputFolder).GetDirectories().Where(d=>File.Exists(Path.Combine(d.FullName,"session.db"))).OrderByDescending(d=>d.Name).ToArray():[];
        var selection=Ui.Choice(folders,folders.FirstOrDefault());DockPanel.SetDock(selection,Dock.Top);root.Children.Add(selection);
        var search=Ui.Input("");search.PlaceholderText="원문·번역 검색";DockPanel.SetDock(search,Dock.Top);root.Children.Add(search);
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap};DockPanel.SetDock(status,Dock.Top);root.Children.Add(status);
        var actions=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(actions,Dock.Top);root.Children.Add(actions);
        var list=new ListBox{ItemTemplate=new FuncDataTemplate<string>((text,_)=>new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,8)})};root.Children.Add(list);
        List<Caption> rows=[];CancellationTokenSource? running=null;
        void Filter()=>list.ItemsSource=rows.Where(c=>(c.Original+" "+c.Korean).Contains(search.Text??"",StringComparison.CurrentCultureIgnoreCase)).Select(c=>$"{SessionStore.Timestamp(c.Start)} [{c.State}]\n{c.Original}\n{c.Korean}").ToList();
        void Load(){try{if(selection.SelectedItem is DirectoryInfo d){using var store=SessionStore.OpenExisting(d.FullName);rows=store.Read();Filter();}}catch(Exception ex){status.Text=ex.Message;}}
        async Task Run(bool summary)
        {
            if(running!=null||selection.SelectedItem is not DirectoryInfo folder)return;
            if(isBusy?.Invoke(folder.FullName)==true){status.Text="녹음 또는 후처리가 끝난 뒤 다시 시도하세요.";return;}
            using var cancel=new CancellationTokenSource();running=cancel;
            try
            {
                using var store=SessionStore.OpenExisting(folder.FullName,false);
                if(summary&&!settings.ShouldSummarize)throw new IOException("요약 카테고리를 먼저 선택하세요.");
                var recovery=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
                recovery.TargetLanguage=store.TargetLanguage;
                var agent=await CodexTranslator.ForSavedSessionAsync(recovery,folder.FullName,cancel.Token);
                if(summary){store.Flush();await SummaryNotes.CreateAsync(folder.FullName,settings.SummaryCategory,agent.WriteNoteAsync,s=>Avalonia.Threading.Dispatcher.UIThread.Post(()=>status.Text=s),cancel.Token,null,settings.SummaryLanguage,settings.SelectedSummary,settings.SummaryFormat);}
                else foreach(var chunk in store.Read().Where(c=>c.State is not("done" or "transcribed")).Chunk(6))
                {
                    status.Text="미완료 번역 재개 중…";var output=await agent.TranslateBatchAsync(chunk,[],cancel.Token);foreach(var c in chunk)store.Save(c with{Korean=output[c.Id],State="done"});
                }
                store.Flush();Load();status.Text="처리 완료";
            }
            catch(Exception ex){status.Text=ex.Message;}finally{running=null;}
        }
        actions.Children.Add(Ui.Button("미완료 번역 재개",async()=>await Run(false)));actions.Children.Add(Ui.Button("다시 요약",async()=>await Run(true)));
        actions.Children.Add(Ui.Button("폴더 열기",()=>{if(selection.SelectedItem is DirectoryInfo d)Process.Start(new ProcessStartInfo(d.FullName){UseShellExecute=true});}));
        selection.SelectionChanged+=(_,_)=>Load();search.TextChanged+=(_,_)=>Filter();Closed+=(_,_)=>running?.Cancel();Load();
    }
}

