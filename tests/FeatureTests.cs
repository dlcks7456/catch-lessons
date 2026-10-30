using System.Text.Json;

namespace CatchLessons;

internal static class FeatureTests
{
    private sealed class UpdateResponse:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("test-update"))});
    }
    public static async Task<string> RunAsync()
    {
        int passed=0;void Check(bool result,string message){if(!result)throw new Exception(message);passed++;}
        var settings=new Settings();settings.SummaryCategory="none";Check(!settings.ShouldSummarize,"None category disables summary");
        const string release="""{"tag_name":"v1.2.0","assets":[{"name":"CatchLessons-Windows-x64.exe","browser_download_url":"https://github.com/acme/caption/releases/download/v1.2.0/CatchLessons-Windows-x64.exe"},{"name":"CatchLessons-macOS-arm64.pkg","browser_download_url":"https://github.com/acme/caption/releases/download/v1.2.0/CatchLessons-macOS-arm64.pkg"}]}""";
        Check(UpdateService.Parse("acme/caption",release,"1.0.0",false).Newer&&UpdateService.Parse("acme/caption",release,"1.0.0",true,"arm64").Download!.AbsolutePath.EndsWith("arm64.pkg"),"Version check selects a compatible release installer");
        Check(!UpdateService.Parse("acme/caption",release,"1.2.0",false).Newer&&UpdateService.Parse("acme/caption",release,"1.0.0",true,"x64").Download==null,"Up-to-date version and missing architecture are reported correctly");
        Check(UpdateService.NormalizeRepository("https://github.com/acme/caption/")=="acme/caption"&&UpdateService.NormalizeRepository("acme/caption")=="acme/caption"&&UpdateService.NormalizeRepository("https://github.com.evil.example/acme/caption")==null&&UpdateService.NormalizeRepository("http://github.com/acme/caption")==null,"Build setting accepts only a GitHub repository address");
        Check(UpdateService.NormalizeRepository("")==null,"Empty build repository remains unconfigured");
        var digest="sha256:"+new string('a',64);var withDigest=release.Replace("\"name\":\"CatchLessons-Windows-x64.exe\"", "\"digest\":\""+digest+"\",\"name\":\"CatchLessons-Windows-x64.exe\"");
        Check(UpdateService.Parse("acme/caption",withDigest,"1.0.0",false).Sha256==new string('a',64),"Release SHA-256 is extracted for update verification");
        bool missingDigest=false;try{await UpdateService.DownloadAsync(UpdateService.Parse("acme/caption",release,"1.0.0",false),new Progress<int?>(),CancellationToken.None);}catch(IOException){missingDigest=true;}Check(missingDigest,"Update without SHA-256 is rejected before download");
        var payload=System.Text.Encoding.UTF8.GetBytes("test-update");var payloadDigest=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload));
        var fixture=new UpdateResult("v1.2.0",new Uri("https://github.com/acme/caption/releases/download/v1.2.0/test.exe"),true,payloadDigest);
        var downloaded=await UpdateService.DownloadAsync(fixture,new Progress<int?>(),CancellationToken.None,new UpdateResponse());Check(File.ReadAllBytes(downloaded).SequenceEqual(payload),"Verified update is downloaded correctly");File.Delete(downloaded);
        bool invalidDigest=false;try{await UpdateService.DownloadAsync(fixture with{Sha256=new string('0',64)},new Progress<int?>(),CancellationToken.None,new UpdateResponse());}catch(IOException){invalidDigest=true;}Check(invalidDigest,"Corrupted update is rejected");
        Check(UpdateService.WindowsInstallerArguments(@"C:\Program Files\CatchLessons",false).Contains("/TASKS=!whisper")&&UpdateService.WindowsInstallerArguments(@"C:\Program Files\CatchLessons",true).Contains("/TASKS=whisper")&&UpdateService.WindowsInstallerArguments(@"C:\Program Files\CatchLessons",true).Contains(@"/DIR=C:\Program Files\CatchLessons"),"Update preserves installation location and speech engine choice");
        if(!UpdateService.RepositoryConfigured){bool unconfigured=false;try{await UpdateService.CheckAsync(false,CancellationToken.None);}catch(InvalidOperationException){unconfigured=true;}Check(unconfigured,"Missing build repository rejects update checks before network access");}
        var oldSettings=JsonSerializer.Deserialize<Settings>("{\"UpdateRepository\":\"untrusted/legacy-value\"}")!;
        Check(typeof(Settings).GetProperty("UpdateRepository")==null&&!JsonSerializer.Serialize(oldSettings).Contains("UpdateRepository"),"Legacy user settings cannot override the build repository");
        Check(!UpdateService.Parse("acme/caption",release.Replace("v1.2.0","v1.2"),"1.2.0",false).Newer,"Equivalent short version does not trigger an update");
        bool unsafeReleaseRejected=false;try{UpdateService.Parse("acme/caption",release.Replace("github.com/acme","evil.example/acme"),"1.0.0",false);}catch(IOException){unsafeReleaseRejected=true;}Check(unsafeReleaseRejected&&!UpdateService.ValidRepository("../outside"),"Update source and release URL validation");
        Check(settings.Shortcuts["pauseResume"]=="Primary+Alt+P","Pause shortcut default");
        Check(CodexSessionLink.Create("11111111-1111-4111-8111-111111111111")=="codex://threads/11111111-1111-4111-8111-111111111111","Codex link targets the recorded thread");
        bool invalidSessionLink=false;try{CodexSessionLink.Create("not-a-session?path=other");}catch(ArgumentException){invalidSessionLink=true;}Check(invalidSessionLink,"Invalid session links are rejected");
        var languageMarker=Path.Combine(Environment.CurrentDirectory,"artifacts","language-test-"+Guid.NewGuid().ToString("N"));
        Check(Settings.InitialUiLanguage(languageMarker)=="en","Missing installer language defaults to English");
        await File.WriteAllTextAsync(languageMarker,"ko");Check(Settings.InitialUiLanguage(languageMarker)=="ko","Korean installer language is used on first run");File.Delete(languageMarker);
        Check(JsonSerializer.Deserialize<Settings>("{\"UiLanguage\":\"en\"}")!.UiLanguage=="en","Existing language setting remains explicit");
        var documents=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);var oldFolder=Path.Combine(documents,"CaptionGlass");var newFolder=Path.Combine(documents,"CatchLessons");
        var migrated=new Settings{OutputFolder=oldFolder};migrated.MigrateOutputFolder();Check(migrated.OutputFolder==newFolder&&migrated.OutputFolderMigrated,"Legacy default output folder migrates");
        migrated.OutputFolder=oldFolder;migrated.MigrateOutputFolder();Check(migrated.OutputFolder==oldFolder,"A later explicit choice of the legacy folder is preserved");
        var custom=new Settings{OutputFolder=Path.Combine(documents,"Custom")};custom.MigrateOutputFolder();Check(custom.OutputFolder==Path.Combine(documents,"Custom"),"Custom output folders are preserved");
        var legacy=new Settings{Shortcuts=new(){["settings"]="Ctrl+Alt+P"}};legacy.EnsurePauseShortcut();Check(!legacy.Shortcuts.ContainsKey("pauseResume"),"Legacy shortcuts do not collide through modifier aliases");
        var pausedSession=new CaptionSession(new Settings());var audioQueue=(System.Threading.Channels.Channel<AudioFrame>)typeof(CaptionSession).GetField("audio",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(pausedSession)!;
        pausedSession.QueueCaptured(new(new byte[1600],0,.05,.1f));pausedSession.SetPaused(true);pausedSession.QueueCaptured(new(new byte[1600],.05,.10,.1f));pausedSession.SetPaused(false);pausedSession.QueueCaptured(new(new byte[1600],10,10.05,.1f));
        Check(audioQueue.Reader.TryRead(out var firstFrame)&&audioQueue.Reader.TryRead(out var flushFrame)&&audioQueue.Reader.TryRead(out var secondFrame)&&!audioQueue.Reader.TryRead(out _)&&firstFrame.Start==0&&flushFrame.Pcm.Length==0&&Math.Abs(secondFrame.Start-.05)<.0001,"Pause flushes buffered audio, drops only new audio and resumes a continuous recording timeline");await pausedSession.StopAsync();
        await using(var failedCloud=new CloudTranscriber())
        {
            var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;typeof(CloudTranscriber).GetField("pending",flags)!.SetValue(failedCloud,1);typeof(CloudTranscriber).GetField("failure",flags)!.SetValue(failedCloud,new IOException("simulated disconnect"));
            bool failedFast=false;try{await failedCloud.DrainAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));}catch(IOException){failedFast=true;}Check(failedFast,"A disconnected cloud stream does not wait for a response that cannot arrive");
        }
        Check(settings.UiLanguage=="en","English is the default app language");
        UiText.Language="en";Check(UiText.T("설정")=="Settings"&&Languages.Source("auto").Label=="Auto-detect","English UI and language labels");
        var userInput=new System.Windows.Controls.TextBox{Text="설정"};WpfLocalizer.Apply(userInput);Check(userInput.Text=="설정","UI localization does not change user-entered text");
        var previewText=new System.Windows.Controls.TextBlock{Name="PartialText",Text="설정"};WpfLocalizer.Apply(previewText);Check(previewText.Text=="설정","UI localization preserves transcript text");
        UiText.Language="ko";Check(UiText.T("설정")=="설정"&&Languages.Source("auto").Label=="언어 자동 감지","Korean UI and language labels");UiText.Language="en";
        settings.UiLanguage="ko";Check(JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!.UiLanguage=="ko","App language persists independently from translation language");settings.UiLanguage="en";
        Check(!ShortcutKeys.Actions.Any(a=>a.Id is "history" or "detach"),"Removed features no longer register shortcuts");
        var saveGate=new SettingsSaveGate();
        Check(!saveGate.TextChanged(true)&&saveGate.HasTextEdits&&!SettingsSaveGate.CanRunTimer(true,false),"Editing text waits for focus loss, including pending timer saves");
        Check(!SettingsSaveGate.CanRunTimer(false,true)&&SettingsSaveGate.CanRunTimer(false,false),"Dropdown defers scheduled saves until closed");
        Check(saveGate.HasTextEdits,"Pending text remains dirty after a failed save");saveGate.Saved();
        Check(!saveGate.HasTextEdits&&saveGate.TextChanged(false),"Successful save clears dirty state; programmatic changes still schedule save");
        Check(settings.CodexModel=="gpt-6-luna"&&settings.CodexEffort=="low"&&settings.ClaudeModel=="haiku"&&settings.ClaudeEffort=="low","Speed-first provider defaults");
        Check(!settings.CodexFastMode,"Fast mode starts off");
        var key=settings.AgentKey;settings.CodexEffort="high";Check(key!=settings.AgentKey,"Model options invalidate connection cache");settings.CodexEffort="low";
        Check(AgentModels.Efforts("chatgpt","gpt-6-luna").Any(e=>e.Id=="low"&&e.Label=="Light (low)"),"Light label maps to supported CLI low");
        var appearance=new Settings{CaptionFontSize=31,WindowOpacity=.63};
        var restored=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(appearance))!;
        Check(restored.CaptionFontSize==31&&restored.WindowOpacity==.63,"Appearance settings survive serialization");
        restored.CaptionFontSize=999;restored.WindowOpacity=-1;Check(restored.CaptionFontSize==36&&restored.WindowOpacity==.45,"Appearance values remain within slider limits");
        var activated=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var activationName="CatchLessons-test-"+Guid.NewGuid().ToString("N");
        using(var listener=new InstanceActivation(()=>activated.TrySetResult(),activationName)){await InstanceActivation.SignalAsync(activationName);await activated.Task.WaitAsync(TimeSpan.FromSeconds(3));Check(true,"Second-instance signal reaches existing instance");}
        settings.SummaryCategory="lecture";settings.SummaryEnabled=false;Check(!settings.ShouldSummarize,"Summary toggle disables summary");
        Check(SummaryNotes.CanRun(settings,true)&&!SummaryNotes.CanRun(settings,false),"Manual summary runs when stop-time summary is disabled");
        settings.SummaryEnabled=true;settings.SummaryCategories.Add(new("custom-test","테스트","반드시 TEST_CUSTOM_PROMPT 내용을 적용하세요."));settings.SummaryCategory="custom-test";
        var copy=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;copy.SummaryCategories[^1].Name="수정됨";
        Check(settings.SummaryCategories[^1].Name=="테스트"&&copy.SelectedSummary.Name=="수정됨","Independent editable category persistence");
        copy.SummaryCategories.RemoveAt(copy.SummaryCategories.Count-1);Check(!copy.ShouldSummarize,"Deleting selected category is safe");
        Check(ShortcutKeys.Parse("Primary+Alt+R",false)==(3,"R")&&ShortcutKeys.Parse("Primary+Alt+R",true)==(10,"R"),"Portable primary modifier");
        Check(ShortcutKeys.Capture(3,"R",false)=="Primary+Alt+R"&&ShortcutKeys.Capture(10,"R",true)=="Primary+Alt+R","Capture portable key chord");
        Check(ShortcutKeys.Display("Primary+Alt+R",false)=="Ctrl+Alt+R"&&ShortcutKeys.Display("Primary+Alt+R",true)=="⌘+Alt+R","Native shortcut label");
        Check(ShortcutKeys.Capture(0,"Back",false)==""&&ShortcutKeys.Capture(0,"Delete",true)=="","Clear shortcut");
        Check(WindowsHotkeys.NativeModifiers(3)==0x4003&&WindowsHotkeys.NativeModifiers(1)==0x4002&&WindowsHotkeys.NativeModifiers(2)==0x4001,"Native Ctrl/Alt flags and no-repeat");
        Check(CodexTranslator.ErrorDetail("{\"type\":\"turn.failed\",\"error\":{\"message\":\"network unavailable\"}}","").Contains("network unavailable"),"CLI failure reason retained");
        Check(CodexTranslator.ErrorDetail("{\"type\":\"result\",\"is_error\":true,\"result\":\"OAuth session expired\"}","")=="OAuth session expired","Claude authentication error remains actionable");
        var window=new MainWindow{BackgroundEnabled=true};var closing=new System.ComponentModel.CancelEventArgs();
        typeof(MainWindow).GetMethod("OnClosing",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance,null,[typeof(object),typeof(System.ComponentModel.CancelEventArgs)],null)!.Invoke(window,[window,closing]);
        Check(closing.Cancel,"Close-to-tray keeps session alive");window.BackgroundEnabled=false;window.Close();
        var connectionWindow=new MainWindow();typeof(MainWindow).GetField("settings",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(connectionWindow,new Settings{AgentProvider="chatgpt"});
        connectionWindow.AgentProbe=(s,_)=>s.AgentProvider=="claude"?Task.FromException<string>(new IOException("login required")):Task.FromResult("connected");
        await connectionWindow.CheckAgentConnectionAsync();
        Check(connectionWindow.AgentWarning.Visibility==System.Windows.Visibility.Collapsed&&connectionWindow.AgentConnectionText.Text.Contains("Claude")&&connectionWindow.StartButton.IsEnabled,"Nonselected agent failure is shown without a blocking warning");
        connectionWindow.AgentProbe=(s,_)=>Task.FromException<string>(new IOException("login required"));await connectionWindow.CheckAgentConnectionAsync();
        Check(connectionWindow.AgentWarning.Visibility==System.Windows.Visibility.Visible&&connectionWindow.AgentWarningText.Text.Contains("ChatGPT")&&!connectionWindow.StartButton.IsEnabled,"Selected agent failure is warned and prevents starting");
        connectionWindow.AgentProbe=(s,_)=>Task.FromResult("connected");await connectionWindow.CheckAgentConnectionAsync();Check(connectionWindow.AgentWarning.Visibility==System.Windows.Visibility.Collapsed&&!connectionWindow.AgentConnectionText.Text.Contains("failed"),"Warning and failure status clear after both agents connect");
        typeof(MainWindow).GetField("settings",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(connectionWindow,new Settings{AgentProvider="claude"});
        connectionWindow.AgentProbe=(s,_)=>s.AgentProvider=="chatgpt"?Task.FromException<string>(new IOException("login required")):Task.FromResult("connected");await connectionWindow.CheckAgentConnectionAsync();
        Check(connectionWindow.AgentWarning.Visibility==System.Windows.Visibility.Collapsed&&connectionWindow.AgentConnectionText.Text.Contains("ChatGPT")&&connectionWindow.StartButton.IsEnabled,"Claude can start when the unused ChatGPT agent fails");connectionWindow.Close();
        Check(SummaryNotes.FileTitle("# CON")!="CON"&&!SummaryNotes.FileTitle("# ../../a:b?*").Contains('/'),"Safe model-generated filenames");
        var folder=Path.Combine(Environment.CurrentDirectory,"artifacts","feature-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);await File.WriteAllTextAsync(Path.Combine(folder,"original.txt"),"Test source");
        var overflowStore=new SessionStore(folder);var overflowSession=new CaptionSession(new Settings());var overflowFlags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        typeof(CaptionSession).GetField("store",overflowFlags)!.SetValue(overflowSession,overflowStore);
        var rawQueue=(System.Threading.Channels.Channel<Caption>)typeof(CaptionSession).GetField("rawTranscripts",overflowFlags)!.GetValue(overflowSession)!;
        for(int i=1;i<=100;i++)rawQueue.Writer.TryWrite(new(i,"queued-"+i,i,i+1,"queued"));bool overflowReported=false;overflowSession.Failed+=_=>overflowReported=true;
        overflowSession.OnRawTranscript(new(101,"overflow",101,102,"Do not lose this text"));
        Check(overflowReported&&overflowStore.Read().Any(c=>c.Id==101&&c.Original=="Do not lose this text"),"Full transcription queue preserves the final recognized text");await overflowSession.StopAsync();
        await File.WriteAllTextAsync(Path.Combine(folder,LocalModels.Choices[0].File),"invalid");Check(!LocalModels.HasExistingModel(folder),"A truncated model is not treated as installed");File.Delete(Path.Combine(folder,LocalModels.Choices[0].File));
        Check(LocalModels.WhisperEnabledAt(folder),"Whisper enabled by default");await File.WriteAllTextAsync(Path.Combine(folder,"whisper-disabled.flag"),"");Check(!LocalModels.WhisperEnabledAt(folder),"API-only installation disables Whisper");
        var fake=Path.Combine(folder,"fake-cli.js");await File.WriteAllTextAsync(fake,"""
            const fs=require('fs');const args=process.argv.slice(2);fs.appendFileSync('calls.jsonl',JSON.stringify(args)+'\n');if(args.includes('--desktop')){console.log('Opening session '+args[args.indexOf('--resume')+1]+' in Claude Desktop');process.exit(0);}let input='';process.stdin.on('data',x=>input+=x);process.stdin.on('end',()=>{
            const id='11111111-1111-4111-8111-111111111111';let result={ok:true};
            if(input.includes('"current":')){const data=JSON.parse(input.substring(input.lastIndexOf('\n')+1));if(data.current.some(c=>c.text==='FAIL_FIRST')){console.log(JSON.stringify({type:'error',message:'simulated transient failure'}));process.exitCode=1;return;}result={translations:data.current.map(c=>({id:c.id,text:'테스트 번역'}))};}else if(input.includes('Output language:'))result={markdown:'# Test summary\n\nCaptured content.'};
            if(args[0]==='-p')console.log(JSON.stringify({session_id:id,is_error:false,structured_output:result}));else{fs.writeFileSync(args[args.indexOf('--output-last-message')+1],JSON.stringify(result));console.log(JSON.stringify({type:'thread.started',thread_id:id}));}});
            """);
        foreach(var provider in new[]{"chatgpt","claude"})
        {
            var fakeAgent=new CodexTranslator(fake,folder,provider:provider);await fakeAgent.StartSessionAsync(folder,CancellationToken.None);var id=fakeAgent.SessionId;await fakeAgent.TranslateAsync("hello",[],CancellationToken.None);
            var args=JsonSerializer.Deserialize<string[]>(File.ReadLines(Path.Combine(folder,"calls.jsonl")).Last())!;
            Check(args.Contains(provider=="claude"?"haiku":"gpt-6-luna")&&args.Contains(provider=="claude"?"low":"model_reasoning_effort=\"low\"")&&fakeAgent.SessionId==id,"Explicit provider model/effort survives resume: "+provider);
            Check(provider=="claude"?!args.Any(a=>a.StartsWith("service_tier=")):args.Contains("service_tier=\"default\""),"Fast off explicit and Claude isolated");
            if(provider=="claude")
            {
                await fakeAgent.OpenClaudeDesktopAsync(CancellationToken.None);
                var desktopArgs=JsonSerializer.Deserialize<string[]>(File.ReadLines(Path.Combine(folder,"calls.jsonl")).Last())!;
                Check(desktopArgs.SequenceEqual(new[]{"--desktop","--resume",id}),"Claude Desktop opens the same recorded session");
                var offlineSettings=new Settings{SummaryEnabled=false,OpenFolderAfterSummary=false};var offlineStore=new SessionStore(folder);offlineStore.Save(new(1,"claude",0,1,"Keep Claude caption"));
                var offlineSession=new CaptionSession(offlineSettings);var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                typeof(CaptionSession).GetField("store",flags)!.SetValue(offlineSession,offlineStore);typeof(CaptionSession).GetField("translator",flags)!.SetValue(offlineSession,fakeAgent);
                var offlineWindow=new MainWindow();typeof(MainWindow).GetField("settings",flags)!.SetValue(offlineWindow,offlineSettings);typeof(MainWindow).GetField("session",flags)!.SetValue(offlineWindow,offlineSession);
                offlineWindow.SessionIdText.Tag=id;offlineWindow.OpenAgentSessionButton.CommandParameter=fakeAgent;typeof(MainWindow).GetMethod("RefreshSessionButton",flags)!.Invoke(offlineWindow,null);
                Check(offlineWindow.OpenAgentSessionButton.Visibility==System.Windows.Visibility.Visible&&!offlineWindow.OpenAgentSessionButton.IsEnabled,"Claude session button is disabled while recording");
                await ((Task)typeof(MainWindow).GetMethod("StopAsync",flags)!.Invoke(offlineWindow,[true])!).WaitAsync(TimeSpan.FromSeconds(20));
                Check(offlineWindow.OpenAgentSessionButton.IsEnabled&&Equals(offlineWindow.OpenAgentSessionButton.CommandParameter,fakeAgent),"Claude session button is enabled after stop");offlineWindow.Close();
            }
        }
        var unavailableCli=Path.Combine(folder,"desktop-unavailable.js");await File.WriteAllTextAsync(unavailableCli,"console.error('unknown option --desktop');process.exit(1);");
        var unavailableAgent=new CodexTranslator(unavailableCli,folder,provider:"claude");typeof(CodexTranslator).GetProperty("SessionId")!.GetSetMethod(true)!.Invoke(unavailableAgent,["11111111-1111-4111-8111-111111111111"]);
        bool desktopError=false;try{await unavailableAgent.OpenClaudeDesktopAsync(CancellationToken.None);}catch(IOException ex){desktopError=ex.Message.Contains("2.1.285")&&ex.Message.Contains("unknown option");}Check(desktopError,"Claude Desktop failures include the version requirement and CLI error");
        var fastAgent=new CodexTranslator(fake,folder,fastMode:true);await fastAgent.StartSessionAsync(folder,CancellationToken.None);await fastAgent.TranslateAsync("fast test",[],CancellationToken.None);var fastArgs=JsonSerializer.Deserialize<string[]>(File.ReadLines(Path.Combine(folder,"calls.jsonl")).Last())!;Check(fastArgs.Contains("service_tier=\"fast\"")&&fastArgs.Contains("features.fast_mode=true"),"Fast on passed on resumed request");
        foreach(bool summarizeOnStop in new[]{false,true})
        {
            var stopSettings=new Settings{SummaryEnabled=summarizeOnStop,OpenFolderAfterSummary=false};
            var stopStore=new SessionStore(folder);var caption=new Caption(1,"stop-test",0,1,"Keep original","번역 유지","done");stopStore.Save(caption);
            var stopSession=new CaptionSession(stopSettings);var stopFlags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            typeof(CaptionSession).GetField("store",stopFlags)!.SetValue(stopSession,stopStore);typeof(CaptionSession).GetField("translator",stopFlags)!.SetValue(stopSession,fastAgent);
            var stopWindow=new MainWindow();typeof(MainWindow).GetField("settings",stopFlags)!.SetValue(stopWindow,stopSettings);typeof(MainWindow).GetField("session",stopFlags)!.SetValue(stopWindow,stopSession);
            stopWindow.ApplyCaption(caption);stopWindow.PartialText.Text="unfinished preview";stopWindow.SessionIdText.Tag=fastAgent.SessionId;
            _=stopWindow.Dispatcher.BeginInvoke(new Action(()=>{if(ReferenceEquals(typeof(MainWindow).GetField("session",stopFlags)!.GetValue(stopWindow),stopSession))stopWindow.ApplyCaption(caption with{Korean="최종 번역"});}));
            await ((Task)typeof(MainWindow).GetMethod("StopAsync",stopFlags)!.Invoke(stopWindow,[true])!).WaitAsync(TimeSpan.FromSeconds(20));
            Check(stopWindow.History.Items.Count==1&&((CaptionRow)stopWindow.History.Items[0]).Value.Korean=="최종 번역"&&stopWindow.PartialText.Text==""&&stopWindow.SummaryRetryButton.IsEnabled&&Equals(stopWindow.SessionIdText.Tag,fastAgent.SessionId),"Stopping retains final queued captions, session and manual summary: "+summarizeOnStop);
            Check(!summarizeOnStop||(bool)typeof(MainWindow).GetField("summarySaved",stopFlags)!.GetValue(stopWindow)!,"Automatic summary succeeds without clearing captions");
            typeof(MainWindow).GetMethod("RetrySummary",stopFlags)!.Invoke(stopWindow,[stopWindow,new System.Windows.RoutedEventArgs()]);
            for(int i=0;i<200&&(bool)typeof(MainWindow).GetField("busy",stopFlags)!.GetValue(stopWindow)!;i++)await Task.Delay(50);
            Check(!(bool)typeof(MainWindow).GetField("busy",stopFlags)!.GetValue(stopWindow)!&&(bool)typeof(MainWindow).GetField("summarySaved",stopFlags)!.GetValue(stopWindow)!&&stopWindow.History.Items.Count==1,"Manual summary after stopping preserves captions");
            stopWindow.ResetCaptionDisplay();Check(stopWindow.History.Items.Count==0&&stopWindow.SessionIdText.Tag==null,"New recording reset clears prior display");stopWindow.Close();
        }
        var closeStore=new SessionStore(folder);closeStore.Save(new(1,"close-test",0,1,"Preserve the recording"));
        var closeSession=new CaptionSession(new Settings());var privateFlags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        typeof(CaptionSession).GetField("store",privateFlags)!.SetValue(closeSession,closeStore);
        var closeWindow=new MainWindow();typeof(MainWindow).GetField("settings",privateFlags)!.SetValue(closeWindow,new Settings{SummaryEnabled=false,OpenFolderAfterSummary=false});
        typeof(MainWindow).GetField("session",privateFlags)!.SetValue(closeWindow,closeSession);typeof(MainWindow).GetField("lastAgent",privateFlags)!.SetValue(closeWindow,fastAgent);typeof(MainWindow).GetField("lastFolder",privateFlags)!.SetValue(closeWindow,closeStore.Folder);
        typeof(MainWindow).GetMethod("RetrySummary",privateFlags)!.Invoke(closeWindow,[closeWindow,new System.Windows.RoutedEventArgs()]);
        var closeArgs=new System.ComponentModel.CancelEventArgs();typeof(MainWindow).GetMethod("OnClosing",privateFlags,null,[typeof(object),typeof(System.ComponentModel.CancelEventArgs)],null)!.Invoke(closeWindow,[closeWindow,closeArgs]);
        for(int i=0;i<200&&typeof(MainWindow).GetField("session",privateFlags)!.GetValue(closeWindow)!=null;i++)await Task.Delay(50);
        Check(closeArgs.Cancel&&typeof(MainWindow).GetField("session",privateFlags)!.GetValue(closeWindow)==null&&File.ReadAllText(Path.Combine(closeStore.Folder,"txt","original.txt")).Contains("Preserve the recording"),"Closing during a live manual summary stops and saves the active recording");
        using(var queueStore=new SessionStore(folder))
        {
            var testSession=new CaptionSession(new Settings());var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            typeof(CaptionSession).GetField("store",flags)!.SetValue(testSession,queueStore);typeof(CaptionSession).GetField("translator",flags)!.SetValue(testSession,new CodexTranslator(fake,folder));
            var queue=(System.Threading.Channels.Channel<Caption>)typeof(CaptionSession).GetField("translations",flags)!.GetValue(testSession)!;
            var first=new TaskCompletionSource<Caption>(TaskCreationOptions.RunContinuationsAsynchronously);var next=new TaskCompletionSource<Caption>(TaskCreationOptions.RunContinuationsAsynchronously);testSession.Updated+=c=>{if(c.Id==1)first.TrySetResult(c);else next.TrySetResult(c);};
            int before=File.ReadAllLines(Path.Combine(folder,"calls.jsonl")).Length;
            var loop=(Task)typeof(CaptionSession).GetMethod("TranslateLoopAsync",flags)!.Invoke(testSession,null)!;
            await queue.Writer.WriteAsync(new(1,"fail",0,1,"FAIL_FIRST"));Check((await first.Task.WaitAsync(TimeSpan.FromSeconds(8))).State=="failed","Transient failure is saved");
            await queue.Writer.WriteAsync(new(2,"next",1,2,"next caption"));testSession.SetPaused(true);Check((await next.Task.WaitAsync(TimeSpan.FromSeconds(8))).State=="done"&&testSession.IsPaused,"Queued translation completes while recording is paused");queue.Writer.Complete();await loop;
            Check(File.ReadAllLines(Path.Combine(folder,"calls.jsonl")).Length-before==2,"No repeated live retries block newer captions");
            typeof(CaptionSession).GetField("store",flags)!.SetValue(testSession,null);await testSession.StopAsync();
        }
        var stub=Path.Combine(folder,"slow-cli.js");await File.WriteAllTextAsync(stub,"setTimeout(() => {}, 60000);");
        var slow=new CodexTranslator(stub,folder);
        var run=typeof(CodexTranslator).GetMethod("RunJsonAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        try{await (Task<JsonDocument>)run.Invoke(slow,["test","{}",TimeSpan.FromMilliseconds(200),CancellationToken.None,false,false,null])!;throw new Exception("Timeout not raised");}
        catch(IOException ex){Check(ex.Message.Contains("시간 초과"),"Timeout is retryable IOException with clear reason");}
        using(var canceled=new CancellationTokenSource()){canceled.Cancel();try{await (Task<JsonDocument>)run.Invoke(slow,["test","{}",TimeSpan.FromSeconds(1),canceled.Token,false,false,null])!;throw new Exception("Cancellation not raised");}catch(OperationCanceledException){Check(true,"User cancellation is not a retryable timeout");}}
        bool customized=false;
        var note=await SummaryNotes.CreateAsync(folder,"custom-test",(p,_)=>{customized=p.Contains("TEST_CUSTOM_PROMPT")&&p.Contains("plain text")&&p.Contains("Japanese");return Task.FromResult("녹음 내용에 맞는 제목\n본문");},_=>{},CancellationToken.None,null,"ja",settings.SelectedSummary,"txt");
        Check(customized&&Path.GetFileName(note)=="녹음 내용에 맞는 제목.ja.txt","Custom prompt/language/plain-text title");
        bool invoked=false;try{await SummaryNotes.CreateAsync(folder,"none",(_,_)=>{invoked=true;return Task.FromResult("bad");},_=>{},CancellationToken.None);}catch(InvalidOperationException){}
        Check(!invoked,"No summary request for None");
        using(var store=new SessionStore(folder,"ko",false))
        {
            store.Save(new(1,"only",0,1,"원문만",State:"transcribed"));store.Flush();
            Check(!File.Exists(Path.Combine(store.Folder,"txt","translation.txt"))&&new CaptionRow(store.Read()[0]).DisplayKorean=="","Transcription-only exports and display");
            Check(File.Exists(Path.Combine(store.Folder,"txt","original.txt"))&&File.Exists(Path.Combine(store.Folder,"srt","original.srt"))&&Directory.GetFiles(store.Folder).Length==0&&File.Exists(Path.Combine(store.InternalFolder,"session.db"))&&!File.Exists(Path.Combine(store.InternalFolder,"latency.jsonl"))&&!File.Exists(Path.Combine(store.InternalFolder,"transcription.raw.jsonl")),"Only requested output is created and recovery remains separate without raw/latency logs");
            var summary=await SummaryNotes.CreateAsync(store.Folder,"lecture",(_,_)=>Task.FromResult("# Recording\n\nSummary"),_=>{},CancellationToken.None);
            Check(File.Exists(summary)&&Directory.GetFiles(store.Folder).Length==1,"Summary reads the new transcript location without creating extra output files");
        }
        var watch=System.Diagnostics.Stopwatch.StartNew();string stressFolder;
        using(var store=new SessionStore(folder))
        {
            stressFolder=store.Folder;
            for(int i=1;i<=1000;i++)store.Save(new(i,"stress-"+i,i*12,i*12+10,"source "+i,"번역 "+i,"done"));
            store.Flush();Check(store.Read().Count==1000&&File.ReadAllLines(Path.Combine(store.Folder,"txt","translation.txt")).Length==1000,"Long-history export after throttled writes");
        }
        using(var reopened=SessionStore.OpenExisting(stressFolder))Check(reopened.Read().Count==1000,"Reopen stored history");
        return JsonSerializer.Serialize(new{passed,syntheticRows=1000,elapsedSeconds=watch.Elapsed.TotalSeconds,cloudRequests=0});
    }
}

