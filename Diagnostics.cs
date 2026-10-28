using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CatchLessons;

public static class Diagnostics
{
    [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="SendMessageW")]
    private static extern IntPtr SendHotkeyMessage(IntPtr window,uint message,IntPtr id,IntPtr data);
    public static async Task<string> RunAsync(string mode)
    {
        if (mode == "--protocol-test" || mode == "--local-test") return await LocalTests.RunAsync();
        if (mode == "--local-smoke") return await LocalTests.SmokeAsync();
        if (mode == "--streaming-smoke") return await LocalTests.SmokeAsync(true);
        if (mode == "--quiet-streaming-smoke") return await LocalTests.SmokeAsync(true,.01f);
        if (mode == "--noise-smoke") return await LocalTests.NoiseSmokeAsync();
        if(mode=="--install-mode-test")
        {
            var enabled=LocalModels.WhisperEnabled;var settings=Settings.Load();
            if(!enabled&&settings.SpeechEngine!="openai")throw new Exception("API-only installation did not select the API engine");
            bool modelExists=File.Exists(Path.Combine(LocalModels.Folder,LocalModels.Resolve("balanced").File));
            if(enabled&&!modelExists)throw new Exception("Whisper installation did not include the default model");
            return JsonSerializer.Serialize(new{passed=true,whisperEnabled=enabled,modelExists,apiOnly=!enabled&&settings.SpeechEngine=="openai"});
        }
        if(mode=="--update-config-test")return JsonSerializer.Serialize(new{repository=UpdateService.BuildRepository,normalized=UpdateService.NormalizeRepository(UpdateService.BuildRepository),configured=UpdateService.RepositoryConfigured});
        if (mode == "--pause-smoke") return await LocalTests.PauseSmokeAsync();
        if (mode == "--audio-pipeline-test")
        {
            using var local=JsonDocument.Parse(await LocalTests.SmokeAsync(false,.01f));
            var transcript=local.RootElement.GetProperty("transcript").GetString()!;
            var folder=Path.Combine(Environment.CurrentDirectory,"artifacts","audio-pipeline-test",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            var agent=CodexTranslator.Create(new Settings(),Path.Combine(Settings.Root,"agent-work"));using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await agent.StartSessionAsync(folder,timeout.Token);var id=agent.SessionId;var translation=await agent.TranslateAsync(transcript,[],timeout.Token);
            if(!translation.Any(c=>c>='가'&&c<='힣')||agent.SessionId!=id)throw new Exception("Korean translation or recording conversation missing");
            return JsonSerializer.Serialize(new{passed=true,quietSample=true,sourceRecognized=true,koreanTranslation=true,provider=agent.Provider,model=agent.Model,sessionId=id,liveAudio=false});
        }
        if (mode == "--streaming-ui-test")
        {
            var window = new MainWindow();
            window.ApplyPreview(new("Current phrase", 14));
            window.ApplyCaption(new(1, "old", 0, 12, "Previous phrase"));
            if (window.PartialText.Text != "Current phrase") throw new Exception("Older final must not erase newer preview");
            window.ApplyPreview(new("Stale draft", 10));
            if (window.PartialText.Text != "Current phrase") throw new Exception("Reject stale preview");
            window.ApplyCaption(new(2, "current", 12, 24, "Current phrase, completed."));
            window.ApplyPreview(new("Late draft", 14));
            if (window.PartialText.Text.Length != 0 || window.History.Items.Count != 2) throw new Exception("Confirmed preview must not reappear or duplicate history");
            window.ResetCaptionDisplay();
            window.ApplyPreview(new("New session", 2));
            if (window.PartialText.Text != "New session") throw new Exception("Reset preview timeline");
            window.Close();
            return """{"passed":4,"cloudRequests":0,"realWindowsOpened":0}""";
        }
        if (mode == "--design-test") return await RedesignTests.RunAsync();
        if(mode=="--warning-render")
        {
            var window=new MainWindow();UiText.Language="en";WpfLocalizer.Apply(window);window.HeaderControls.Visibility=window.StatusControls.Visibility=window.FooterControls.Visibility=Visibility.Visible;
            window.ShowAgentWarnings(["Claude"]);
            var content=(FrameworkElement)window.Content;content.Measure(new Size(920,460));content.Arrange(new Rect(0,0,920,460));content.UpdateLayout();
            var bitmap=new RenderTargetBitmap(920,460,96,96,PixelFormats.Pbgra32);bitmap.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory("artifacts");using(var output=File.Create("artifacts/agent-connection-warning.png"))encoder.Save(output);
            if(window.AgentWarning.Visibility!=Visibility.Visible||!window.AgentWarningText.Text.Contains("Claude"))throw new Exception("Connection warning missing");
            UiText.Language="ko";WpfLocalizer.Apply(window);
            var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            typeof(MainWindow).GetField("loadingMode",flags)!.SetValue(window,true);typeof(MainWindow).GetField("loadingLanguages",flags)!.SetValue(window,true);typeof(MainWindow).GetField("loadingCategory",flags)!.SetValue(window,true);
            window.ModePicker.ItemsSource=new[]{new{Id=false,Name=UiText.T("번역 모드")},new{Id=true,Name=UiText.T("녹취 모드")}};window.ModePicker.SelectedValue=false;window.SourcePicker.SelectedIndex=-1;window.TargetPicker.SelectedIndex=-1;window.SummaryPicker.SelectedIndex=-1;window.SourcePicker.ItemsSource=Languages.Sources.Select(l=>new LanguageOption(l.Code,l.Name)).ToArray();window.SourcePicker.SelectedValue="auto";window.TargetPicker.ItemsSource=Languages.All.Select(l=>new LanguageOption(l.Code,l.Name)).ToArray();window.TargetPicker.SelectedValue="ko";window.SummaryPicker.ItemsSource=new[]{SummaryNotes.None}.Concat(SummaryNotes.Categories).Select(c=>c with{}).ToArray();window.SummaryPicker.SelectedValue="lecture";window.DetectionText.Text=UiText.T("자동 감지 대기");
            await Application.Current.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);
            window.ShowAgentWarnings(["Claude"]);content.UpdateLayout();var koBitmap=new RenderTargetBitmap(920,460,96,96,PixelFormats.Pbgra32);koBitmap.Render(content);var koEncoder=new PngBitmapEncoder();koEncoder.Frames.Add(BitmapFrame.Create(koBitmap));using(var output=File.Create("artifacts/agent-connection-warning-ko.png"))koEncoder.Save(output);
            if(!window.AgentWarningText.Text.Contains("설정"))throw new Exception("Korean connection warning missing");
            if((window.SourcePicker.SelectedItem as LanguageOption)?.Label!="언어 자동 감지"||(window.TargetPicker.SelectedItem as LanguageOption)?.Label!="한국어 (ko)"||(window.SummaryPicker.SelectedItem as SummaryCategory)?.DisplayName!="교육/강의 요약 정리")throw new Exception("Korean picker labels did not update");
            window.Close();return "{\"passed\":true,\"simulatedAgentFailure\":true,\"liveUiActions\":0}";
        }
        if(mode=="--settings-language-render")
        {
            var window=new SettingsWindow(new Settings{UiLanguage="ko",Theme="dark"},true);ThemeManager.Apply("dark");
            var content=(FrameworkElement)window.Content;content.Measure(new Size(700,window.Height));content.Arrange(new Rect(0,0,700,window.Height));content.UpdateLayout();
            var bitmap=new RenderTargetBitmap(700,(int)window.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory("artifacts");using(var output=File.Create("artifacts/settings-language-ko.png"))encoder.Save(output);
            if(window.Title!="CatchLessons 설정"||UiText.Language!="ko")throw new Exception("Korean settings language missing");window.Close();return "{\"passed\":true,\"liveUiActions\":0}";
        }
        if (mode == "--quality-test") return await QualityTests.RunAsync();
        if (mode == "--summary-test") return await SummaryTests.RunAsync();
        if (mode == "--feature-test") return await FeatureTests.RunAsync();
        if(mode=="--hotkey-test")
        {
            var chord=new Dictionary<string,string>{{"startStop","Ctrl+Alt+Shift+F23"}};string? received=null;
            using var first=new WindowsHotkeys(action=>received=action);
            if(first.Bind(chord) is string failed)throw new Exception(failed);
            using(var conflict=new WindowsHotkeys(_=>{}))if(conflict.Bind(chord)==null)throw new Exception("OS hotkey conflict was not detected");
            // Dispatch the OS message directly; do not send keystrokes to the user's applications.
            SendHotkeyMessage(first.Handle,0x312,new IntPtr(1),IntPtr.Zero);
            var frame=new System.Windows.Threading.DispatcherFrame();_ = Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));System.Windows.Threading.Dispatcher.PushFrame(frame);
            if(received!="startStop")throw new Exception("Background hotkey action was not delivered");
            first.Clear();using var next=new WindowsHotkeys(_=>{});if(next.Bind(chord)!=null)throw new Exception("Hotkey was not released");
            return "{\"passed\":true,\"osRegistration\":true,\"conflictDetection\":true,\"backgroundDispatch\":true,\"release\":true,\"physicalKeystrokes\":false}";
        }
        if (mode is "--agent-session-test" or "--claude-session-test" or "--fast-session-test")
        {
            var folder=Path.Combine(Environment.CurrentDirectory,"artifacts","agent-session-test",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            var agent=CodexTranslator.Create(new Settings {AgentProvider=mode=="--claude-session-test"?"claude":"chatgpt",CodexFastMode=mode=="--fast-session-test",CustomInstruction="Use polite Korean subtitles. Preserve product names."},Path.Combine(Settings.Root,"agent-work"));
            using var token=new CancellationTokenSource(TimeSpan.FromMinutes(4));
            await agent.StartSessionAsync(folder,token.Token); var id=agent.SessionId;
            await agent.TranslateAsync("The project is called Silver Pine.",[],token.Token);
            var second=await agent.TranslateAsync("It launches next Monday.",[],token.Token);
            if(id!=agent.SessionId)throw new Exception("Agent session changed between translation turns");
            var note=await agent.WriteNoteAsync("Summarize the project name and launch date mentioned earlier in this conversation as a short Korean Markdown note.",token.Token);
            if(!(note.Contains("Silver",StringComparison.OrdinalIgnoreCase)||note.Contains("실버")))throw new Exception("Session memory was not retained");
            return JsonSerializer.Serialize(new {passed=true,provider=agent.Provider,model=agent.Model,effort=agent.Effort,fastMode=agent.FastMode,sessionId=id,second,note});
        }
        if(mode=="--agent-connection-test")return JsonSerializer.Serialize(new{message=await CodexTranslator.CheckConnectionAsync(new Settings(),CancellationToken.None)});
        if(mode=="--agent-workflow-test")
        {
            var folder=Path.Combine(Environment.CurrentDirectory,"artifacts","workflow-test",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            var marker=Guid.NewGuid().ToString("N");await File.WriteAllTextAsync(Path.Combine(folder,"marker.txt"),marker);
            var agent=CodexTranslator.Create(new Settings(),folder);using var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await agent.StartSessionAsync(folder,cancel.Token);var id=agent.SessionId;
            var report=await agent.RunWorkflowAsync("Read only marker.txt in the recording folder and quote its complete content in the report. Do not modify any file, use external services, or perform any other action.",folder,cancel.Token);
            if(!report.Contains(marker)||agent.SessionId!=id)throw new Exception("Workflow did not read the local fixture in the same session");
            return JsonSerializer.Serialize(new{passed=true,sessionId=id,localReadOnlyFixture=true,externalMcpActions=0});
        }
        if (mode == "--processing-ui-test")
        {
            var window = new MainWindow();
            window.ShowProcessing("저장 중", "마지막 전사 저장", true, false);
            if (window.IsMouseOver || window.ProcessingPanel.Visibility != Visibility.Visible ||
                !window.ProcessingProgress.IsIndeterminate || window.ProcessingAction.IsEnabled)
                throw new Exception("Saving state must remain visible without hover, with cancellation disabled");
            window.ShowProcessing("요약 중", "단계 1, 2/3", true, true);
            if (!window.ProcessingAction.IsEnabled || window.ProcessingDetail.Text != "단계 1, 2/3")
                throw new Exception("Summary progress/cancel state");
            foreach (var title in new[] { "완료", "취소", "실패" })
            {
                window.ShowProcessing(title, "결과", false, false);
                if (window.ProcessingProgress.IsIndeterminate || window.ProcessingProgress.Visibility != Visibility.Collapsed ||
                    window.ProcessingPanel.Visibility != Visibility.Visible || !window.ProcessingAction.IsEnabled ||
                    (string)window.ProcessingAction.Content != UiText.T("확인")) throw new Exception("Terminal state must stop loading and remain readable");
            }
            window.ResetCaptionDisplay();
            if (window.ProcessingPanel.Visibility != Visibility.Collapsed) throw new Exception("New session must clear result card");
            window.Close();
            return """{"passed":true,"noHoverRequired":true,"savingSummaryTerminalReset":true,"cloudRequests":0,"realWindowsOpened":0}""";
        }
        if (mode == "--reset-test")
        {
            var window = new MainWindow();
            window.SetPreview();
            window.PartialText.Text = "previous partial";
            window.Meter.Value = .8;
            window.LatestButton.Visibility = Visibility.Visible;
            window.ResetCaptionDisplay();
            if (window.History.Items.Count != 0 || window.PartialText.Text.Length != 0 || window.Meter.Value != 0 ||
                window.LatestButton.Visibility != Visibility.Collapsed || window.EmptyText.ReadLocalValue(UIElement.VisibilityProperty) != DependencyProperty.UnsetValue)
                throw new Exception("Caption display did not reset");
            window.Close();
            var root = Path.Combine(Environment.CurrentDirectory, "artifacts", "reset-tests");
            using var first = new SessionStore(root);
            first.Save(new(1, "old", 0, 1, "Previous session"));
            using var next = new SessionStore(root);
            if (first.Folder == next.Folder || next.Read().Count != 0 || first.Read().Count != 1 ||
                File.ReadAllText(Path.Combine(next.Folder, "txt", "original.txt")).Length != 0)
                throw new Exception("New session did not get an independent empty store");
            return """{"passed":true,"displayReset":true,"newSessionStore":true,"previousSessionPreserved":true,"cloudRequests":0,"realWindowsOpened":0}""";
        }
        if (mode == "--drag-test")
        {
            var window = new MainWindow();
            window.HeaderControls.Visibility = Visibility.Visible;
            var content = (FrameworkElement)window.Content;
            foreach (var width in new[] { 760d, 920d })
            {
                content.Measure(new Size(width, 430));
                content.Arrange(new Rect(0, 0, width, 430));
                content.UpdateLayout();
                var handle = window.DragHandle;
                foreach (var point in new[] { new Point(2, 2), new Point(40, 18), new Point(handle.ActualWidth - 2, handle.ActualHeight / 2) })
                {
                    var hit = VisualTreeHelper.HitTest(content, handle.TranslatePoint(point, content))?.VisualHit;
                    while (hit != null && hit != handle) hit = VisualTreeHelper.GetParent(hit);
                    if (hit != handle) throw new Exception($"Drag region not hit-testable at width {width}, {point}");
                }
                var button = window.StartButton;
                var buttonHit = VisualTreeHelper.HitTest(content, button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), content))?.VisualHit;
                while (buttonHit != null && buttonHit != button && buttonHit != handle) buttonHit = VisualTreeHelper.GetParent(buttonHit);
                if (buttonHit != button) throw new Exception("Drag region overlaps Start button");
            }
            window.Close();
            return """{"passed":true,"widths":[760,920],"checks":8,"emptyHeaderDraggable":true,"buttonsExcluded":true}""";
        }
        if (mode == "--self-test")
        {
            void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
            Check(SessionStore.Timestamp(3661.234) == "01:01:01,234", "SRT hour formatting");
            var folder = Path.Combine(Environment.CurrentDirectory, "artifacts", "tests");
            using var store = new SessionStore(folder);
            store.Save(new(2, "later", 4, 7, "Second"));
            store.Save(new(1, "first", 1, 3, "First"));
            store.Save(new(1, "first", 1, 3, "First", "첫 번째", "done"));
            store.Flush();
            Check(store.Read().Count == 2, "Upsert must not duplicate");
            Check(store.Read()[0].Id == 1, "Out-of-order events must be sorted");
            Check(File.ReadAllText(Path.Combine(store.Folder, "txt", "translation.txt")).StartsWith("첫 번째"), "Korean UTF-8");
            Check(File.ReadAllText(Path.Combine(store.Folder, "srt", "original.srt")).Contains("00:00:01,000 --> 00:00:03,000"), "Source timestamps");
            Check(File.ReadAllText(Path.Combine(store.Folder, "txt", "translation.txt")).Contains("[번역 pending]"), "Pending translations must be visible");
            var settings = new Settings();
            Check(settings.LocalModel == "balanced", "Local model defaults without an API key");
            return JsonSerializer.Serialize(new { passed = 7, store.Folder });
        }
        if (mode == "--render")
        {
            var window = new MainWindow();
            window.SetPreview();
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(920, 430));
            content.Arrange(new Rect(0, 0, 920, 430));
            content.UpdateLayout();
            var image = new RenderTargetBitmap(920, 430, 96, 96, PixelFormats.Pbgra32);
            image.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            Directory.CreateDirectory("artifacts");
            using var output = File.Create("artifacts/ui-preview.png");
            encoder.Save(output);
            window.Close();
            return """{"rendered":"artifacts/ui-preview.png","sampleData":true}""";
        }
        if (mode == "--codex-test")
        {
            var translator = new CodexTranslator("", Path.Combine(Environment.CurrentDirectory, "artifacts", "codex-test"));
            var started = DateTime.UtcNow;
            var result = await translator.TranslateBatchAsync([
                new(7, "test7", 0, 3, "Please do not close the window."),
                new(9, "test9", 3, 6, "The meeting starts at three.")
            ], [], CancellationToken.None);
            if (result.Count != 2 || !result.ContainsKey(7) || !result.ContainsKey(9)) throw new Exception("Batch alignment");
            return JsonSerializer.Serialize(new { translations = result, elapsedSeconds = (DateTime.UtcNow - started).TotalSeconds });
        }
        if (mode is "--probe" or "--probe-tone" or "--record-test")
        {
            var devices = AudioCapture.Devices();
            using var capture = new AudioCapture();
            capture.Start(null);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            float peak = 0;
            int frames = 0, audible = 0;
            using var tone = mode is "--probe-tone" or "--record-test" ? new WasapiOut() : null;
            var recordingPath = Path.Combine(Environment.CurrentDirectory, "artifacts", "test-tone.wav");
            WaveFileWriter? writer = mode == "--record-test" ? new WaveFileWriter(recordingPath, AudioCapture.Format) : null;
            tone?.Init(new SignalGenerator(48000, 2) { Frequency = 440, Gain = .04, Type = SignalGeneratorType.Sin }.ToWaveProvider());
            tone?.Play();
            try
            {
                await capture.PumpAsync(frame =>
                {
                    frames++;
                    peak = Math.Max(peak, frame.Peak);
                    if (frame.Peak > .001) audible++;
                    writer?.Write(frame.Pcm, 0, frame.Pcm.Length);
                    return Task.CompletedTask;
                }, stop.Token);
            }
            catch (OperationCanceledException) { }
            finally { writer?.Dispose(); }
            tone?.Stop();
            if (mode == "--record-test")
            {
                using var reader = new WaveFileReader(recordingPath);
                if (reader.TotalTime.TotalSeconds < 3 || reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1)
                    throw new Exception("WAV recording format/duration mismatch");
            }
            return JsonSerializer.Serialize(new { devices, frames, audibleFrames = audible, peak, passed = frames > 0 && audible > 0, cloudAudioSent = false, recorded = mode == "--record-test" });
        }
        throw new ArgumentException("Unknown diagnostic option");
    }
}


