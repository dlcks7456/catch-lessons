using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace CatchLessons;

public sealed class CaptionRow(Caption caption) : INotifyPropertyChanged
{
    public Caption Value { get; private set; } = caption;
    public string Original => Value.Original;
    public FlowDirection SourceDirection => (FlowDirection)Languages.Direction(Value.Original);
    public FlowDirection TranslationDirection => (FlowDirection)Languages.Direction(DisplayKorean);
    public bool IsTranscriptionOnly => Value.State == "transcribed";
    public string DisplayKorean => IsTranscriptionOnly ? "" : Value.State == "done" ? Value.Korean :
        Value.State == "pending" ? UiText.T("번역 중…") : UiText.T("번역 미완료 · ")+Value.State;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(Caption c) { Value = c; PropertyChanged?.Invoke(this, new(null)); }
}

public partial class MainWindow : Window
{
    private Settings settings = new();
    private CaptionSession? session;
    private readonly ObservableCollection<CaptionRow> rows = new();
    private bool busy, closeAfterStop;
    private string? lastFolder;

    private readonly CaptionScroll historyFollow;

    private CancellationTokenSource? summaryCancellation;
    private CancellationTokenSource? startupCancellation;
    private bool closeWhenReady;
    private bool loadingCategory = true;
    private bool loadingMode = true;
    private string? lastSourceWarning;
    private CodexTranslator? lastAgent;
    private CancellationTokenSource? connectionCheck;
    private bool agentConnected;
    private Exception? pendingSessionFailure;
    internal Func<Settings,CancellationToken,Task<string>> AgentProbe=(s,t)=>CodexTranslator.CheckConnectionAsync(s,t);

