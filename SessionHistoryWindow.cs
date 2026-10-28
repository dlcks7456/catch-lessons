using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Diagnostics;

namespace CatchLessons;

public sealed class SessionHistoryWindow : Window
{
    public SessionHistoryWindow(Settings settings,Func<string?>? activeFolder=null)
    {
        Style=(Style)Application.Current.FindResource(typeof(Window)); Title="저장된 녹음 기록";Width=960;Height=680;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var grid=new Grid {Margin=new Thickness(20)}; grid.ColumnDefinitions.Add(new(){Width=new GridLength(260)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});Content=grid;
        var left=new DockPanel {Margin=new Thickness(0,0,18,0)};grid.Children.Add(left);
        var refresh=new Button {Content="목록 새로고침"};DockPanel.SetDock(refresh,Dock.Top);left.Children.Add(refresh);
        var sessions=new ListBox {DisplayMemberPath="Name",Background=System.Windows.Media.Brushes.Transparent,Foreground=(System.Windows.Media.Brush)FindResource("Text")};left.Children.Add(sessions);
        var right=new DockPanel();Grid.SetColumn(right,1);grid.Children.Add(right);
        var search=new TextBox {ToolTip="선택한 기록의 원문·번역 검색"};DockPanel.SetDock(search,Dock.Top);right.Children.Add(search);
        var controls=new StackPanel {Orientation=Orientation.Horizontal};DockPanel.SetDock(controls,Dock.Top);right.Children.Add(controls);
        var retry=new Button {Content="미완료 번역 재개"};controls.Children.Add(retry);
        var summarize=new Button {Content="선택한 방식으로 요약"};controls.Children.Add(summarize);
        var open=new Button {Content="폴더 열기"};controls.Children.Add(open);
        var edit=new Button {Content="선택 구간 수정"};controls.Children.Add(edit);
        var status=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)};DockPanel.SetDock(status,Dock.Top);right.Children.Add(status);
        var list=new ListBox {Background=System.Windows.Media.Brushes.Transparent,Foreground=(System.Windows.Media.Brush)FindResource("Text"),HorizontalContentAlignment=HorizontalAlignment.Stretch};right.Children.Add(list);
        VirtualizingPanel.SetIsVirtualizing(list,true);VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);
        var template=new DataTemplate();var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding());text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);text.SetValue(MarginProperty,new Thickness(5,8,5,12));template.VisualTree=text;list.ItemTemplate=template;
        List<Caption> rows=[],visible=[];string? folder=null;
        void Filter(){visible=rows.Where(c=>string.IsNullOrWhiteSpace(search.Text)||(c.Original+" "+c.Korean).Contains(search.Text,StringComparison.CurrentCultureIgnoreCase)).ToList();list.ItemsSource=visible.Select(c=>$"{SessionStore.Timestamp(c.Start)}  [{c.State}]\n{c.Original}\n{c.Korean}").ToList();}
        void Load()
        {
            if(folder==null)return;
            using var store=SessionStore.OpenExisting(folder);rows=store.Read();Filter();status.Text=$"{rows.Count}개 구간 / 미완료 {rows.Count(c=>c.State is not ("done" or "transcribed"))}개";
        }
        void Refresh()=>sessions.ItemsSource=Directory.Exists(settings.OutputFolder)?new DirectoryInfo(settings.OutputFolder).GetDirectories().Where(d=>File.Exists(Path.Combine(d.FullName,"session.db"))).OrderByDescending(d=>d.Name).ToArray():[];
        refresh.Click+=(_,_)=>{try{Refresh();}catch(Exception ex){status.Text=ex.Message;}};
        sessions.SelectionChanged+=(_,_)=>{folder=(sessions.SelectedItem as DirectoryInfo)?.FullName;try{Load();}catch(Exception ex){status.Text=ex.Message;}};
        search.TextChanged+=(_,_)=>Filter();
        open.Click+=(_,_)=>{try{if(folder!=null)Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}catch(Exception ex){status.Text=ex.Message;}};
        CancellationTokenSource? operation=null;
        edit.Click+=(_,_)=>
        {
            if(folder==null||list.SelectedIndex<0||operation!=null)return;
            if(string.Equals(folder,activeFolder?.Invoke(),StringComparison.OrdinalIgnoreCase)){status.Text="현재 처리 중인 기록은 수정할 수 없습니다.";return;}
            var selected=visible[list.SelectedIndex];var selectedFolder=folder;
            var dialog=new Window{Title="원문·번역 수정",Width=680,Height=420,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};
            var panel=new StackPanel{Margin=new Thickness(20)};dialog.Content=panel;
            panel.Children.Add(new TextBlock{Text="원문"});var original=new TextBox{Text=selected.Original,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=110};panel.Children.Add(original);
            panel.Children.Add(new TextBlock{Text="번역 (비우면 미완료로 표시)"});var translation=new TextBox{Text=selected.Korean,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=110,IsEnabled=selected.State!="transcribed"};panel.Children.Add(translation);
            var message=new TextBlock{TextWrapping=TextWrapping.Wrap};panel.Children.Add(message);var save=new Button{Content="수정 저장"};panel.Children.Add(save);
            save.Click+=(_,_)=>{try{if(string.IsNullOrWhiteSpace(original.Text))throw new IOException("원문을 입력하세요.");using var store=SessionStore.OpenExisting(selectedFolder,false);store.UpdateText(selected with{Original=original.Text.Trim(),Korean=translation.Text.Trim(),State=selected.State=="transcribed"?"transcribed":string.IsNullOrWhiteSpace(translation.Text)?"pending":"done"});dialog.Close();Load();}catch(Exception ex){message.Text=ex.Message;}};
            dialog.ShowDialog();
        };
        async Task Run(bool note)
        {
            if(folder==null||operation!=null)return;var selected=folder;
            if(string.Equals(selected,activeFolder?.Invoke(),StringComparison.OrdinalIgnoreCase)){status.Text="현재 처리 중인 기록입니다. 중지·요약이 끝난 뒤 다시 시도하세요.";return;}
            using var cancel=new CancellationTokenSource();operation=cancel;retry.IsEnabled=summarize.IsEnabled=sessions.IsEnabled=false;
            try
            {
                if(note&&!settings.ShouldSummarize)throw new IOException("요약을 켜고 카테고리를 선택하세요.");
                using var store=SessionStore.OpenExisting(selected,false);
                if(!note&&!store.Read().Any(c=>c.State is not ("done" or "transcribed"))){status.Text="미완료 번역이 없습니다.";return;}
                var sessionSettings=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings))!;sessionSettings.TargetLanguage=store.TargetLanguage;
                var agent=await CodexTranslator.ForSavedSessionAsync(sessionSettings,selected,cancel.Token);
                if(note)
                {
                    store.Flush();await SummaryNotes.CreateAsync(selected,settings.SummaryCategory,agent.WriteNoteAsync,s=>Dispatcher.BeginInvoke(()=>status.Text=s),cancel.Token,null,settings.SummaryLanguage,settings.SelectedSummary,settings.SummaryFormat);
                    if(settings.OpenFolderAfterSummary)Process.Start(new ProcessStartInfo(selected){UseShellExecute=true});
                }
                else
                {
                    var pending=store.Read().Where(c=>c.State is not ("done" or "transcribed")).ToList();int count=0;
                    foreach(var batch in pending.Chunk(6))
                    {
                        status.Text=$"미완료 번역 재개 중 {count}/{pending.Count}";
                        var result=await agent.TranslateBatchAsync(batch,[],cancel.Token);
                        foreach(var c in batch)store.Save(c with{Korean=result[c.Id],State="done"}); count+=batch.Length;
                    }
                    store.Flush();
                }
                Load();status.Text="처리 완료";
            }
            catch(OperationCanceledException){status.Text="작업 취소";}
            catch(Exception ex){status.Text=ex.Message;}
            finally{operation=null;retry.IsEnabled=summarize.IsEnabled=sessions.IsEnabled=true;}
        }
        retry.Click+=async(_,_)=>await Run(false);summarize.Click+=async(_,_)=>await Run(true);
        Closed+=(_,_)=>operation?.Cancel();try{Refresh();}catch(Exception ex){status.Text=ex.Message;}
    }
}
