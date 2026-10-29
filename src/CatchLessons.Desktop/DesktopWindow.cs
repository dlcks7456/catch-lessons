using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace CatchLessons.Desktop;

internal sealed class DesktopWindow:Window
{
    private Settings settings=Settings.Load();
    private CaptionSession? session;
    private CodexTranslator? lastAgent;
    private string? lastFolder;
    private bool busy,connected,closing,follow=true,loading=true;
    private CancellationTokenSource? operation,probe;
    private readonly ObservableCollection<Caption> rows=[];
    private readonly TextBlock status=new(){FontSize=12,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock connection=new(){FontSize=10};
    private readonly TextBlock sessionId=new(){FontSize=10};
    private readonly TextBlock draft=new(){FontSize=16,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock stage=new(){FontSize=16,TextWrapping=TextWrapping.Wrap};
    private readonly ProgressBar progress=new(){Height=4};
    private readonly Border progressPanel=new(){Padding=new Thickness(16),CornerRadius=new CornerRadius(12),IsVisible=false};
    private readonly ListBox captions=new(){Background=Brushes.Transparent,BorderThickness=new Thickness(0)};
    private readonly ComboBox mode,source,target,category;
    private readonly TextBox topic;
    private readonly Border agentWarning=new(){IsVisible=false,Padding=new Thickness(14),CornerRadius=new CornerRadius(10),Background=Brush.Parse("#503C35"),HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(20,135,20,0)};
    private readonly TextBlock agentWarningText=new(){TextWrapping=TextWrapping.Wrap};
    private readonly CheckBox summarize=new(){Content=UiText.T("요약"),Width=45};
    private readonly Button start,pause;
    private readonly Border shell=new(){Padding=new Thickness(18),CornerRadius=new CornerRadius(18)};
    private double shownThrough,previewEnd;
    public bool BackgroundEnabled {get;set;}
    private bool exitRequested;
    private Exception? pendingFailure;
    public void ExitFromTray(){exitRequested=true;BackgroundEnabled=false;Close();}
    public DesktopWindow()
    {
        UiText.Language=settings.UiLanguage;
        Title="CatchLessons";Width=920;Height=500;MinWidth=600;MinHeight=340;Topmost=true;WindowDecorations=Avalonia.Controls.WindowDecorations.None;
        Icon=new WindowIcon(new Bitmap(Path.Combine(AppContext.BaseDirectory,"Assets","app-icon.png")));
        var root=new Grid();Content=root;root.Children.Add(shell);
        var grid=new Grid{RowDefinitions=new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto")};shell.Child=grid;
        var header=new DockPanel();grid.Children.Add(header);
        var actions=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(actions,Dock.Right);header.Children.Add(actions);
        start=Ui.Icon("play",UiText.T("시작 / 중지"),async()=>await StartStop());actions.Children.Add(start);
        pause=Ui.Icon("pause",UiText.T("일시정지"),TogglePause);pause.IsEnabled=false;actions.Children.Add(pause);
        actions.Children.Add(Ui.Icon("gear",UiText.T("설정"),async()=>await EditSettings()));


        actions.Children.Add(Ui.Icon("minus",UiText.T("최소화"),()=>WindowState=WindowState.Minimized));
        actions.Children.Add(Ui.Icon("x",UiText.T("닫기"),Close));
        mode=Ui.Choice(new[]{UiText.T("번역 모드"),UiText.T("녹취 모드")},settings.TranscriptionOnly?UiText.T("녹취 모드"):UiText.T("번역 모드"));mode.Width=110;DockPanel.SetDock(mode,Dock.Right);header.Children.Add(mode);
        var brand=new StackPanel{VerticalAlignment=VerticalAlignment.Center};brand.Children.Add(new TextBlock{Text="CatchLessons",FontSize=16,FontWeight=FontWeight.SemiBold});brand.Children.Add(connection);header.Children.Add(brand);
        brand.PointerPressed+=(_,e)=>{if(e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)BeginMoveDrag(e);};
        var languages=new Grid{ColumnDefinitions=new ColumnDefinitions("*,24,*,12,*")};Grid.SetRow(languages,1);grid.Children.Add(languages);
        source=Ui.Choice(Languages.Sources,Languages.Source(settings.SourceLanguage));target=Ui.Choice(Languages.All,Languages.Target(settings.TargetLanguage));Grid.SetColumn(target,2);languages.Children.Add(source);languages.Children.Add(target);
        var arrow=new TextBlock{Text="→",HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(arrow,1);languages.Children.Add(arrow);
        topic=Ui.Input(settings.Topic);topic.PlaceholderText=UiText.T("주제 힌트");topic.Margin=new Thickness(0,5,0,12);Grid.SetColumn(topic,4);languages.Children.Add(topic);
        topic.LostFocus+=(_,_)=>{if(loading||busy||session!=null)return;settings.Topic=topic.Text?.Trim()??"";try{settings.Save();}catch(Exception ex){status.Text=UiText.Status(ex.Message);}};
        var stateRow=new DockPanel();Grid.SetRow(stateRow,2);grid.Children.Add(stateRow);DockPanel.SetDock(sessionId,Dock.Right);stateRow.Children.Add(sessionId);stateRow.Children.Add(status);
        captions.ItemsSource=rows;
        captions.ItemTemplate=new FuncDataTemplate<Caption>((caption,_)=>
        {
            if(caption==null)return new TextBlock();var panel=new StackPanel{Margin=new Thickness(2,8,6,12)};
            panel.Children.Add(new TextBlock{Text=caption.Original,TextWrapping=TextWrapping.Wrap,FontSize=caption.State=="transcribed"?24:14,Opacity=caption.State=="transcribed"?1:.7});
            if(caption.State!="transcribed")panel.Children.Add(new TextBlock{Text=caption.State=="done"?caption.Korean:UiText.T("번역 중…"),TextWrapping=TextWrapping.Wrap,FontSize=24,Margin=new Thickness(0,6,0,0)});
            return panel;
        });
        Grid.SetRow(captions,3);grid.Children.Add(captions);Grid.SetRow(draft,4);grid.Children.Add(draft);
        captions.PointerWheelChanged+=(_,e)=>{if(e.Delta.Y>0)follow=false;};
        captions.PointerPressed+=(_,e)=>{if(e.Source is Avalonia.Controls.Primitives.Thumb)follow=false;};
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};Grid.SetRow(footer,5);grid.Children.Add(footer);
        footer.Children.Add(Ui.Icon("arrow-down",UiText.T("최신 자막"),()=>{follow=true;Tail();}));
        summarize.Content="";ToolTip.SetTip(summarize,UiText.T("중지 후 요약 실행"));Avalonia.Automation.AutomationProperties.SetName(summarize,UiText.T("중지 후 요약 실행"));summarize.IsChecked=settings.SummaryEnabled;footer.Children.Add(summarize);
        category=Ui.Choice(new[]{SummaryNotes.None}.Concat(settings.SummaryCategories),settings.SelectedSummary);category.Width=190;footer.Children.Add(category);
        footer.Children.Add(Ui.Icon("note-text",UiText.T("요약 실행"),async()=>await RetrySummary()));
        footer.Children.Add(Ui.Icon("folder-open",UiText.T("저장 폴더"),()=>OpenFolder(lastFolder??settings.OutputFolder)));
        var progressContent=new StackPanel{Spacing=12};progressContent.Children.Add(stage);progressContent.Children.Add(progress);progressContent.Children.Add(Ui.Button(UiText.T("취소 / 확인"),()=>{if(busy)operation?.Cancel();else progressPanel.IsVisible=false;}));progressPanel.Child=progressContent;progressPanel.MaxWidth=540;progressPanel.HorizontalAlignment=HorizontalAlignment.Center;progressPanel.VerticalAlignment=VerticalAlignment.Center;root.Children.Add(progressPanel);
        var warningContent=new DockPanel();agentWarning.Child=warningContent;var warningClose=Ui.Icon("x",UiText.T("닫기"),()=>agentWarning.IsVisible=false);DockPanel.SetDock(warningClose,Dock.Right);warningContent.Children.Add(warningClose);var warningSettings=Ui.Icon("gear",UiText.T("연결 설정"),async()=>await EditSettings());DockPanel.SetDock(warningSettings,Dock.Right);warningContent.Children.Add(warningSettings);warningContent.Children.Add(agentWarningText);root.Children.Add(agentWarning);
        void Chrome(bool visible){foreach(var control in new Control[]{header,languages,stateRow,footer})control.IsVisible=visible;}
        root.PointerEntered+=(_,_)=>Chrome(true);
        root.PointerExited+=(_,_)=>{if(!topic.IsKeyboardFocusWithin&&!category.IsDropDownOpen&&!source.IsDropDownOpen&&!target.IsDropDownOpen&&!mode.IsDropDownOpen)Chrome(false);};
        topic.LostFocus+=(_,_)=>{if(!root.IsPointerOver&&!category.IsDropDownOpen&&!source.IsDropDownOpen&&!target.IsDropDownOpen&&!mode.IsDropDownOpen)Chrome(false);};
        foreach(var edge in new[]{WindowEdge.West,WindowEdge.East,WindowEdge.North,WindowEdge.South,WindowEdge.NorthWest,WindowEdge.NorthEast,WindowEdge.SouthWest,WindowEdge.SouthEast})
        {
            bool left=edge is WindowEdge.West or WindowEdge.NorthWest or WindowEdge.SouthWest;bool right=edge is WindowEdge.East or WindowEdge.NorthEast or WindowEdge.SouthEast;
            bool top=edge is WindowEdge.North or WindowEdge.NorthWest or WindowEdge.NorthEast;bool bottom=edge is WindowEdge.South or WindowEdge.SouthWest or WindowEdge.SouthEast;
            var grip=new Border{Background=Brushes.Transparent,Width=left||right?8:double.NaN,Height=top||bottom?8:double.NaN,HorizontalAlignment=left?HorizontalAlignment.Left:right?HorizontalAlignment.Right:HorizontalAlignment.Stretch,VerticalAlignment=top?VerticalAlignment.Top:bottom?VerticalAlignment.Bottom:VerticalAlignment.Stretch};
            grip.PointerPressed+=(_,e)=>BeginResizeDrag(edge,e);root.Children.Add(grip);
        }
        loading=false;ApplyTheme();
        void SaveChoice(){if(loading||busy)return;if(session==null){settings.TranscriptionOnly=mode.SelectedItem as string==UiText.T("녹취 모드");settings.SourceLanguage=(source.SelectedItem as LanguageOption)?.Code??"auto";settings.TargetLanguage=(target.SelectedItem as LanguageOption)?.Code??"ko";}settings.SummaryCategory=(category.SelectedItem as SummaryCategory)?.Id??"none";settings.SummaryEnabled=summarize.IsChecked==true;try{settings.Save();}catch(Exception ex){status.Text=UiText.Status(ex.Message);}}
        mode.SelectionChanged+=(_,_)=>SaveChoice();source.SelectionChanged+=(_,_)=>SaveChoice();target.SelectionChanged+=(_,_)=>SaveChoice();category.SelectionChanged+=(_,_)=>SaveChoice();summarize.IsCheckedChanged+=(_,_)=>SaveChoice();
        Opened+=async(_,_)=>await Check();
        Closing+=async(_,e)=>{if(BackgroundEnabled){e.Cancel=true;Hide();return;}if(closing)return;if(busy){e.Cancel=true;operation?.Cancel();return;}if(session!=null){e.Cancel=true;await StartStop();closing=true;Close();}};
        Closed+=(_,_)=>{probe?.Cancel();operation?.Cancel();};
        KeyDown+=async(_,e)=>
        {
            var flags=(e.KeyModifiers.HasFlag(KeyModifiers.Control)?1:0)|(e.KeyModifiers.HasFlag(KeyModifiers.Alt)?2:0)|(e.KeyModifiers.HasFlag(KeyModifiers.Shift)?4:0)|(e.KeyModifiers.HasFlag(KeyModifiers.Meta)?8:0);
            foreach(var action in ShortcutKeys.Actions)
            {
                if(!settings.Shortcuts.TryGetValue(action.Id,out var value)||string.IsNullOrWhiteSpace(value))continue;
                if(ShortcutKeys.Parse(value,OperatingSystem.IsMacOS())!=(flags,ShortcutKeys.NormalizeKey(e.Key.ToString())))continue;
                e.Handled=true;switch(action.Id){case "startStop":await StartStop();break;case "pauseResume":TogglePause();break;case "settings":await EditSettings();break;case "folder":OpenFolder(lastFolder??settings.OutputFolder);break;case "mode":if(session==null)mode.SelectedIndex=1-mode.SelectedIndex;break;case "summary":summarize.IsChecked=!(summarize.IsChecked==true);break;case "latest":follow=true;Tail();break;case "minimize":WindowState=WindowState.Minimized;break;}break;
            }
        };
    }
    private void ApplyTheme(){Application.Current!.RequestedThemeVariant=settings.Theme switch{"light"=>ThemeVariant.Light,"dark"=>ThemeVariant.Dark,_=>ThemeVariant.Default};shell.Background=Application.Current.ActualThemeVariant==ThemeVariant.Light?Brush.Parse("#F5F7F7"):Brush.Parse("#181D21");progressPanel.Background=Application.Current.ActualThemeVariant==ThemeVariant.Light?Brush.Parse("#E8EEEE"):Brush.Parse("#242B30");}
    private async Task Check()
    {
        probe?.Cancel();using var cancel=new CancellationTokenSource();probe=cancel;connected=false;connection.Text=UiText.T("연결 확인 중…");agentWarning.IsVisible=false;var failed=new List<string>();var snapshot=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        async Task Test(string provider){var candidate=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(snapshot))!;candidate.AgentProvider=provider;var name=provider=="claude"?"Claude":"ChatGPT";try{await CodexTranslator.CheckConnectionAsync(candidate,cancel.Token);if(probe==cancel&&provider==snapshot.AgentProvider){connected=true;connection.Text=name+" "+UiText.T("연결됨");}}catch(Exception ex){if(probe!=cancel||cancel.IsCancellationRequested)return;failed.Add(name);if(provider==snapshot.AgentProvider)connection.Text=UiText.T("Agent 연결 실패")+": "+UiText.Status(ex.Message);}}
        try{await Task.WhenAll(Test("chatgpt"),Test("claude"));if(probe==cancel){var selected=snapshot.AgentProvider=="claude"?"Claude":"ChatGPT";agentWarning.IsVisible=failed.Contains(selected);agentWarningText.Text=UiText.T("연결 확인 필요")+"\n"+selected+" · "+UiText.T("설정에서 해당 Agent의 CLI 경로와 로그인을 확인하세요.");if(connected&&failed.Count>0)connection.Text+=" · "+string.Join(", ",failed)+" "+UiText.T("연결 실패");}}finally{if(probe==cancel)probe=null;}
    }
    private void SetBusy(bool value){busy=value;mode.IsEnabled=source.IsEnabled=target.IsEnabled=topic.IsEnabled=!value&&session==null;category.IsEnabled=summarize.IsEnabled=!value;pause.IsEnabled=session!=null;}
    private void TogglePause(){if(session==null||!pause.IsEnabled)return;try{session.SetPaused(!session.IsPaused);}catch(Exception ex){status.Text=UiText.Status(ex.Message);return;}pause.Content=new PathIcon{Data=Ui.Symbol(session.IsPaused?"play":"pause")};ToolTip.SetTip(pause,UiText.T(session.IsPaused?"녹음 재개":"일시정지"));status.Text=UiText.T(session.IsPaused?"녹음이 일시정지되었습니다":"시스템 오디오를 듣고 있습니다");}
    private void Progress(string text,bool running=true){stage.Text=UiText.Status(text);progress.IsIndeterminate=running;progressPanel.IsVisible=true;}
    private void Tail(){if(follow&&rows.Count>0)captions.ScrollIntoView(rows[^1]);}
    private void Clear(){rows.Clear();draft.Text="";shownThrough=previewEnd=0;follow=true;}
    private async Task StartStop()
    {
        if(busy){operation?.Cancel();return;}
        using var cancel=new CancellationTokenSource();operation=cancel;SetBusy(true);
        try
        {
            if(session==null)
            {
                settings.Topic=topic.Text?.Trim()??"";settings.Save();
                if(!connected){await Check();if(!connected)throw new IOException(UiText.T("Agent 연결을 확인하세요."));}
                Clear();var current=new CaptionSession(settings);session=current;
                current.Status+=s=>Dispatcher.UIThread.Post(()=>{status.Text=UiText.Status(s);if(busy)Progress(UiText.Status(s));});
                current.SessionStarted+=id=>Dispatcher.UIThread.Post(()=>{sessionId.Text=UiText.T("세션 ")+id[..8];ToolTip.SetTip(sessionId,id);});
                current.Partial+=p=>Dispatcher.UIThread.Post(()=>{if(session!=current||p.End<=shownThrough||p.End<previewEnd)return;previewEnd=p.End;draft.Text=p.Text;});
                current.Updated+=c=>Dispatcher.UIThread.Post(()=>{if(session!=current)return;int index=rows.ToList().FindIndex(r=>r.Id==c.Id);if(index<0){rows.Add(c);shownThrough=Math.Max(shownThrough,c.End);if(c.End>=previewEnd)draft.Text="";}else rows[index]=c;Tail();});
                current.LanguagesDetected+=codes=>Dispatcher.UIThread.Post(()=>status.Text=UiText.T("감지 언어: ")+string.Join(", ",codes));
                current.Failed+=ex=>Dispatcher.UIThread.Post(async()=>{if(session!=current)return;if(busy){pendingFailure=ex;operation?.Cancel();return;}await StartStop();Progress(ex.Message,false);});
                Progress(UiText.T("음성 인식 및 새 Agent 세션 준비 중…"));await current.StartAsync(cancel.Token);lastAgent=current.Agent;lastFolder=current.Folder;progressPanel.IsVisible=false;
            }
            else
            {
                var current=session;pause.IsEnabled=false;Progress(UiText.T("녹음 마무리 중…"));await current.StopAsync();lastFolder=current.Folder;lastAgent=current.Agent;session=null;
                if(lastFolder!=null&&lastAgent!=null)
                {
                    if(settings.ShouldSummarize)await Summarize(lastFolder,lastAgent,cancel.Token);
                    if(!string.IsNullOrWhiteSpace(settings.PostStopPrompt))
                    {
                        Progress(UiText.T("후속 워크플로우 실행 중…"));
                        await File.WriteAllTextAsync(Path.Combine(SessionStore.InternalFor(lastFolder),"workflow-result-"+DateTime.Now.ToString("yyyyMMddHHmmss")+".md"),await lastAgent.RunWorkflowAsync(settings.PostStopPrompt,lastFolder,cancel.Token),cancel.Token);
                    }
                }
                Clear();Progress(UiText.T("저장 완료"),false);
            }
        }
        catch(Exception ex){if(session!=null){try{await session.StopAsync();}catch{}session=null;}Progress((pendingFailure??ex).Message,false);}
        finally{pendingFailure=null;operation=null;SetBusy(false);if(exitRequested&&session==null){closing=true;Close();}}
    }
    private async Task Summarize(string folder,CodexTranslator agent,CancellationToken token=default,bool manual=false)
    {
        if(!SummaryNotes.CanRun(settings,manual))return;
        await SummaryNotes.CreateAsync(folder,settings.SummaryCategory,agent.WriteNoteAsync,s=>Dispatcher.UIThread.Post(()=>Progress(s)),token,null,settings.SummaryLanguage,settings.SelectedSummary,settings.SummaryFormat);
        if(settings.OpenFolderAfterSummary)OpenFolder(folder);
    }
    private async Task RetrySummary()
    {
        if(busy||(session?.Folder??lastFolder) is not string folder||lastAgent==null||settings.SelectedSummary.Id=="none")return;
        using var cancel=new CancellationTokenSource();operation=cancel;SetBusy(true);
        try{session?.FlushCaptions();await Summarize(folder,lastAgent,cancel.Token,true);Progress(UiText.T("요약 저장 완료"),false);}
        catch(Exception ex){Progress(ex.Message,false);}
        finally
        {
            operation=null;SetBusy(false);var failure=pendingFailure;pendingFailure=null;
            if(failure!=null&&session!=null){await StartStop();if(!exitRequested)Progress(failure.Message,false);}
            if(exitRequested&&!closing){if(session!=null)await StartStop();else{closing=true;Close();}}
        }
    }
    private async Task EditSettings(){if(busy||session!=null)return;bool reopen=false;do{var dialog=new DesktopSettings(settings,reopen);await dialog.ShowDialog(this);if(dialog.UpdateStarted){ExitFromTray();return;}reopen=dialog.LanguageChanged;settings=Settings.Load();UiText.Language=settings.UiLanguage;(Application.Current as DesktopApp)?.RefreshTrayLanguage();Ui.ApplyLanguage((Control)Content!,captions);loading=true;mode.ItemsSource=new[]{UiText.T("번역 모드"),UiText.T("녹취 모드")};mode.SelectedItem=settings.TranscriptionOnly?UiText.T("녹취 모드"):UiText.T("번역 모드");source.SelectedIndex=-1;target.SelectedIndex=-1;category.SelectedIndex=-1;source.ItemsSource=Languages.Sources.Select(l=>new LanguageOption(l.Code,l.Name)).ToArray();target.ItemsSource=Languages.All.Select(l=>new LanguageOption(l.Code,l.Name)).ToArray();source.SelectedItem=Languages.Source(settings.SourceLanguage);target.SelectedItem=Languages.Target(settings.TargetLanguage);category.ItemsSource=new[]{SummaryNotes.None}.Concat(settings.SummaryCategories).Select(c=>c with{}).ToArray();category.SelectedItem=settings.SelectedSummary;summarize.IsChecked=settings.SummaryEnabled;loading=false;ApplyTheme();}while(reopen);await Check();}
    private void OpenFolder(string folder){try{Directory.CreateDirectory(folder);Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}catch(Exception ex){status.Text=UiText.Status(ex.Message);}}
}