    private bool summarySaved, postStopAttempted;
    private double latestPreviewEnd, displayedThrough;
    private bool loadingLanguages = true;
    private bool loadingTopic=true;
    public MainWindow()
    {
        InitializeComponent();
        History.ItemsSource = rows;
        historyFollow = new CaptionScroll(HistoryScroll);
        historyFollow.Changed += following => LatestButton.Visibility = following ? Visibility.Collapsed : Visibility.Visible;
        try { settings = Settings.Load(); }
        catch (Exception ex) { StatusText.Text = UiText.T("설정 읽기 실패: ") + ex.Message; }
        UiText.Language=settings.UiLanguage;WpfLocalizer.Apply(this);TopicInput.Text=settings.Topic;TopicPlaceholder.Visibility=string.IsNullOrEmpty(TopicInput.Text)?Visibility.Visible:Visibility.Collapsed;loadingTopic=false;
        FontSizeSlider.Value=settings.CaptionFontSize;OpacitySlider.Value=Opacity=settings.WindowOpacity;
        FontSizeSlider.ValueChanged+=SaveAppearance;
        OpacitySlider.ValueChanged+=SaveAppearance;
        ThemeManager.Apply(settings.Theme);
        SourcePicker.ItemsSource = Languages.Sources.ToArray();
        TargetPicker.ItemsSource = Languages.All;
        RefreshLanguages();
        RefreshSummaryControls();
        ModePicker.ItemsSource = new[] { new { Id = false, Name = UiText.T("번역 모드") }, new { Id = true, Name = UiText.T("녹취 모드") } };
        ModePicker.SelectedValue = settings.TranscriptionOnly;
        loadingMode = false;
        Loaded += async (_,_) => { if(IsVisible) await CheckAgentConnectionAsync(); };
        SourceInitialized += (_,_)=>RegisterShortcuts();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            hotkeys?.Dispose();
            connectionCheck?.Cancel();

        };
    }
    private void Dispatch(Action action) => Dispatcher.BeginInvoke(action);
    internal void ShowAgentWarnings(IEnumerable<string> failed)
    {
        var names=failed.ToArray();AgentWarning.Visibility=names.Length==0?Visibility.Collapsed:Visibility.Visible;
        AgentWarningTitle.Text=UiText.T("연결 확인 필요");
        AgentWarningText.Text=string.Join(", ",names)+" · "+UiText.T("설정에서 해당 Agent의 CLI 경로와 로그인을 확인하세요.");
    }
    private void DismissAgentWarning(object sender,RoutedEventArgs e)=>AgentWarning.Visibility=Visibility.Collapsed;
    internal async Task CheckAgentConnectionAsync()
    {
        connectionCheck?.Cancel();using var cancel=new CancellationTokenSource();connectionCheck=cancel;
        var snapshot=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        agentConnected=false;AgentConnectionText.Text=UiText.T("Agent 연결 확인 중…");AgentConnectionText.ToolTip=null;ShowAgentWarnings([]);if(session==null&&!busy)StartButton.IsEnabled=false;
        var failures=new List<string>();
        async Task Check(string provider)
        {
            var candidate=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(snapshot))!;candidate.AgentProvider=provider;
            var name=provider=="claude"?"Claude":"ChatGPT";
            try{await AgentProbe(candidate,cancel.Token);if(connectionCheck==cancel&&provider==snapshot.AgentProvider){agentConnected=true;AgentConnectionText.Text=name+" "+UiText.T("연결됨");}}
            catch(Exception ex){if(connectionCheck!=cancel||cancel.IsCancellationRequested)return;failures.Add(name);if(provider==snapshot.AgentProvider){AgentConnectionText.Text=UiText.T("Agent 연결 실패");AgentConnectionText.ToolTip=UiText.Status(ex.Message);}}
            finally{if(connectionCheck==cancel&&provider==snapshot.AgentProvider&&session==null&&!busy)StartButton.IsEnabled=agentConnected;}
        }
        try{await Task.WhenAll(Check("chatgpt"),Check("claude"));if(connectionCheck==cancel){var selected=snapshot.AgentProvider=="claude"?"Claude":"ChatGPT";ShowAgentWarnings(failures.Where(name=>name==selected));if(agentConnected&&failures.Count>0)AgentConnectionText.Text+=" · "+string.Join(", ",failures)+" "+UiText.T("연결 실패");}}
        finally{if(connectionCheck==cancel)connectionCheck=null;}
    }
    private void TopicChanged(object sender,System.Windows.Controls.TextChangedEventArgs e){if(TopicPlaceholder!=null)TopicPlaceholder.Visibility=string.IsNullOrEmpty(TopicInput.Text)?Visibility.Visible:Visibility.Collapsed;}
    private void TopicLostFocus(object sender,KeyboardFocusChangedEventArgs e)=>SaveTopic();
    private void SaveTopic(){if(loadingTopic||!IsLoaded)return;settings.Topic=TopicInput.Text.Trim();try{settings.Save();}catch(Exception ex){StatusText.Text=UiText.Status(ex.Message);}}
    private void CopySessionId(object sender,MouseButtonEventArgs e)
    {
        if(SessionIdText.Tag is string id)try{Clipboard.SetText(id);}catch(Exception ex){StatusText.Text=UiText.Status(ex.Message);}
    }
    private WindowsHotkeys? hotkeys;
    private void RegisterShortcuts(){hotkeys??=new WindowsHotkeys(ExecuteShortcut);hotkeys.Bind(settings.Shortcuts);}
    private void ExecuteShortcut(string action)
    {
            if(action is "settings"){Show();WindowState=WindowState.Normal;Activate();}
            switch(action)
            {
                case "startStop": if(StartButton.IsEnabled)StartStop(this,new());break;
                case "pauseResume": if(PauseButton.IsEnabled)PauseRecording(this,new());break;
                case "settings": if(SettingsButton.IsEnabled)OpenSettings(this,new());break;

                case "mode": if(ModePicker.IsEnabled)ModePicker.SelectedValue=!settings.TranscriptionOnly;break;
                case "summary": if(SummaryEnabledToggle.IsEnabled)SummaryEnabledToggle.IsChecked=!(SummaryEnabledToggle.IsChecked==true);break;
                case "latest": historyFollow.Resume();break;
                case "folder": OpenFolder(this,new());break;

                case "minimize": WindowState=WindowState.Minimized;break;
            }
    }
    private void RefreshSummaryControls()
    {
        loadingCategory = true;
        SummaryPicker.ItemsSource = new[] { SummaryNotes.None }.Concat(settings.SummaryCategories).Select(c=>c with{}).ToArray();
        SummaryPicker.SelectedValue = settings.SelectedSummary.Id;
        SummaryEnabledToggle.IsChecked = settings.SummaryEnabled;
        SummaryPicker.IsEnabled = SummaryEnabledToggle.IsEnabled = !busy;
        SummaryRetryButton.IsEnabled = !busy && (session?.Folder??lastFolder)!=null && settings.SelectedSummary.Id!="none";
        PauseButton.IsEnabled=session!=null&&startupCancellation==null;
        TargetPicker.IsEnabled=!busy&&session==null&&!settings.TranscriptionOnly;
        TargetPicker.ToolTip=settings.TranscriptionOnly?UiText.T("녹취 모드에서는 번역하지 않습니다. 요약 언어는 설정에서 별도로 선택하세요."):UiText.T("번역 언어");
        loadingCategory = false;
    }
    private void ModeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (loadingMode || busy || session != null || ModePicker.SelectedValue is not bool only) return;
        settings.TranscriptionOnly = only;
        RefreshSummaryControls();
        try { settings.Save(); } catch (Exception ex) { StatusText.Text = UiText.Status(ex.Message); }
    }
    internal void ApplyPreview(TranscriptPreview preview)
    {
        if (preview.End <= displayedThrough || preview.End < latestPreviewEnd) return;
        latestPreviewEnd = preview.End;
        EmptyText.Visibility = Visibility.Collapsed;
        PartialText.Text = preview.Text;
        PartialText.FlowDirection = (FlowDirection)Languages.Direction(preview.Text);
        historyFollow.ContentChanged();
    }
    internal void ApplyCaption(Caption caption)
    {
        EmptyText.Visibility = Visibility.Collapsed;
        var row = rows.FirstOrDefault(r => r.Value.Id == caption.Id);
        if (row != null) row.Update(caption);
        else
        {
            var at = rows.TakeWhile(r => r.Value.Start <= caption.Start).Count();
            rows.Insert(at, new(caption));
            displayedThrough = Math.Max(displayedThrough, caption.End);
            if (caption.End >= latestPreviewEnd) PartialText.Text = "";
        }
        // Keep captions while the user is reading earlier lines.
        if (historyFollow.Following)
            while (rows.Count > 500) rows.RemoveAt(0);
        historyFollow.ContentChanged();

    }
    private void SetMainAction(string icon, string label)
    {
        StartButton.Tag = FindResource("Icon-" + icon);
        StartButton.Content = label;
    }
    private void PauseRecording(object sender,RoutedEventArgs e)
    {
        if(session==null||startupCancellation!=null)return;
        try{session.SetPaused(!session.IsPaused);}catch(Exception ex){StatusText.Text=UiText.Status(ex.Message);return;}PauseButton.Tag=FindResource(session.IsPaused?"Icon-play":"Icon-pause");
        PauseButton.Content=UiText.T(session.IsPaused?"녹음 재개":"일시정지");
        StatusText.Text=UiText.T(session.IsPaused?"녹음이 일시정지되었습니다":"시스템 오디오를 듣고 있습니다");
        if(session.IsPaused)Meter.Value=0;
    }
    private void RefreshLanguages()
    {
        loadingLanguages = true;
        settings.SourceLanguage = Languages.Source(settings.SourceLanguage).Code;
        settings.TargetLanguage = Languages.Target(settings.TargetLanguage).Code;
        SourcePicker.SelectedValue = Languages.Source(settings.SourceLanguage).Code;
        TargetPicker.SelectedValue = Languages.Target(settings.TargetLanguage).Code;
        DetectionText.Text = settings.SourceLanguage == "auto" ? UiText.T("자동 감지 대기") : UiText.T("원문 언어 고정");
        loadingLanguages = false;
    }
    private void LanguageSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (loadingLanguages || busy || session != null) return;
        settings.SourceLanguage = SourcePicker.SelectedValue as string ?? "auto";
        settings.TargetLanguage = TargetPicker.SelectedValue as string ?? "ko";
        RefreshLanguages();
        try { settings.Save(); } catch (Exception ex) { StatusText.Text = UiText.Status(ex.Message); }
    }
    private async void StartStop(object sender, RoutedEventArgs e)
    {
        if (summaryCancellation != null) { summaryCancellation.Cancel(); return; }
        if (startupCancellation != null) { startupCancellation.Cancel(); return; }
        if (busy) return;
        if (session != null) { await StopAsync(); return; }
        SaveTopic();
        if(!agentConnected){await CheckAgentConnectionAsync();if(!agentConnected)return;}
        busy = true;
        StartButton.IsEnabled = false;
        SettingsButton.IsEnabled = false;
        SummaryRetryButton.IsEnabled = false;
        SourcePicker.IsEnabled = TargetPicker.IsEnabled = TopicInput.IsEnabled = false;
        ModePicker.IsEnabled = false;
        SummaryPicker.IsEnabled = SummaryEnabledToggle.IsEnabled = false;
        ResetCaptionDisplay();
        postStopAttempted=false;
        var current = new CaptionSession(settings);
        session = current;
        current.SessionStarted += id => Dispatch(()=>{if(session!=current)return;SessionIdText.Tag=id;SessionIdText.Text=UiText.T("세션 ")+id[..Math.Min(8,id.Length)];SessionIdText.ToolTip=id+UiText.T(" (클릭해서 복사)");});
        using var startup = new CancellationTokenSource();
        startupCancellation = startup;
        SetMainAction("x", UiText.T("모델 준비 취소"));
        StartButton.IsEnabled = true;
        ShowProcessing(UiText.T("음성 인식 준비"), UiText.T("선택한 음성 엔진과 새 Agent 세션을 준비합니다."), true, true);
        ProcessingAction.Content = UiText.T("준비 취소");
        current.LanguagesDetected += codes => Dispatch(() =>
        {
            if (session != current || settings.SourceLanguage != "auto") return;
            DetectionText.Text = codes.Length == 0 ? UiText.T("언어 판단 불가") : UiText.T("감지: ") + string.Join(", ", codes.Select(c => UiText.LanguageLabel(c,Languages.All.FirstOrDefault(l => l.Code == c)?.Name ?? c)));
        });
        current.Status += text => Dispatch(() =>
        {
            if (session != current) return;
            StatusText.Text = UiText.Status(text);
            if (busy && summaryCancellation==null && ProcessingProgress.IsIndeterminate) ProcessingDetail.Text = UiText.Status(text);
        });
        current.Level += level => Dispatch(() => { if (session == current) Meter.Value = Math.Min(1, level * 4); });
        current.Partial += preview => Dispatch(() => { if (session == current) ApplyPreview(preview); });
        current.Updated += caption => Dispatch(() => { if (session == current) ApplyCaption(caption); });
        current.Failed += ex => Dispatch(async () =>
        {
            if (session != current) return;
            if(busy){pendingSessionFailure=ex;summaryCancellation?.Cancel();return;}
            await StopAsync(false);
            StatusText.Text = UiText.Status(ex.Message);
        });
        try
        {
            await current.StartAsync(startup.Token);
            PauseButton.Tag=FindResource("Icon-pause");PauseButton.Content=UiText.T("일시정지");
            ProcessingPanel.Visibility = Visibility.Collapsed;
            ProcessingProgress.IsIndeterminate = false;
            lastFolder = current.Folder;
            lastAgent = current.Agent;
            SetMainAction("stop", UiText.T("녹음 중지 및 요약"));
        }
        catch (Exception ex)
        {
            try { await current.StopAsync(); } catch { }
            session = null;
            StatusText.Text = startup.IsCancellationRequested ? UiText.T("모델 준비를 취소했습니다.") : UiText.Status(ex.Message);
            ShowProcessing(UiText.T("음성 인식 준비가 완료되지 않았습니다"), StatusText.Text, false, false);
            SetMainAction("play", UiText.T("녹음 시작"));
        }
        finally
        {
            busy = false;
            startupCancellation = null;
            StartButton.IsEnabled = true;
            SettingsButton.IsEnabled = session == null;
            SummaryRetryButton.IsEnabled = session == null && lastFolder != null;
            SourcePicker.IsEnabled = TargetPicker.IsEnabled = TopicInput.IsEnabled = session == null;
            ModePicker.IsEnabled = session == null;
            RefreshSummaryControls();
            if(pendingSessionFailure is { } failure&&session!=null){pendingSessionFailure=null;await StopAsync(false);StatusText.Text=UiText.Status(failure.Message);ShowProcessing(UiText.T("녹음 중단"),StatusText.Text,false,false);}
            if (closeWhenReady&&!closeAfterStop) { if(session!=null)await StopAsync();else{closeAfterStop=true;Close();} }
        }
    }
    private async Task StopAsync(bool summarize = true)
    {
        if (session == null || busy) return;
        busy = true;
        StartButton.IsEnabled = false;
        var old = session;
        PauseButton.IsEnabled=false;
        SummaryPicker.IsEnabled = SummaryEnabledToggle.IsEnabled = false;
        ShowProcessing(UiText.T("녹음 마무리 · 파일 저장 중"), UiText.T("남은 음성 인식과 번역을 마무리하고 있습니다."), true, false);
        try
        {
            await old.StopAsync();
            session = null;
            lastFolder = old.Folder;
            lastAgent = old.Agent;
            lastSourceWarning = old.StopWarning;
            StatusText.Text = (old.StopWarning is null ? null : UiText.Status(old.StopWarning)) ?? UiText.T("중지됨 · 원문과 번역 파일을 저장했습니다");
            if (summarize && settings.ShouldSummarize && lastFolder != null) await GenerateSummaryAsync(lastFolder, old.StopWarning);
            else
            {
                if (summarize) ResetCaptionDisplay();
                ShowProcessing(UiText.T("저장 완료"), (old.StopWarning is null ? null : UiText.Status(old.StopWarning)) ?? UiText.T("원문과 선택한 기록을 저장했습니다. 요약은 실행하지 않았습니다."), false, false);
            }
            if(summarize&&lastFolder!=null&&(!settings.ShouldSummarize||summarySaved))await RunPostStopWorkflowAsync(lastFolder);
        }
        catch (Exception ex)
        {
            StatusText.Text = UiText.T("종료 오류: ") + UiText.Status(ex.Message);
            ShowProcessing(UiText.T("마무리 중 오류가 발생했습니다"), StatusText.Text, false, false);
        }
        finally
        {
            session = null;
            busy = false;
            SetMainAction("play", UiText.T("녹음 시작"));
            StartButton.IsEnabled = SettingsButton.IsEnabled = true;
            Meter.Value = 0;
            SummaryPicker.IsEnabled = true;
            SourcePicker.IsEnabled = TargetPicker.IsEnabled = TopicInput.IsEnabled = true;
            SummaryRetryButton.IsEnabled = lastFolder != null;
            ModePicker.IsEnabled = true;
            RefreshSummaryControls();
            pendingSessionFailure=null;
            if (closeWhenReady) { closeAfterStop = true; Close(); }
        }
    }
    public bool BackgroundEnabled {get;set;}
    public void ExitFromTray(){BackgroundEnabled=false;Close();}
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if(BackgroundEnabled){e.Cancel=true;Hide();return;}
        if (closeAfterStop) return;
        if (startupCancellation != null)
        {
            e.Cancel = true; closeWhenReady = true; startupCancellation.Cancel(); return;
        }
        if (summaryCancellation != null)
        {
            e.Cancel = true;
            closeWhenReady = true;
            summaryCancellation.Cancel();
            return;
        }
        if (busy) { e.Cancel = true; return; }
        if (session == null) return;
        e.Cancel = true;
        closeWhenReady = true;
        await StopAsync();
    }
    private async void OpenSettings(object sender, RoutedEventArgs e)
    {
        if(!SettingsButton.IsEnabled)return;
        SaveTopic();
        hotkeys?.Clear();
        bool reopen=false;
        do
        {
            var dialog = new SettingsWindow(settings,reopen) { Owner = this };
            dialog.ShowDialog();reopen=dialog.LanguageChanged;
            settings = Settings.Load();
            UiText.Language=settings.UiLanguage;WpfLocalizer.Apply(this);(Application.Current as App)?.RefreshTrayLanguage();
            ThemeManager.Apply(settings.Theme);
            ModePicker.ItemsSource = new[] { new { Id=false,Name=UiText.T("번역 모드") },new { Id=true,Name=UiText.T("녹취 모드") } };
            loadingMode=true;ModePicker.SelectedValue=settings.TranscriptionOnly;loadingMode=false;
            loadingLanguages=true;SourcePicker.SelectedIndex=-1;TargetPicker.SelectedIndex=-1;SourcePicker.ItemsSource=Languages.Sources.Select(l=>new LanguageOption(l.Code,l.Name)).ToArray();TargetPicker.ItemsSource=Languages.All.Select(l=>new LanguageOption(l.Code,l.Name)).ToArray();
            RefreshLanguages();
            loadingCategory=true;SummaryPicker.SelectedIndex=-1;
            RefreshSummaryControls();
            foreach(var row in rows)row.Update(row.Value);
        }while(reopen);
        RegisterShortcuts();await CheckAgentConnectionAsync();
    }
    private void SummaryCategoryChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (loadingCategory || SummaryPicker.SelectedValue is not string id) return;
        settings.SummaryCategory = id;
        if (id == "none") settings.SummaryEnabled = false;
        RefreshSummaryControls();
        try { settings.Save(); }
        catch (Exception ex) { StatusText.Text = UiText.T("요약 카테고리 저장 실패: ") + UiText.Status(ex.Message); }
    }
    private void SummaryEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (loadingCategory || busy) return;
        settings.SummaryEnabled = SummaryEnabledToggle.IsChecked == true;
        if (settings.SummaryEnabled && settings.SelectedSummary.Id == "none")
        {
            if (settings.SummaryCategories.Count > 0) settings.SummaryCategory = settings.SummaryCategories[0].Id;
            else settings.SummaryEnabled = false;
        }
        RefreshSummaryControls();
        try { settings.Save(); } catch (Exception ex) { StatusText.Text = UiText.Status(ex.Message); }
    }
    private async Task GenerateSummaryAsync(string folder, string? warning = null,bool manual=false)
    {
        summarySaved=false;
        if (!SummaryNotes.CanRun(settings,manual)) return;
        var category = settings.SelectedSummary with { };
        SummaryPicker.IsEnabled = SettingsButton.IsEnabled = SummaryRetryButton.IsEnabled = false;
        SummaryEnabledToggle.IsEnabled = ModePicker.IsEnabled = false;
        SourcePicker.IsEnabled = TargetPicker.IsEnabled = TopicInput.IsEnabled = false;
        SetMainAction("x", UiText.T("요약 취소"));
        StartButton.IsEnabled = true;
        using var cancel = new CancellationTokenSource();
        summaryCancellation = cancel;
        ShowProcessing(UiText.T("전체 원문 요약 중"), category.Name + UiText.T(" · Codex 응답을 기다리고 있습니다."), true, true);
        try
        {
            var codex = lastAgent ?? CodexTranslator.Create(settings, Path.Combine(Settings.Root, "agent-work"));
            if(codex.SessionId==null){Directory.CreateDirectory(SessionStore.InternalFor(folder));await codex.StartSessionAsync(SessionStore.InternalFor(folder),cancel.Token);}
            var path = await SummaryNotes.CreateAsync(folder, settings.SummaryCategory, codex.WriteNoteAsync,
                text => Dispatch(() =>
                {
                    if (summaryCancellation != cancel || cancel.IsCancellationRequested) return;
                    StatusText.Text = ProcessingDetail.Text = UiText.Status(text);
                }), cancel.Token, warning, settings.SummaryLanguage, category,settings.SummaryFormat);
            summaryCancellation = null;
            summarySaved=true;
            if(!manual)ResetCaptionDisplay();
            StatusText.Text = UiText.T(manual&&session!=null?"요약 저장 완료 · 녹음 계속 중 · ":"요약 저장 완료 · 새 세션 시작 대기 · ") + Path.GetFileName(path);
            try { if (settings.OpenFolderAfterSummary) Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
            catch (Exception ex) { StatusText.Text += UiText.T(" · 폴더 열기 실패: ") + ex.Message; }
            ShowProcessing(UiText.T("요약 저장 완료"), StatusText.Text, false, false);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = cancel.IsCancellationRequested ? UiText.T("요약 취소 · 원문과 번역은 저장되어 있습니다") : UiText.T("Codex 요약 시간 초과 · ‘요약 다시’로 재시도하세요");
            ShowProcessing(cancel.IsCancellationRequested ? UiText.T("요약이 취소되었습니다") : UiText.T("요약 응답 시간이 초과되었습니다"), StatusText.Text, false, false);
        }
        catch (Exception ex)
        {
            StatusText.Text = UiText.T("요약 미완료 · ") + UiText.Status(ex.Message) + UiText.T(" · 원문과 번역은 보존됩니다");
            ShowProcessing(UiText.T("요약을 완료하지 못했습니다"), StatusText.Text, false, false);
        }
        finally { summaryCancellation = null; }
    }
    private async Task RunPostStopWorkflowAsync(string folder)
    {
        if(postStopAttempted||string.IsNullOrWhiteSpace(settings.PostStopPrompt)||lastAgent==null)return;
        postStopAttempted=true;using var cancel=new CancellationTokenSource();summaryCancellation=cancel;
        ShowProcessing(UiText.T("후속 워크플로우 실행 중"),UiText.T("설정한 요청을 같은 Agent 세션에서 실행합니다. Agent 권한 정책이 적용됩니다."),true,true);
        SetMainAction("x",UiText.T("후속 작업 취소"));StartButton.IsEnabled=true;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(SessionStore.InternalFor(folder),"workflow-state.json"),"{\"status\":\"started\"}",cancel.Token);
            var report=await lastAgent.RunWorkflowAsync(settings.PostStopPrompt,folder,cancel.Token);
            await File.WriteAllTextAsync(Path.Combine(SessionStore.InternalFor(folder),"workflow-result-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".md"),report,cancel.Token);
            await File.WriteAllTextAsync(Path.Combine(SessionStore.InternalFor(folder),"workflow-state.json"),"{\"status\":\"completed\"}",cancel.Token);
            ShowProcessing(UiText.T("후속 워크플로우 완료"),UiText.T("실행 보고서를 저장했습니다."),false,false);
        }
        catch(Exception ex)
        {
            try{File.WriteAllText(Path.Combine(SessionStore.InternalFor(folder),"workflow-state.json"),System.Text.Json.JsonSerializer.Serialize(new{status=cancel.IsCancellationRequested?"canceled":"failed",error=ex.Message}));}catch(IOException){}
            ShowProcessing(UiText.T("후속 워크플로우 미완료"),UiText.Status(ex.Message),false,false);
        }
        finally{summaryCancellation=null;}
    }
    internal void ResetCaptionDisplay()
    {
        ProcessingPanel.Visibility = Visibility.Collapsed;
        ProcessingProgress.IsIndeterminate = false;
        rows.Clear();
        latestPreviewEnd = displayedThrough = 0;
        PartialText.Text = "";
        SessionIdText.Text=""; SessionIdText.Tag=null;
        Meter.Value = 0;
        RefreshLanguages();
        EmptyText.ClearValue(VisibilityProperty);
        historyFollow.Resume();

        LatestButton.Visibility = Visibility.Collapsed;
    }
    internal void ShowProcessing(string title, string detail, bool running, bool canCancel)
    {
        ProcessingPanel.Visibility = Visibility.Visible;
        ProcessingTitle.Text = title;
        ProcessingDetail.Text = detail;
        ProcessingProgress.IsIndeterminate = running;
        ProcessingProgress.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        ProcessingAction.Content = running ? UiText.T("요약 취소") : UiText.T("확인");
        ProcessingAction.IsEnabled = !running || canCancel;
    }
    private void ProcessingActionClick(object sender, RoutedEventArgs e)
    {
        if (ProcessingProgress.IsIndeterminate)
        {
            var cancellation = summaryCancellation ?? startupCancellation;
            if (cancellation == null) return;
            cancellation.Cancel();
            ProcessingDetail.Text = UiText.T("취소 처리 중… 기존 저장 파일은 유지됩니다.");
            ProcessingAction.IsEnabled = false;
        }
        else ProcessingPanel.Visibility = Visibility.Collapsed;
    }
    private async void RetrySummary(object sender, RoutedEventArgs e)
    {
        if (busy || (session?.Folder??lastFolder) is not string folder || settings.SelectedSummary.Id=="none") return;
        bool live=session!=null;
        busy = true;
        try { session?.FlushCaptions();await GenerateSummaryAsync(folder, live?null:lastSourceWarning,true); }
        catch(Exception ex){ShowProcessing(UiText.T("요약을 완료하지 못했습니다"),UiText.Status(ex.Message),false,false);}
        finally
        {
            busy = false;
            SetMainAction(live?"stop":"play", UiText.T(live?"녹음 중지 및 요약":"녹음 시작"));
            StartButton.IsEnabled = SummaryPicker.IsEnabled = SummaryRetryButton.IsEnabled = true;SettingsButton.IsEnabled=!live;
            SourcePicker.IsEnabled = TargetPicker.IsEnabled = TopicInput.IsEnabled = !live;
            ModePicker.IsEnabled = !live;
            RefreshSummaryControls();
            if(pendingSessionFailure is { } failure&&session!=null){pendingSessionFailure=null;await StopAsync(false);if(!closeAfterStop){StatusText.Text=UiText.Status(failure.Message);ShowProcessing(UiText.T("녹음 중단"),StatusText.Text,false,false);}}
            if (closeWhenReady&&!closeAfterStop) { if(session!=null)await StopAsync();else{closeAfterStop=true;Close();} }
        }
    }
    private void ResumeFollowing(object sender, RoutedEventArgs e) => historyFollow.Resume();
    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.LeftButton != MouseButtonState.Pressed) return;
        e.Handled = true;
        DragMove();
    }
    private void ResizeWindow(object sender, MouseButtonEventArgs e)
    {
        if (WindowState != WindowState.Normal || e.ChangedButton != MouseButton.Left ||
            e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement edge ||
            !int.TryParse(edge.Tag?.ToString(), out var direction) || direction is < 1 or > 8) return;
        e.Handled = true;
        ReleaseCapture();
        SendMessage(new WindowInteropHelper(this).Handle, 0x0112, new IntPtr(0xF000 + direction), IntPtr.Zero);
    }
    private void OpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = lastFolder ?? settings.OutputFolder;
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { StatusText.Text = UiText.Status(ex.Message); }
    }
    private void Minimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void SaveAppearance(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        settings.CaptionFontSize=FontSizeSlider.Value;settings.WindowOpacity=Opacity=OpacitySlider.Value;
        if(!IsLoaded)return;
        try{settings.Save();}catch(Exception ex){StatusText.Text=UiText.T("화면 설정 저장 실패: ")+ex.Message;}
    }
    public void SetPreview()
    {
        EmptyText.Visibility = Visibility.Collapsed;
        StatusText.Text = UiText.T("미리보기 · 실제 음성을 처리하고 있지 않습니다");
        rows.Add(new(new(1, "preview", 0, 3, "A small window. A clearer understanding.", "작은 창으로, 더 선명하게 이해하세요.", "done")));
        rows.Add(new(new(2, "preview2", 3, 7, "Keep listening. Your words are saved locally.", "계속 들어보세요. 말한 내용은 PC에 저장됩니다.", "done")));
    }
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}















