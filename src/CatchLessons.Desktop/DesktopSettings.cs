using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using System.Diagnostics;
using System.Text.Json;

namespace CatchLessons.Desktop;

internal sealed class DesktopSettings:Window
{
    public bool LanguageChanged {get;private set;}
    public bool UpdateStarted {get;private set;}
    public DesktopSettings(Settings current,bool openLanguageTab=false)
    {
        UiText.Language=current.UiLanguage;
        Title=UiText.T("CatchLessons 설정");Width=720;Height=760;MinWidth=620;MinHeight=500;
        var value=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(current))!;
        var root=new DockPanel{Margin=new Thickness(24)};Content=root;
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap};DockPanel.SetDock(status,Dock.Bottom);root.Children.Add(status);
        var done=Ui.Button(UiText.T("닫기"),()=>Close());DockPanel.SetDock(done,Dock.Bottom);root.Children.Add(done);
        var tabs=new TabControl();root.Children.Add(tabs);var pages=new List<TabItem>();
        StackPanel Page(string name){var panel=new StackPanel{Spacing=4};pages.Add(new(){Header=name,Content=new ScrollViewer{Content=panel}});return panel;}
        var general=Page(UiText.T("일반"));Ui.Label(general,UiText.T("앱 언어"));var uiLanguage=Ui.Choice(new[]{"English","한국어"},current.UiLanguage=="ko"?"한국어":"English");general.Children.Add(uiLanguage);Ui.Label(general,UiText.T("앱 버전"));general.Children.Add(new TextBlock{Text=UpdateService.CurrentVersion});
        var updateStatus=new TextBlock{Text=UpdateService.RepositoryConfigured?"":UiText.T("업데이트 저장소가 설정되지 않았습니다."),TextWrapping=TextWrapping.Wrap};general.Children.Add(updateStatus);UpdateResult? update=null;Func<bool> saveBeforeUpdate=()=>false;var updateLife=new CancellationTokenSource();bool installingUpdate=false;Closing+=(_,e)=>{if(installingUpdate)e.Cancel=true;};
        var updateProgress=new ProgressBar{Minimum=0,Maximum=100,Height=4,IsVisible=false};
        Button download=null!,updateCheck=null!;download=Ui.Button(UiText.T("업데이트"),downloadUpdate);download.IsVisible=false;
        async void downloadUpdate(){download.IsEnabled=false;updateCheck.IsEnabled=false;updateProgress.IsVisible=true;updateProgress.IsIndeterminate=true;try{if(!saveBeforeUpdate())throw new IOException("Could not save settings before updating.");if(OperatingSystem.IsMacOS())UpdateService.EnsureMacInstalled();else UpdateService.WindowsInstallLocation();updateStatus.Text=UiText.T("업데이트 준비 중…");var progress=new Progress<int?>(n=>{updateProgress.IsIndeterminate=!n.HasValue;if(n.HasValue)updateProgress.Value=n.Value;updateStatus.Text=UiText.T("업데이트 준비 중…")+(n.HasValue?" "+n+"%":"");});var path=await UpdateService.DownloadAsync(update!,progress,updateLife.Token);updateProgress.IsIndeterminate=true;updateStatus.Text=UiText.T("업데이트 설치 시작 중…");if(OperatingSystem.IsMacOS()){updateStatus.Text=UiText.T("관리자 인증과 설치를 기다리는 중…");installingUpdate=true;try{await UpdateService.InstallMacAsync(path);}finally{installingUpdate=false;}}else UpdateService.StartWindowsInstaller(path);UpdateStarted=true;Close();}catch(OperationCanceledException){}catch(Exception ex){updateProgress.IsVisible=false;updateStatus.Text=UiText.T("업데이트 실패: ")+ex.Message;download.IsEnabled=true;updateCheck.IsEnabled=UpdateService.RepositoryConfigured;}}
        updateCheck=Ui.Button(UiText.T("업데이트 확인"),async()=>{try{updateStatus.Text=UiText.T("업데이트 확인 중…");update=await UpdateService.CheckAsync(OperatingSystem.IsMacOS(),updateLife.Token);updateStatus.Text=update.Newer?(update.Download==null?UiText.T("새 버전이 있지만 설치 파일을 찾지 못했습니다."):UiText.T("새 버전 사용 가능: ")+update.Version):UiText.T("최신 버전입니다.");download.IsVisible=update.Newer&&update.Download!=null;}catch(OperationCanceledException){}catch(Exception ex){updateStatus.Text=UiText.T("업데이트 확인 실패: ")+ex.Message;}});updateCheck.IsEnabled=UpdateService.RepositoryConfigured;general.Children.Add(updateCheck);general.Children.Add(download);general.Children.Add(updateProgress);Closed+=(_,_)=>updateLife.Cancel();
        var connection=Page(UiText.T("연결"));var connectionTab=pages[^1];var checkLife=new CancellationTokenSource();Closed+=(_,_)=>checkLife.Cancel();
        var apiKeyReadError=false;var apiKeyLoaded=true;var apiKeyDraft="";Button? summaryKey=null;
        try{apiKeyDraft=current.GetKey();}catch{apiKeyLoaded=false;apiKeyReadError=true;}
        bool keyDialogOpen=false,revertingProvider=false,apiKeySaveFailed=false;ComboBox? summaryProvider=null;
        Func<Task<bool>> openApiKey=()=>Task.FromResult(false);Func<Task> checkAgent=()=>Task.CompletedTask;Action openAgentSettings=()=>{};
        Button ApiKeyButton(Action action)=>Ui.Icon("key",UiText.T("공통 API Key 관리"),action);
        Grid ProviderRow(Panel parent,ComboBox provider,Button keyButton)
        {
            var row=new Grid{ColumnDefinitions=new ColumnDefinitions("*,42")};provider.Margin=new Thickness(0,5,6,10);row.Children.Add(provider);keyButton.Margin=new Thickness(3,5,3,10);keyButton.VerticalAlignment=VerticalAlignment.Top;Grid.SetColumn(keyButton,1);row.Children.Add(keyButton);parent.Children.Add(row);return row;
        }
        var translationPanel=new StackPanel{Spacing=4};connection.Children.Add(translationPanel);Ui.Label(translationPanel,UiText.T("번역 Provider"));
        var translationProvider=Ui.Choice(new[]{UiText.T("Agent"),"OpenAI API"},current.TranslationProvider=="openai"?"OpenAI API":UiText.T("Agent"));
        var translationKey=ApiKeyButton(()=>{_=openApiKey();});ProviderRow(translationPanel,translationProvider,translationKey);
        translationPanel.Children.Add(new TextBlock{Text=UiText.T("Agent 번역은 현재 동작을 유지합니다. OpenAI API는 최근 맥락을 포함해 번역을 스트리밍합니다. API Key는 오른쪽 키 버튼에서 관리합니다."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
        bool agentSettingsOverride=false;Button summaryAgentSetup=null!,instructionsAgentSetup=null!;var agentSettings=new StackPanel{Spacing=4,IsVisible=current.TranslationProvider!="openai"};connection.Children.Add(agentSettings);Ui.Label(agentSettings,"Agent");
        var agent=Ui.Choice(new[]{"ChatGPT","Claude"},current.AgentProvider=="claude"?"Claude":"ChatGPT");
        agent.ItemTemplate=new Avalonia.Controls.Templates.FuncDataTemplate<string>((name,_)=>new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,Children={new PathIcon{Data=Ui.Symbol(name=="Claude"?"brand-claude":"brand-openai"),Width=18,Height=18},new TextBlock{Text=name}}});
        var agentStatus=new TextBlock{TextWrapping=TextWrapping.Wrap};var agentRow=new Grid{ColumnDefinitions=new ColumnDefinitions("*,42")};agent.Margin=new Thickness(0,5,6,10);agentRow.Children.Add(agent);
        var agentCheck=Ui.Icon("arrows-clockwise",UiText.T("Agent 연결 확인"),async()=>await checkAgent());agentCheck.Margin=new Thickness(3,5,3,10);agentCheck.VerticalAlignment=VerticalAlignment.Top;Grid.SetColumn(agentCheck,1);agentRow.Children.Add(agentCheck);agentSettings.Children.Add(agentRow);agentSettings.Children.Add(agentStatus);
        var fast=new ToggleSwitch{Content=UiText.T("Fast 모드"),OnContent=UiText.T("켬"),OffContent=UiText.T("끔"),FocusAdorner=null,IsChecked=current.CodexFastMode,IsVisible=value.AgentProvider=="chatgpt"};fast.GotFocus+=(_,_)=>fast.Opacity=.8;fast.LostFocus+=(_,_)=>fast.Opacity=1;agentSettings.Children.Add(fast);
        string optionProvider=value.AgentProvider;bool loadingOptions=true;
        Ui.Label(agentSettings,UiText.T("모델 · 다음 녹음부터 적용"));var modelChoice=Ui.Choice(AgentModels.Read(optionProvider),AgentModels.Read(optionProvider).FirstOrDefault(m=>m.Id==value.AgentModel));agentSettings.Children.Add(modelChoice);
        Ui.Label(agentSettings,UiText.T("Effort"));var effortChoice=Ui.Choice(AgentModels.Efforts(optionProvider,value.AgentModel),AgentModels.Efforts(optionProvider,value.AgentModel).FirstOrDefault(e=>e.Id==value.AgentEffort));agentSettings.Children.Add(effortChoice);
        void SaveOptions(){if(loadingOptions)return;value.CodexFastMode=fast.IsChecked==true;var m=(modelChoice.SelectedItem as AgentModel)?.Id;var e=(effortChoice.SelectedItem as AgentEffort)?.Id??"low";if(optionProvider=="claude"){value.ClaudeModel=m??"haiku";value.ClaudeEffort=e;}else{value.CodexModel=m??"gpt-6-luna";value.CodexEffort=e;}}
        var codexPathPanel=new StackPanel{Spacing=4};var claudePathPanel=new StackPanel{Spacing=4};
        void RefreshOptions(){loadingOptions=true;fast.IsVisible=optionProvider=="chatgpt";fast.IsChecked=value.CodexFastMode;codexPathPanel.IsVisible=optionProvider=="chatgpt";claudePathPanel.IsVisible=optionProvider=="claude";var modelName=optionProvider=="claude"?value.ClaudeModel:value.CodexModel;var effortName=optionProvider=="claude"?value.ClaudeEffort:value.CodexEffort;var models=AgentModels.Read(optionProvider);modelChoice.ItemsSource=models;modelChoice.SelectedItem=models.FirstOrDefault(m=>m.Id==modelName);var efforts=AgentModels.Efforts(optionProvider,modelName);effortChoice.ItemsSource=efforts;effortChoice.SelectedItem=efforts.FirstOrDefault(e=>e.Id==effortName)??efforts.FirstOrDefault();loadingOptions=false;}
        RefreshOptions();
        TextBox ExecutablePath(StackPanel parent,string title,string path,string provider)
        {
            Ui.Label(parent,title);var input=Ui.Input(string.IsNullOrWhiteSpace(path)?CodexTranslator.DetectPath(provider):path);input.IsReadOnly=true;ToolTip.SetTip(input,UiText.T("클릭하여 실행 파일 선택"));
            async Task Browse(){var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title=title,AllowMultiple=false});if(files.Count>0&&files[0].TryGetLocalPath() is string file)input.Text=file;}
            input.PointerPressed+=async(_,e)=>{e.Handled=true;await Browse();};input.KeyDown+=async(_,e)=>{if(e.Key is Key.Enter or Key.Space){e.Handled=true;await Browse();}};parent.Children.Add(input);return input;
        }
        var codex=ExecutablePath(codexPathPanel,UiText.T("Codex 실행 경로"),current.CodexPath,"chatgpt");
        var claude=ExecutablePath(claudePathPanel,UiText.T("Claude 실행 경로"),current.ClaudePath,"claude");
        agentSettings.Children.Add(codexPathPanel);agentSettings.Children.Add(claudePathPanel);
        async Task Check(){if(!IsVisible||!agentSettings.IsVisible)return;SaveOptions();try{var candidate=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(value))!;candidate.AgentProvider=agent.SelectedItem as string=="Claude"?"claude":"chatgpt";candidate.CodexPath=codex.Text??"";candidate.ClaudePath=claude.Text??"";agentStatus.Text=UiText.T("Agent 실제 응답 확인 중…");await CodexTranslator.CheckConnectionAsync(candidate,checkLife.Token);agentStatus.Text=(candidate.AgentProvider=="claude"?"Claude":"ChatGPT")+" "+UiText.T("연결됨");}catch(OperationCanceledException) when(checkLife.IsCancellationRequested){}catch(Exception ex){agentStatus.Text=UiText.Status(ex.Message);}}
        var enginePanel=new StackPanel{Spacing=4};connection.Children.Add(enginePanel);Ui.Label(enginePanel,UiText.T("음성 인식 엔진"));
        var engine=Ui.Choice(LocalModels.WhisperEnabled?new[]{UiText.T("로컬 Whisper"),"OpenAI API"}:new[]{"OpenAI API"},!LocalModels.WhisperEnabled||current.SpeechEngine=="openai"?"OpenAI API":UiText.T("로컬 Whisper"));
        var speechKey=ApiKeyButton(()=>{_=openApiKey();});ProviderRow(enginePanel,engine,speechKey);
        var local=new StackPanel{Spacing=4};enginePanel.Children.Add(local);Ui.Label(local,UiText.T("음성 모델"));var model=Ui.Choice(LocalModels.Choices,LocalModels.Resolve(current.LocalModel));local.Children.Add(model);
        var cpu=new CheckBox{Content=UiText.T("CPU만 사용"),IsChecked=current.LocalCpuOnly};local.Children.Add(cpu);
        var keyHint=new TextBlock{Text=UiText.T("음성 인식·번역·요약은 같은 보안 저장 API Key를 사용합니다."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};enginePanel.Children.Add(keyHint);
        void RefreshSpeechOptions()=>local.IsVisible=engine.SelectedItem as string!="OpenAI API";
        var language=Page(UiText.T("언어"));Ui.Label(language,UiText.T("원문 언어"));var source=Ui.Choice(Languages.Sources,Languages.Source(current.SourceLanguage));language.Children.Add(source);
        Ui.Label(language,UiText.T("번역 언어"));var target=Ui.Choice(Languages.All,Languages.Target(current.TargetLanguage));language.Children.Add(target);
        Ui.Label(language,UiText.T("전문용어 (한 줄에 하나)"));var terms=Ui.Input(current.Terms,true);language.Children.Add(terms);
        var storage=Page(UiText.T("녹음·저장"));Ui.Label(storage,UiText.T("출력 장치"));var devices=AudioPlatform.Devices();var device=Ui.Choice(devices,devices.FirstOrDefault(d=>d.Id==current.DeviceId)??devices.FirstOrDefault());storage.Children.Add(device);
        Ui.Label(storage,UiText.T("저장 폴더"));var folder=Ui.Input(current.OutputFolder);storage.Children.Add(folder);
        storage.Children.Add(Ui.Button(UiText.T("지정한 폴더 열기"),()=>{try{Directory.CreateDirectory(folder.Text!);Process.Start(new ProcessStartInfo(folder.Text!){UseShellExecute=true});}catch(Exception ex){status.Text=UiText.Status(ex.Message);}}));
        var wav=new CheckBox{Content=UiText.T("원음 WAV 저장"),IsChecked=current.RecordAudio};var streaming=new CheckBox{Content=UiText.T("자막 미리보기"),IsChecked=current.StreamingPreview};storage.Children.Add(wav);storage.Children.Add(streaming);
        var previewHelp=new TextBlock{Text=UiText.T("API 번역은 발화 중 원문이 충분히 바뀌면 최소 2초 간격으로 임시 번역을 요청합니다. 추가 API 요금이 발생하며, 끄면 확정 번역만 요청합니다."),TextWrapping=TextWrapping.Wrap};storage.Children.Add(previewHelp);
        void ShowPreviewHelp()=>previewHelp.IsVisible=translationProvider.SelectedItem as string=="OpenAI API";
        translationProvider.SelectionChanged+=(_,_)=>ShowPreviewHelp();ShowPreviewHelp();
        Ui.Label(storage,UiText.T("문맥 대기"));var pace=Ui.Choice(new[]{"fast","balanced","context"},current.CaptionPace);storage.Children.Add(pace);
        var summary=Page(UiText.T("요약"));var enabled=new CheckBox{Content=UiText.T("요약 실행"),IsChecked=current.SummaryEnabled};summary.Children.Add(enabled);var open=new CheckBox{Content=UiText.T("요약 완료 후 폴더 열기"),IsChecked=current.OpenFolderAfterSummary};summary.Children.Add(open);
        Ui.Label(summary,UiText.T("요약 Provider"));summaryProvider=Ui.Choice(new[]{UiText.T("선택한 Agent"),"OpenAI API"},current.ApiSummary?"OpenAI API":UiText.T("선택한 Agent"));
        summaryKey=ApiKeyButton(()=>{_=openApiKey();});ProviderRow(summary,summaryProvider,summaryKey);
        summary.Children.Add(new TextBlock{Text=UiText.T("API 요약은 연결 탭과 같은 키를 사용합니다. 후속 작업은 선택한 Agent가 실행합니다."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
        summaryAgentSetup=Ui.Button(UiText.T("Agent 설정"),()=>openAgentSettings());summary.Children.Add(summaryAgentSetup);
        Ui.Label(summary,UiText.T("요약 언어"));var summaryLanguage=Ui.Choice(Languages.All,Languages.Target(current.SummaryLanguage));summary.Children.Add(summaryLanguage);
        Ui.Label(summary,UiText.T("요약 형식"));var format=Ui.Choice(new[]{"md","txt"},current.SummaryFormat);summary.Children.Add(format);
        Ui.Label(summary,UiText.T("카테고리"));var categories=new System.Collections.ObjectModel.ObservableCollection<SummaryCategory>(value.SummaryCategories);var category=Ui.Choice(categories,categories.FirstOrDefault(c=>c.Id==current.SummaryCategory)??categories.FirstOrDefault());summary.Children.Add(category);
        var name=Ui.Input("");var prompt=Ui.Input("",true);summary.Children.Add(name);summary.Children.Add(prompt);bool editing=false;
        void Select(){editing=true;name.Text=(category.SelectedItem as SummaryCategory)?.Name??"";prompt.Text=(category.SelectedItem as SummaryCategory)?.Prompt??"";editing=false;}
        category.SelectionChanged+=(_,_)=>Select();name.TextChanged+=(_,_)=>{if(!editing&&category.SelectedItem is SummaryCategory c)c.Name=name.Text??"";};prompt.TextChanged+=(_,_)=>{if(!editing&&category.SelectedItem is SummaryCategory c)c.Prompt=prompt.Text??"";};Select();
        summary.Children.Add(Ui.Button(UiText.T("카테고리 추가"),()=>{var c=new SummaryCategory("custom-"+Guid.NewGuid().ToString("N"),UiText.T("새 카테고리"),UiText.T("내용을 주제별로 정리하세요."));categories.Add(c);category.SelectedItem=c;}));
        summary.Children.Add(Ui.Button(UiText.T("선택 카테고리 삭제"),()=>{if(category.SelectedItem is SummaryCategory c){if(value.SummaryCategory==c.Id){value.SummaryCategory="none";enabled.IsChecked=false;}categories.Remove(c);category.SelectedItem=categories.FirstOrDefault();}}));
        var instructions=Page(UiText.T("지침"));Ui.Label(instructions,UiText.T("Custom Instruction"));instructions.Children.Add(new TextBlock{Text=UiText.T("번역 방식·말투·용어 규칙을 설정합니다. 다음 녹음부터 적용합니다."),TextWrapping=TextWrapping.Wrap});var instruction=Ui.Input(current.CustomInstruction,true);instruction.MaxLength=8000;instructions.Children.Add(instruction);
        Ui.Label(instructions,UiText.T("중지 후 후속 작업"));instructions.Children.Add(new TextBlock{Text=UiText.T("요약 완료 후 같은 Agent가 실행합니다. Skill·MCP와 Agent 권한 정책을 따릅니다. 비우면 실행하지 않습니다."),TextWrapping=TextWrapping.Wrap});var workflow=Ui.Input(current.PostStopPrompt,true);workflow.MaxLength=8000;instructions.Children.Add(workflow);
        instructionsAgentSetup=Ui.Button(UiText.T("Agent 설정"),()=>openAgentSettings());instructions.Children.Add(instructionsAgentSetup);
        var appearance=Page(UiText.T("화면·키"));Ui.Label(appearance,UiText.T("테마"));var theme=Ui.Choice(new[]{"system","dark","light"},current.Theme);appearance.Children.Add(theme);
        Ui.Label(appearance,UiText.T("클릭 후 키 조합 입력 · Backspace/Delete로 해제 · 앱 포커스 필요"));var keys=new Dictionary<string,TextBox>();foreach(var action in ShortcutKeys.Actions)
        {
            Ui.Label(appearance,action.Name);var raw=current.Shortcuts.GetValueOrDefault(action.Id,"");var input=Ui.Input(ShortcutKeys.Display(raw,OperatingSystem.IsMacOS()));input.Tag=raw;input.IsReadOnly=true;keys[action.Id]=input;appearance.Children.Add(input);
            input.KeyDown+=(_,e)=>{if(e.Key==Key.Tab)return;e.Handled=true;int flags=(e.KeyModifiers.HasFlag(KeyModifiers.Control)?1:0)|(e.KeyModifiers.HasFlag(KeyModifiers.Alt)?2:0)|(e.KeyModifiers.HasFlag(KeyModifiers.Shift)?4:0)|(e.KeyModifiers.HasFlag(KeyModifiers.Meta)?8:0);try{var captured=ShortcutKeys.Capture(flags,e.Key.ToString(),OperatingSystem.IsMacOS());input.Tag=captured;input.Text=ShortcutKeys.Display(captured,OperatingSystem.IsMacOS());}catch(ArgumentException){}};
        }
        tabs.ItemsSource=pages;
        if(openLanguageTab)tabs.SelectedIndex=0;
        bool Save()
        {
            try
            {
                value.AgentProvider=agent.SelectedItem as string=="Claude"?"claude":"chatgpt";value.CodexPath=codex.Text??"";value.ClaudePath=claude.Text??"";
                value.TranslationProvider=translationProvider.SelectedItem as string=="OpenAI API"?"openai":"agent";
                value.SummaryProvider=summaryProvider.SelectedItem as string=="OpenAI API"?"openai":"agent";
                SaveOptions();
                value.SpeechEngine=engine.SelectedItem as string=="OpenAI API"?"openai":"whisper";value.LocalModel=(model.SelectedItem as LocalModel)?.Id??"balanced";value.LocalCpuOnly=cpu.IsChecked==true;
                if(apiKeyLoaded||!string.IsNullOrEmpty(apiKeyDraft))value.SetKey(apiKeyDraft);value.CustomInstruction=instruction.Text??"";value.PostStopPrompt=workflow.Text??"";
                value.SourceLanguage=(source.SelectedItem as LanguageOption)?.Code??"auto";value.TargetLanguage=(target.SelectedItem as LanguageOption)?.Code??"ko";value.Terms=terms.Text??"";
                value.UiLanguage=uiLanguage.SelectedItem as string=="한국어"?"ko":"en";
                if(!Path.IsPathFullyQualified(folder.Text??""))throw new IOException(UiText.T("저장 폴더의 전체 경로를 입력하세요."));value.OutputFolder=folder.Text!;value.DeviceId=(device.SelectedItem as AudioDevice)?.Id;
                value.RecordAudio=wav.IsChecked==true;value.StreamingPreview=streaming.IsChecked==true;value.CaptionPace=pace.SelectedItem as string??"balanced";
                value.SummaryEnabled=enabled.IsChecked==true;value.OpenFolderAfterSummary=open.IsChecked==true;value.SummaryLanguage=(summaryLanguage.SelectedItem as LanguageOption)?.Code??"ko";value.SummaryCategories=categories.ToList();value.SummaryFormat=format.SelectedItem as string??"md";
                value.Theme=theme.SelectedItem as string??"system";value.Shortcuts=keys.ToDictionary(k=>k.Key,k=>k.Value.Tag as string??"");value.Save();status.Text=UiText.T("자동 저장됨");return true;
            }
            catch(Exception ex){status.Text=UiText.Status(ex.Message);return false;}
        }
        var saveGate=new SettingsSaveGate();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(700)};
        void Schedule(){if(!IsVisible||keyDialogOpen)return;timer.Stop();timer.Start();}
        bool HasApiKey()=>apiKeyLoaded&&!string.IsNullOrWhiteSpace(apiKeyDraft);
        async Task<bool> EditApiKey()
        {
            if(!IsVisible)return false;
            apiKeySaveFailed=false;keyDialogOpen=true;timer.Stop();
            try
            {
                var dialog=new DesktopApiKeyDialog(apiKeyDraft,apiKeyReadError);
                await dialog.ShowDialog<bool?>(this);
                if(dialog.SavedKey==null)return false;
                if(!Save()){apiKeySaveFailed=true;return false;}
                var previousKey=apiKeyDraft;var previousLoaded=apiKeyLoaded;var previousReadError=apiKeyReadError;var previousProtectedKey=value.ProtectedKey;
                apiKeyDraft=dialog.SavedKey;apiKeyLoaded=true;apiKeyReadError=false;
                try
                {
                    value.SetKey(apiKeyDraft);
                    if(!OperatingSystem.IsMacOS())value.Save();
                }
                catch(Exception ex)
                {
                    apiKeyDraft=previousKey;apiKeyLoaded=previousLoaded;apiKeyReadError=previousReadError;value.ProtectedKey=previousProtectedKey;
                    apiKeySaveFailed=true;status.Text=UiText.Status(ex.Message);return false;
                }
                return true;
            }
            finally{keyDialogOpen=false;}
        }
        async Task<string> ConfirmApiSelection(ComboBox provider,string previous)
        {
            var selected=provider.SelectedItem as string??previous;
            if(selected=="OpenAI API"&&!await EditApiKey()&&(!HasApiKey()||apiKeySaveFailed))
            {
                revertingProvider=true;provider.SelectedItem=previous;revertingProvider=false;return previous;
            }
            return selected;
        }
        openApiKey=EditApiKey;checkAgent=Check;
        void UpdateAgentSetupVisibility()
        {
            agentSettings.IsVisible=translationProvider.SelectedItem as string==UiText.T("Agent")||agentSettingsOverride;
            bool agentHidden=!agentSettings.IsVisible;
            summaryAgentSetup.IsVisible=agentHidden&&summaryProvider.SelectedItem as string!="OpenAI API";
            instructionsAgentSetup.IsVisible=agentHidden;
        }
        void ShowAgentSettings()
        {
            agentSettingsOverride=true;UpdateAgentSetupVisibility();tabs.SelectedItem=connectionTab;_=Check();
        }
        openAgentSettings=ShowAgentSettings;
        saveBeforeUpdate=Save;
        var lastTranslationProvider=current.TranslationProvider=="openai"?"OpenAI API":UiText.T("Agent");
        var lastSpeechProvider=current.SpeechEngine=="openai"?"OpenAI API":UiText.T("로컬 Whisper");
        RefreshSpeechOptions();
        translationProvider.SelectionChanged+=async(_,_)=>
        {
            if(revertingProvider)return;
            lastTranslationProvider=await ConfirmApiSelection(translationProvider,lastTranslationProvider);
            agentSettingsOverride=false;UpdateAgentSetupVisibility();Schedule();
            if(agentSettings.IsVisible)await Check();
        };
        engine.SelectionChanged+=async(_,_)=>
        {
            if(revertingProvider)return;
            lastSpeechProvider=await ConfirmApiSelection(engine,lastSpeechProvider);
            RefreshSpeechOptions();Schedule();
        };
        var lastSummaryProvider=current.ApiSummary?"OpenAI API":UiText.T("선택한 Agent");
        summaryProvider.SelectionChanged+=async(_,_)=>
        {
            if(revertingProvider)return;
            lastSummaryProvider=await ConfirmApiSelection(summaryProvider,lastSummaryProvider);
            UpdateAgentSetupVisibility();Schedule();
        };
        agent.SelectionChanged+=async(_,_)=>{SaveOptions();optionProvider=agent.SelectedItem as string=="Claude"?"claude":"chatgpt";RefreshOptions();await Check();};
        modelChoice.SelectionChanged+=async(_,_)=>{if(loadingOptions)return;var efforts=AgentModels.Efforts(optionProvider,(modelChoice.SelectedItem as AgentModel)?.Id??"");loadingOptions=true;effortChoice.ItemsSource=efforts;effortChoice.SelectedItem=efforts.FirstOrDefault(e=>e.Id=="low")??efforts.FirstOrDefault();loadingOptions=false;SaveOptions();await Check();};
        effortChoice.SelectionChanged+=async(_,_)=>{if(!loadingOptions){SaveOptions();await Check();}};
        fast.IsCheckedChanged+=async(_,_)=>{if(!loadingOptions){SaveOptions();await Check();}};
        UpdateAgentSetupVisibility();
        Opened+=async(_,_)=>{if(agentSettings.IsVisible)await Check();};
        uiLanguage.SelectionChanged+=(_,_)=>{if(!IsVisible||((uiLanguage.SelectedItem as string=="한국어")==(current.UiLanguage=="ko")))return;if(Save()){LanguageChanged=true;Close();}};
        bool DropdownOpen()=>root.GetVisualDescendants().OfType<ComboBox>().Any(c=>c.IsDropDownOpen);
        bool TextFocused()=>root.GetVisualDescendants().OfType<TextBox>().Any(c=>c.IsKeyboardFocusWithin);
        timer.Tick+=(_,_)=>{if(keyDialogOpen){timer.Stop();return;}if(!SettingsSaveGate.CanRunTimer(TextFocused(),DropdownOpen()))return;timer.Stop();if(Save())saveGate.Saved();};
        AddHandler(TextBox.TextChangedEvent,(_,e)=>{if(!IsVisible||e.Source is not TextBox input)return;if(saveGate.TextChanged(input.IsKeyboardFocusWithin))Schedule();else timer.Stop();});
        AddHandler(InputElement.LostFocusEvent,(_,e)=>{if(!IsVisible||!saveGate.HasTextEdits||e.Source is not TextBox)return;timer.Stop();Dispatcher.UIThread.Post(()=>{if(!IsVisible||!saveGate.HasTextEdits)return;if(DropdownOpen()){Schedule();return;}if(Save())saveGate.Saved();});});
        AddHandler(SelectingItemsControl.SelectionChangedEvent,(_,_)=>Schedule());
        foreach(var checkbox in new ToggleButton[]{cpu,wav,streaming,enabled,open,fast})checkbox.IsCheckedChanged+=(_,_)=>Schedule();
        Closing+=(_,e)=>{timer.Stop();if(keyDialogOpen){e.Cancel=true;return;}if(IsVisible&&!Save())e.Cancel=true;};
    }
}






