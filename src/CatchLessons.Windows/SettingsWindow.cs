using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Diagnostics;

namespace CatchLessons;

public sealed class SettingsWindow : Window
{
    internal static bool IsSettingInput(object source)=>source is FrameworkElement {TemplatedParent:null} && source is TextBox or ComboBox or ToggleButton;
    internal static bool HasOpenDropdown(DependencyObject root)
    {
        if(root is ComboBox {IsDropDownOpen:true})return true;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(HasOpenDropdown(VisualTreeHelper.GetChild(root,i)))return true;
        return false;
    }
    public Settings Value { get; private set; }
    public string? VerifiedAgentKey { get; private set; }
    public bool LanguageChanged {get;private set;}
    public bool UpdateStarted {get;private set;}
    public SettingsWindow(Settings current,bool openLanguageTab=false)
    {
        Value = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(current))!;UiText.Language=current.UiLanguage;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = UiText.T("CatchLessons 설정");
        Width = 700; Height = Math.Min(740, SystemParameters.WorkArea.Height - 60);
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var frame = new Border { CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), Padding = new Thickness(26) };
        frame.SetResourceReference(Border.BackgroundProperty, "Surface");
        frame.SetResourceReference(Border.BorderBrushProperty, "Line");
        var layout = new Grid();
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        frame.Child = layout; Content = frame;

        Button IconButton(string glyph, string label)
        {
            var button = new Button { Content = label, Tag = FindResource("Icon-" + glyph), Style = (Style)FindResource("IconButton") };
            return button;
        }
        var header = new DockPanel { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 22) };
        var close = IconButton("x", UiText.T("설정 닫기")); DockPanel.SetDock(close, Dock.Right);
        close.Click += (_, _) => Close(); header.Children.Add(close);
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = UiText.T("설정"), FontSize = 26, FontWeight = FontWeights.SemiBold });
        var intro = new TextBlock { Text = UiText.T("언어와 입력을 맞추고, 자막에 집중하세요."), Margin = new Thickness(0, 6, 0, 0) };
        intro.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); heading.Children.Add(intro); header.Children.Add(heading);
        header.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
        layout.Children.Add(header);

        var tabs = new TabControl(); Grid.SetRow(tabs, 1); layout.Children.Add(tabs);
        StackPanel Page(string title)
        {
            var panel = new StackPanel { Margin = new Thickness(1, 6, 12, 4) };
            tabs.Items.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } });
            return panel;
        }
        void Label(Panel panel, string title, string? help = null)
        {
            title=UiText.T(title);if(help!=null)help=UiText.T(help);
            panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
            if (help != null)
            {
                var hint = new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 5, 0, 7) };
                hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); panel.Children.Add(hint);
            }
        }
        ComboBox Choice(Panel panel, object items, string display, string key, object? selected)
        {
            var combo = new ComboBox { ItemsSource = (System.Collections.IEnumerable)items, DisplayMemberPath = display, SelectedValuePath = key, Margin = new Thickness(0, 6, 0, 16) };
            combo.SelectedValue = selected; panel.Children.Add(combo); return combo;
        }

        var general=Page(UiText.T("일반"));
        Label(general,UiText.T("앱 언어"));
        var uiLanguage=Choice(general,new[]{new{Id="en",Name="English"},new{Id="ko",Name="한국어"}},"Name","Id",current.UiLanguage);
        Label(general,UiText.T("앱 버전"));general.Children.Add(new TextBlock{Text=UpdateService.CurrentVersion,Margin=new Thickness(0,6,0,12)});
        var updateStatus=new TextBlock{Text=UpdateService.RepositoryConfigured?"":UiText.T("업데이트 저장소가 설정되지 않았습니다."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,8)};general.Children.Add(updateStatus);
        var updateCheck=new Button{Content=UiText.T("업데이트 확인"),IsEnabled=UpdateService.RepositoryConfigured};var updateDownload=new Button{Content=UiText.T("업데이트"),Visibility=Visibility.Collapsed};general.Children.Add(updateCheck);general.Children.Add(updateDownload);
        UpdateResult? update=null;Func<bool> saveBeforeUpdate=()=>false;var updateLife=new CancellationTokenSource();var updateProgress=new ProgressBar{Minimum=0,Maximum=100,Height=4,Visibility=Visibility.Collapsed};general.Children.Add(updateProgress);
        updateCheck.Click+=async(_,_)=>{updateCheck.IsEnabled=false;updateDownload.Visibility=Visibility.Collapsed;try{updateStatus.Text=UiText.T("업데이트 확인 중…");update=await UpdateService.CheckAsync(false,updateLife.Token);updateStatus.Text=update.Newer?(update.Download==null?UiText.T("새 버전이 있지만 설치 파일을 찾지 못했습니다."):UiText.T("새 버전 사용 가능: ")+update.Version):UiText.T("최신 버전입니다.");updateDownload.Visibility=update.Newer&&update.Download!=null?Visibility.Visible:Visibility.Collapsed;}catch(OperationCanceledException){}catch(Exception ex){updateStatus.Text=UiText.T("업데이트 확인 실패: ")+ex.Message;}finally{updateCheck.IsEnabled=UpdateService.RepositoryConfigured;}};
        updateDownload.Click+=async(_,_)=>{updateCheck.IsEnabled=false;updateDownload.IsEnabled=false;updateProgress.Visibility=Visibility.Visible;updateProgress.IsIndeterminate=true;try{UpdateService.WindowsInstallLocation();updateStatus.Text=UiText.T("업데이트 준비 중…");var progress=new Progress<int?>(n=>{updateProgress.IsIndeterminate=!n.HasValue;if(n.HasValue)updateProgress.Value=n.Value;updateStatus.Text=UiText.T("업데이트 준비 중…")+(n.HasValue?" "+n+"%":"");});var path=await UpdateService.DownloadAsync(update!,progress,updateLife.Token);updateProgress.IsIndeterminate=true;updateStatus.Text=UiText.T("업데이트 설치 시작 중…");if(!saveBeforeUpdate())throw new IOException("Could not save settings before updating.");UpdateService.StartWindowsInstaller(path);UpdateStarted=true;Close();}catch(OperationCanceledException){}catch(Exception ex){updateProgress.Visibility=Visibility.Collapsed;updateStatus.Text=UiText.T("업데이트 실패: ")+ex.Message;updateCheck.IsEnabled=true;updateDownload.IsEnabled=true;}};
        var connection = Page(UiText.T("연결"));
        Label(connection, "Agent", UiText.T("녹음마다 새로운 대화를 만들고, 그 안에서 번역과 요약을 이어갑니다. 선택하면 실제 응답으로 연결을 확인합니다."));
        var agentPicker = Choice(connection, new[] { new { Id="chatgpt", Name="ChatGPT", Icon=FindResource("Icon-brand-openai") }, new { Id="claude", Name="Claude", Icon=FindResource("Icon-brand-claude") } }, "", "Id", current.AgentProvider);
        agentPicker.ItemTemplate = (DataTemplate)FindResource("AgentChoiceTemplate");
        var agentStatus = new TextBlock { Text=UiText.T("Agent를 선택하면 연결을 확인합니다."), TextWrapping=TextWrapping.Wrap }; connection.Children.Add(agentStatus);
        var agentTest = IconButton("arrows-clockwise", UiText.T("Agent 연결 다시 확인"));
        connection.Children.Remove(agentPicker);
        var agentRow=new Grid {Margin=new Thickness(0,6,0,16)};agentRow.ColumnDefinitions.Add(new());agentRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        agentPicker.Margin=new Thickness(0,0,8,0);Grid.SetColumn(agentTest,1);agentRow.Children.Add(agentPicker);agentRow.Children.Add(agentTest);connection.Children.Insert(connection.Children.IndexOf(agentStatus),agentRow);
        var modelInputs=new Dictionary<string,ComboBox>();var effortInputs=new Dictionary<string,ComboBox>();var modelPanels=new Dictionary<string,StackPanel>();Action? optionsChanged=null;ToggleButton? fastMode=null;
        foreach(var provider in new[]{"chatgpt","claude"})
        {
            var panel=new StackPanel();connection.Children.Add(panel);modelPanels[provider]=panel;
            Label(panel,UiText.T("모델 · 다음 녹음부터 적용"));
            var model=Choice(panel,AgentModels.Read(provider),"Id","Id",provider=="claude"?current.ClaudeModel:current.CodexModel);modelInputs[provider]=model;
            Label(panel,UiText.T("Effort · 낮을수록 속도 우선"));
            var effort=Choice(panel,AgentModels.Efforts(provider,model.SelectedValue as string??""),"Label","Id",provider=="claude"?current.ClaudeEffort:current.CodexEffort);effortInputs[provider]=effort;
            if(provider=="chatgpt"){fastMode=Switch(panel,UiText.T("Fast 모드"),current.CodexFastMode);fastMode.Checked+=(_,_)=>optionsChanged?.Invoke();fastMode.Unchecked+=(_,_)=>optionsChanged?.Invoke();}
            model.SelectionChanged+=(_,_)=>{var selected=effort.SelectedValue as string;effort.ItemsSource=AgentModels.Efforts(provider,model.SelectedValue as string??"");effort.SelectedValue=selected??"low";if(effort.SelectedIndex<0)effort.SelectedIndex=0;optionsChanged?.Invoke();};
            effort.SelectionChanged+=(_,_)=>optionsChanged?.Invoke();
        }
        void ShowAgentOptions(){foreach(var pair in modelPanels)pair.Value.Visibility=pair.Key==(agentPicker.SelectedValue as string)?Visibility.Visible:Visibility.Collapsed;}
        void ReadAgentOptions(Settings s){s.CodexFastMode=fastMode?.IsChecked==true;s.CodexModel=modelInputs["chatgpt"].SelectedValue as string??"gpt-6-luna";s.CodexEffort=effortInputs["chatgpt"].SelectedValue as string??"low";s.ClaudeModel=modelInputs["claude"].SelectedValue as string??"haiku";s.ClaudeEffort=effortInputs["claude"].SelectedValue as string??"low";}
        ShowAgentOptions();
        Label(connection, UiText.T("음성 인식 엔진"));
        var engineOptions=LocalModels.WhisperEnabled?new[] { new {Id="whisper",Name=UiText.T("로컬 Whisper (무료)")},new {Id="openai",Name="OpenAI API"} }:new[]{new{Id="openai",Name="OpenAI API"}};
        var engine = Choice(connection,engineOptions,"Name","Id",LocalModels.WhisperEnabled?current.SpeechEngine:"openai");
        var localPanel = new StackPanel(); connection.Children.Add(localPanel);
        Label(localPanel, UiText.T("로컬 음성 모델"), UiText.T("첫 시작에 모델을 내려받아 PC에 보관합니다. 음성 인식과 언어 감지에는 API 키나 사용 요금이 없습니다."));
        var localModel = Choice(localPanel, LocalModels.Choices, "DisplayName", "Id", LocalModels.Resolve(current.LocalModel).Id);
        var cpuOnly = new CheckBox { Content = UiText.T("CPU만 사용 (GPU 문제 발생 시)"), IsChecked = current.LocalCpuOnly };
        localPanel.Children.Add(cpuOnly);
        Label(localPanel, UiText.T("가속과 품질"), UiText.T("GPU 가속을 사용할 수 없으면 CPU로 실행합니다. 느리면 가벼운 모델을 선택하세요."));
        var apiPanel = new StackPanel(); connection.Children.Add(apiPanel);
        Label(apiPanel,UiText.T("OpenAI API 키"),UiText.T("선택한 음성은 OpenAI로 전송되며 API 비용이 발생합니다. 키는 현재 Windows 사용자 계정으로 암호화합니다."));
        var apiKey = new PasswordBox(); apiPanel.Children.Add(apiKey); bool keyLoaded=true;
        try { apiKey.Password=current.GetKey(); } catch { keyLoaded=false; }
        void EngineChanged() { bool cloud=engine.SelectedValue as string=="openai"; localPanel.Visibility=cloud?Visibility.Collapsed:Visibility.Visible; apiPanel.Visibility=cloud?Visibility.Visible:Visibility.Collapsed; }
        engine.SelectionChanged += (_,_)=>EngineChanged(); EngineChanged();
        TextBox ExecutablePath(string title,string path,string provider)
        {
            Label(connection,title);
            var input=new TextBox{Text=string.IsNullOrWhiteSpace(path)?CodexTranslator.DetectPath(provider):path,IsReadOnly=true,Cursor=Cursors.Hand,ToolTip=UiText.T("클릭하여 실행 파일 선택")};
            void Browse(){var dialog=new OpenFileDialog{Title=title,Filter=UiText.T("CLI 실행 파일|*.exe;*.js|모든 파일|*.*"),CheckFileExists=true};if(dialog.ShowDialog(this)==true)input.Text=dialog.FileName;}
            input.PreviewMouseLeftButtonDown+=(_,e)=>{e.Handled=true;Browse();};input.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Enter or Key.Space){e.Handled=true;Browse();}};
            connection.Children.Add(input);return input;
        }
        var codex=ExecutablePath(UiText.T("Codex 실행 경로"),current.CodexPath,"chatgpt");
        var claude=ExecutablePath(UiText.T("Claude 실행 경로"),current.ClaudePath,"claude");
        Label(connection,"Custom Instruction",UiText.T("번역 방식·말투·용어 규칙을 설정하세요. 시작 시 세션 지침으로 고정되며 다음 녹음부터 변경값을 적용합니다."));
        var custom = new TextBox {Text=current.CustomInstruction,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=140,MaxLength=8000,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}; connection.Children.Add(custom);
        Label(connection,UiText.T("중지 후 후속 작업"),UiText.T("요약 완료 후 같은 Agent가 실행합니다. 설치된 Skill·MCP를 사용하며 기존 권한·승인 정책을 따릅니다. 비워두면 실행하지 않습니다."));
        var postStop = new TextBox {Text=current.PostStopPrompt,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=140,MaxLength=8000,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};connection.Children.Add(postStop);
        CancellationTokenSource? agentCheck=null;
        async Task CheckAgent()
        {
            agentCheck?.Cancel(); using var check=new CancellationTokenSource(); agentCheck=check; VerifiedAgentKey=null;
            var candidate = new Settings {AgentProvider=agentPicker.SelectedValue as string??"chatgpt",CodexPath=codex.Text.Trim(),ClaudePath=claude.Text.Trim()};
            ReadAgentOptions(candidate);
            agentStatus.Text=UiText.T("실제 Agent 응답 확인 중…");
            try { await CodexTranslator.CheckConnectionAsync(candidate,check.Token); if(agentCheck==check){agentStatus.Text=(candidate.AgentProvider=="claude"?"Claude":"ChatGPT")+" "+UiText.T("연결됨")+" · "+candidate.AgentModel+" / "+candidate.AgentEffort;VerifiedAgentKey=candidate.AgentKey;} }
            catch(Exception ex){if(agentCheck==check)agentStatus.Text=check.IsCancellationRequested?UiText.T("연결 확인 취소"):UiText.Status(ex.Message);}
            finally {if(agentCheck==check)agentCheck=null;}
        }
        optionsChanged=()=>{if(IsVisible)_=CheckAgent();};
        agentPicker.SelectionChanged += async (_,_)=>{ShowAgentOptions();if(IsVisible)await CheckAgent();};
        agentTest.Click += async (_,_)=>await CheckAgent();

        var language = Page(UiText.T("언어"));
        Label(language, UiText.T("원문 언어"), UiText.T("자동 감지는 음성 구간마다 언어를 판단합니다. 알고 있는 언어를 고정하면 오인식을 줄일 수 있습니다."));
        var source = Choice(language, Languages.Sources.ToArray(), "Label", "Code", Languages.Source(current.SourceLanguage).Code);
        Label(language, UiText.T("번역 언어"), UiText.T("실시간 번역에 적용합니다. 요약 언어는 요약 탭에서 별도로 지정합니다. 언어 목록에서 글자를 입력해 해당 항목으로 이동할 수 있습니다."));
        var target = Choice(language, Languages.All, "Label", "Code", Languages.Target(current.TargetLanguage).Code);
        Label(language, UiText.T("전문용어와 고유명사"), UiText.T("한 줄에 하나씩 입력하세요. 실제 음성에 나오는 용어를 넣으면 인식에 도움이 됩니다."));
        var terms = new TextBox { Text = current.Terms, AcceptsReturn = true, Height = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; language.Children.Add(terms);

        var recordingPage = Page(UiText.T("녹음·저장"));
        Label(recordingPage, UiText.T("시스템 출력 장치"), UiText.T("선택한 장치에서 재생되는 소리를 듣습니다. 마이크는 사용하지 않습니다."));
        var devices = new List<AudioDevice> { new("", UiText.T("기본 출력 장치")) };
        try { devices.AddRange(AudioCapture.Devices()); } catch { }
        var device = Choice(recordingPage, devices, "Name", "Id", current.DeviceId ?? "");
        if (device.SelectedIndex < 0) device.SelectedIndex = 0;
        var audioRow = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var test = IconButton("speaker-high", UiText.T("오디오 입력 확인 (4초, 전송 없음)"));
        audioRow.Children.Add(test);
        var testResult = new TextBlock { Text = UiText.T("소리를 재생하고 입력을 확인하세요."), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
        testResult.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); audioRow.Children.Add(testResult); recordingPage.Children.Add(audioRow);
        CancellationTokenSource? audioTest = null;
        test.Click += async (_, _) =>
        {
            test.IsEnabled = false; testResult.Text = UiText.T("4초 동안 입력 확인 중…");
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(4)); audioTest = stop;
            try
            {
                using var capture = new AudioCapture(); capture.Start((string?)device.SelectedValue);
                float peak = 0;
                try { await capture.PumpAsync(f => { peak = Math.Max(peak, f.Peak); return Task.CompletedTask; }, stop.Token); }
                catch (OperationCanceledException) { }
                testResult.Text = peak > .001 ? UiText.T("오디오가 정상적으로 들어오고 있습니다.") : UiText.T("소리가 감지되지 않았습니다. 재생 장치를 확인하세요.");
            }
            catch (Exception ex) { testResult.Text = ex.Message; }
            finally { audioTest = null; test.IsEnabled = true; }
        };
        Label(recordingPage, UiText.T("자막 확정 속도"), UiText.T("문맥을 더 기다리면 자연스러워질 수 있지만 표시도 늦어집니다."));
        var pace = Choice(recordingPage, new[] { new { Id = "fast", Name = UiText.T("빠르게") }, new { Id = "balanced", Name = UiText.T("균형 (권장)") }, new { Id = "context", Name = UiText.T("문맥 우선") } }, "Name", "Id", current.CaptionPace);
        var streaming = new CheckBox { Content = UiText.T("말하는 중 원문 미리보기"), IsChecked = current.StreamingPreview };
        recordingPage.Children.Add(streaming);
        Label(recordingPage, UiText.T("미리보기"), UiText.T("약 2초부터 원문을 주기적으로 갱신합니다. 확정 전 문장은 바뀔 수 있습니다. PC가 느리면 이 기능을 끌 수 있습니다."));
        Label(recordingPage, UiText.T("저장 폴더"));
        var folderRow = new DockPanel { Height = 36, Margin = new Thickness(0, 5, 0, 16) };
        var openFolder = IconButton("folder-open", UiText.T("지정한 저장 폴더를 탐색기로 열기")); openFolder.Margin = new Thickness(6, 0, 0, 0); DockPanel.SetDock(openFolder, Dock.Right); folderRow.Children.Add(openFolder);
        var browse = IconButton("folder", UiText.T("저장 폴더 선택")); browse.Margin = new Thickness(6, 0, 0, 0); DockPanel.SetDock(browse, Dock.Right); folderRow.Children.Add(browse);
        var folder = new TextBox { Text = current.OutputFolder, Margin = new Thickness(0), Height = 36 }; folderRow.Children.Add(folder); recordingPage.Children.Add(folderRow);
        var folderNotice = new TextBlock { TextWrapping = TextWrapping.Wrap }; recordingPage.Children.Add(folderNotice);
        openFolder.Click += (_, _) =>
        {
            try
            {
                var path = folder.Text.Trim();
                if (!Path.IsPathFullyQualified(path)) throw new IOException(UiText.T("폴더의 전체 경로를 입력하세요."));
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                folderNotice.Text = "";
            }
            catch (Exception ex) { folderNotice.Text = ex.Message; }
        };
        browse.Click += (_, _) => { var dialog = new OpenFolderDialog { Title = UiText.T("자막 저장 폴더") }; if (dialog.ShowDialog(this) == true) folder.Text = dialog.FolderName; };
        var recording = new CheckBox { Content = UiText.T("원음도 WAV 파일로 저장"), IsChecked = current.RecordAudio }; recordingPage.Children.Add(recording);
        Label(recordingPage, UiText.T("자동 저장"), UiText.T("원문과 번역은 TXT/SRT로 저장합니다. 녹취 모드는 원문만 저장하며, 요약은 선택한 MD/TXT 형식으로 만듭니다."));

        var summaryPage = Page(UiText.T("요약"));
        ToggleButton Switch(Panel panel, string text, bool initial)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 9) };
            var toggle = new ToggleButton { Content = text, IsChecked = initial, Style = (Style)FindResource("SummarySwitch") };
            row.Children.Add(toggle); row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            panel.Children.Add(row); return toggle;
        }
        var summaryEnabled = Switch(summaryPage, UiText.T("중지 후 요약 실행"), current.SummaryEnabled);
        var autoOpen = Switch(summaryPage, UiText.T("요약 완료 후 저장 폴더 열기"), current.OpenFolderAfterSummary);
        Label(summaryPage, UiText.T("요약 언어"), UiText.T("실시간 번역 언어와 별도로 적용합니다. 녹취 모드에서도 사용할 수 있습니다."));
        var summaryLanguage = Choice(summaryPage, Languages.All, "Label", "Code", Languages.Target(current.SummaryLanguage).Code);
        Label(summaryPage,UiText.T("요약 파일 형식"));
        var summaryFormat=Choice(summaryPage,new[]{new{Id="md",Name="Markdown (.md)"},new{Id="txt",Name=UiText.T("일반 텍스트 (.txt)")}},"Name","Id",current.SummaryFormat);
        Label(summaryPage, UiText.T("카테고리 관리"), UiText.T("이름과 프롬프트를 편집하면 자동 저장됩니다. 삭제해도 기존 요약 파일은 지우지 않습니다."));
        var categories = new ObservableCollection<SummaryCategory>(Value.SummaryCategories);
        var editRow = new DockPanel { Height = 36, Margin = new Thickness(0, 6, 0, 12) };
        var removeCategory = IconButton("trash", UiText.T("선택한 카테고리 삭제")); removeCategory.Margin = new Thickness(6, 0, 0, 0); DockPanel.SetDock(removeCategory, Dock.Right); editRow.Children.Add(removeCategory);
        var addCategory = IconButton("plus", UiText.T("새 카테고리 추가")); addCategory.Margin = new Thickness(6, 0, 0, 0); DockPanel.SetDock(addCategory, Dock.Right); editRow.Children.Add(addCategory);
        var categoryEditor = new ComboBox { ItemsSource = categories, DisplayMemberPath = "DisplayName", SelectedValuePath = "Id" }; editRow.Children.Add(categoryEditor); summaryPage.Children.Add(editRow);
        Label(summaryPage, UiText.T("카테고리 이름"));
        var categoryName = new TextBox { MaxLength = 60 }; summaryPage.Children.Add(categoryName);
        Label(summaryPage, UiText.T("요약 프롬프트"), UiText.T("원하는 구성과 강조할 내용을 작성하세요. 파일명은 Codex가 내용에 맞춰 작성한 제목을 사용합니다."));
        var categoryPrompt = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 160, MaxLength = 12000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; summaryPage.Children.Add(categoryPrompt);
        void SelectCategory()
        {
            var selected = categoryEditor.SelectedItem as SummaryCategory;
            categoryName.IsEnabled = categoryPrompt.IsEnabled = removeCategory.IsEnabled = selected != null;
            categoryName.DataContext = categoryPrompt.DataContext = selected;
        }
        categoryName.SetBinding(TextBox.TextProperty, new Binding("Name") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        categoryPrompt.SetBinding(TextBox.TextProperty, new Binding("Prompt") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        categoryEditor.SelectionChanged += (_, _) => SelectCategory();
        categoryName.LostKeyboardFocus += (_, _) => categoryEditor.Items.Refresh();
        categoryEditor.SelectedValue = current.SummaryCategory;
        if (categoryEditor.SelectedIndex < 0 && categories.Count > 0) categoryEditor.SelectedIndex = 0;
        SelectCategory();
        addCategory.Click += (_, _) =>
        {
            var item = new SummaryCategory("custom-" + Guid.NewGuid().ToString("N"), UiText.T("새 카테고리"), UiText.T("원문에 근거해 주요 내용을 주제별로 정리하세요."));
            categories.Add(item); categoryEditor.SelectedItem = item; categoryName.Focus(); categoryName.SelectAll();
        };
        removeCategory.Click += (_, _) =>
        {
            if (categoryEditor.SelectedItem is not SummaryCategory selected) return;
            if (Value.SummaryCategory == selected.Id) { Value.SummaryCategory = "none"; summaryEnabled.IsChecked = false; }
            categories.Remove(selected); categoryEditor.SelectedIndex = categories.Count > 0 ? 0 : -1; SelectCategory();
        };

        var appearance = Page(UiText.T("화면"));
        Label(appearance, UiText.T("테마"), UiText.T("시스템 설정을 따르거나 밝은 화면과 어두운 화면을 선택하세요."));
        var theme = Choice(appearance, new[] { new { Id = "system", Name = UiText.T("시스템 설정") }, new { Id = "dark", Name = UiText.T("어두운 화면") }, new { Id = "light", Name = UiText.T("밝은 화면") } }, "Name", "Id", current.Theme);
        theme.SelectionChanged += (_, _) => ThemeManager.Apply(theme.SelectedValue as string ?? "system");
        Label(appearance, UiText.T("자막에 집중"), UiText.T("마우스를 벗어나면 조작부가 숨겨집니다. 아이콘 위에 마우스를 올리면 기능 설명이 표시됩니다."));
        Label(appearance, UiText.T("창 배치"), UiText.T("제목 영역을 끌어 이동하고 가장자리와 모서리를 끌어 크기를 조절하세요."));
        var shortcutsPage=Page(UiText.T("단축키"));
        if(openLanguageTab)tabs.SelectedIndex=0;
        Label(shortcutsPage,UiText.T("전역 단축키"),UiText.T("다른 앱을 사용하거나 트레이에 숨겨도 동작합니다. Ctrl, Alt 또는 Win 조합을 입력하세요. Backspace/Delete로 해제합니다. 다른 앱과 충돌한 키는 실행되지 않습니다."));
        var shortcutError=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.IndianRed,Margin=new Thickness(0,4,0,8)};shortcutsPage.Children.Add(shortcutError);
        var shortcutInputs=new Dictionary<string,TextBox>();
        foreach(var action in ShortcutKeys.Actions)
        {
            var value=current.Shortcuts.GetValueOrDefault(action.Id,"");
            Label(shortcutsPage,action.Name);var input=new TextBox {Text=ShortcutKeys.Display(value,false),Tag=value,IsReadOnly=true,ToolTip=UiText.T("클릭 후 키 조합 입력 · Backspace로 해제")};shortcutsPage.Children.Add(input);shortcutInputs[action.Id]=input;
            input.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Tab)return;e.Handled=true;var key=e.Key==Key.System?e.SystemKey:e.Key;var mods=Keyboard.Modifiers;int flags=(mods.HasFlag(ModifierKeys.Control)?1:0)|(mods.HasFlag(ModifierKeys.Alt)?2:0)|(mods.HasFlag(ModifierKeys.Shift)?4:0)|(mods.HasFlag(ModifierKeys.Windows)?8:0);try{var captured=ShortcutKeys.Capture(flags,key.ToString(),false);input.Tag=captured;input.Text=ShortcutKeys.Display(captured,false);}catch(ArgumentException){}};
        }

        string? checkedShortcutSignature=null;bool checkedShortcutsValid=true;
        bool CheckShortcuts()
        {
            var keys=shortcutInputs.ToDictionary(p=>p.Key,p=>p.Value.Tag as string??"");
            var signature=JsonSerializer.Serialize(keys);
            if(signature==checkedShortcutSignature)return checkedShortcutsValid;
            checkedShortcutSignature=signature;
            try{ShortcutKeys.Validate(keys);}catch(ArgumentException ex){shortcutError.Text=UiText.Status(ex.Message);checkedShortcutsValid=false;return false;}
            checkedShortcutsValid=true;
            var conflict=WindowsHotkeys.Check(keys);shortcutError.Text=conflict==null?"":UiText.T("현재 사용할 수 없는 단축키:\n")+conflict;return true;
        }
        var footer = new StackPanel { Margin = new Thickness(0, 16, 0, 0) }; Grid.SetRow(footer, 2); layout.Children.Add(footer);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        error.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); footer.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        
        var save = new Button { Content = UiText.T("닫기"), MinWidth = 100, Style = (Style)FindResource("PrimaryButton"), IsDefault = true }; actions.Children.Add(save); footer.Children.Add(actions);
        bool Persist(bool closeWindow) {
            try
            {
                if (!Path.IsPathFullyQualified(folder.Text.Trim())) throw new IOException(UiText.T("저장 폴더의 전체 경로를 입력하세요."));
                Value.OutputFolder = folder.Text.Trim(); Value.CodexPath = codex.Text.Trim();
                Value.AgentProvider=agentPicker.SelectedValue as string??"chatgpt"; Value.ClaudePath=claude.Text.Trim(); Value.CustomInstruction=custom.Text;
                ReadAgentOptions(Value);
                Value.PostStopPrompt=postStop.Text;Value.Shortcuts=shortcutInputs.ToDictionary(p=>p.Key,p=>p.Value.Tag as string??"");
                Value.SpeechEngine=engine.SelectedValue as string??"whisper"; if(keyLoaded||apiKey.Password.Length>0)Value.SetKey(apiKey.Password);
                Value.DeviceId = (string?)device.SelectedValue; Value.RecordAudio = recording.IsChecked == true;
                Value.CaptionPace = pace.SelectedValue as string ?? "balanced";
                Value.StreamingPreview = streaming.IsChecked == true;
                Value.SummaryCategories = categories.ToList();
                Value.SummaryEnabled = summaryEnabled.IsChecked == true;
                if (Value.SummaryEnabled && Value.SelectedSummary.Id == "none")
                {
                    Value.SummaryCategory = (categoryEditor.SelectedItem as SummaryCategory)?.Id ?? Value.SummaryCategories.FirstOrDefault()?.Id ?? "none";
                    Value.SummaryEnabled = Value.SummaryCategory != "none";
                }
                Value.OpenFolderAfterSummary = autoOpen.IsChecked == true;
                Value.SummaryLanguage = Languages.Target(summaryLanguage.SelectedValue as string).Code;
                Value.SummaryFormat=summaryFormat.SelectedValue as string??"md";
                Value.LocalModel = localModel.SelectedValue as string ?? "balanced";
                Value.LocalCpuOnly = cpuOnly.IsChecked == true;
                Value.SourceLanguage = Languages.Source(source.SelectedValue as string).Code;
                Value.TargetLanguage = Languages.Target(target.SelectedValue as string).Code;
                Value.UiLanguage = uiLanguage.SelectedValue as string??"en";
                Value.Theme = theme.SelectedValue as string ?? "system";
                Value.Terms = terms.Text.Trim();
                if (Value.Keywords.Length > 50 || Value.Keywords.Any(t => t.Length > 100 || t.IndexOfAny(['<', '>']) >= 0))
                    throw new IOException(UiText.T("용어는 최대 50개, 각 100자 이하로 입력하세요. < > 문자는 제외해 주세요."));
                if(!CheckShortcuts())return false;
                Value.Save(); error.Text = UiText.T("자동 저장됨"); if(closeWindow) DialogResult = true; return true;
            }
            catch (Exception ex) { error.Text = UiText.Status(ex.Message); return false; }
        }
        save.Click += (_,_)=>Persist(true);
        bool interactive=false;var saveGate=new SettingsSaveGate();
        uiLanguage.SelectionChanged+=(_,_)=>{if(!interactive||uiLanguage.SelectedValue as string==current.UiLanguage)return;if(Persist(false)){LanguageChanged=true;Close();}};
        Loaded+=(_,_)=>{if(IsVisible){interactive=true;CheckShortcuts();}};
        saveBeforeUpdate=()=>Persist(false);
        var autosave=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(700)};
        void Schedule(){if(!interactive)return;autosave.Stop();autosave.Start();}
        bool TextFocused()=>Keyboard.FocusedElement is TextBox or PasswordBox;
        void TextChanged(bool focused){if(!interactive)return;if(saveGate.TextChanged(focused))Schedule();else autosave.Stop();}
        autosave.Tick+=(_,_)=>{if(!SettingsSaveGate.CanRunTimer(TextFocused(),HasOpenDropdown((DependencyObject)Content)))return;autosave.Stop();if(Persist(false))saveGate.Saved();};
        AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,e)=>{if(IsSettingInput(e.OriginalSource)&&e.OriginalSource is TextBox input)TextChanged(input.IsKeyboardFocusWithin);}));
        AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,new SelectionChangedEventHandler((_,e)=>{if(IsSettingInput(e.OriginalSource))Schedule();}));
        AddHandler(ToggleButton.CheckedEvent,new RoutedEventHandler((_,e)=>{if(IsSettingInput(e.OriginalSource))Schedule();}));
        AddHandler(ToggleButton.UncheckedEvent,new RoutedEventHandler((_,e)=>{if(IsSettingInput(e.OriginalSource))Schedule();}));
        apiKey.PasswordChanged+=(_,_)=>TextChanged(apiKey.IsKeyboardFocusWithin);
        AddHandler(Keyboard.LostKeyboardFocusEvent,new KeyboardFocusChangedEventHandler((_,e)=>
        {
            if(!interactive||!saveGate.HasTextEdits||e.OriginalSource is not (TextBox or PasswordBox))return;
            autosave.Stop();
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>
            {
                if(!interactive||!saveGate.HasTextEdits)return;
                if(HasOpenDropdown((DependencyObject)Content)){Schedule();return;}
                if(Persist(false))saveGate.Saved();
            }));
        }));
        Closing+=(_,e)=>{autosave.Stop();if(interactive&&!Persist(false))e.Cancel=true;};
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape&&!shortcutInputs.Values.Any(i=>i.IsKeyboardFocusWithin)) { e.Handled = true; Close(); } };
        Closed += (_, _) => { interactive=false;autosave.Stop();updateLife.Cancel();audioTest?.Cancel(); agentCheck?.Cancel(); ThemeManager.Apply(Value.Theme); };
    }
}






