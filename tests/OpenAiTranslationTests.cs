using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace CatchLessons;

internal static class OpenAiTranslationTests
{
    private static string Delta(string text) => "event: response.output_text.delta\ndata: " + JsonSerializer.Serialize(new { type = "response.output_text.delta", delta = text }) + "\n\n";
    private const string Completed = "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\"}}\n\n";
    private sealed class Reply(Func<HttpResponseMessage> create) : HttpMessageHandler
    {
        internal readonly List<string> Bodies = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.ToString() != "https://api.openai.com/v1/responses" || request.Headers.Authorization?.ToString() != "Bearer test-key") throw new Exception("Request endpoint/authentication");
            Bodies.Add(await request.Content!.ReadAsStringAsync(token));
            return create();
        }
    }
    private sealed class SplitStream(string first, string last, Task release) : MemoryStream(Encoding.UTF8.GetBytes(first))
    {
        private MemoryStream? tail;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (Position < Length) return await base.ReadAsync(buffer, token);
            await release.WaitAsync(token);
            tail ??= new(Encoding.UTF8.GetBytes(last));
            return await tail.ReadAsync(buffer, token);
        }
        protected override void Dispose(bool disposing) { tail?.Dispose(); base.Dispose(disposing); }
    }
    private static HttpResponseMessage Response(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/event-stream") };

    internal static async Task<string> RunAsync()
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        var settings = JsonSerializer.Deserialize<Settings>("{}")!;
        Check(settings.TranslationProvider == "agent" && settings.Segmentation == (1.2, 12), "Legacy defaults remain unchanged");
        settings.TranslationProvider = "openai";
        Check(settings.ApiTranslation && settings.Segmentation == (1.2, 12), "API translation preserves Whisper audio context");
        settings.SpeechEngine="openai";
        Check(settings.Segmentation==(.6,3),"Streaming ASR uses shorter API translation segments");
        settings.SpeechEngine="whisper";
        settings.TranscriptionOnly = true;
        Check(!settings.ApiTranslation && settings.Segmentation == (1.2, 12), "Transcription-only segmentation remains unchanged");
        settings.TranscriptionOnly = false;
        settings.SetKey("test-key");
        var restored = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;
        restored.SpeechEngine = "openai";
        Check(restored.GetKey() == "test-key", "Speech recognition reuses the translation key");
        restored.SpeechEngine = "whisper"; restored.TranslationProvider = "agent";
        Check(restored.GetKey() == "test-key" && !JsonSerializer.Serialize(restored).Contains("test-key"), "Provider switches preserve encrypted credentials");
        bool missingKey = false;
        try { using var missing = new OpenAiTranslator(settings, "", null); } catch (IOException) { missingKey = true; }
        Check(missingKey, "Missing API key rejected before network access");
        var dialog = new SettingsWindow(settings);
        static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject root)
        {
            yield return root;
            foreach(var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
                foreach(var descendant in Descendants(child))yield return descendant;
        }
        var controls=Descendants(dialog).OfType<System.Windows.FrameworkElement>().ToArray();
        var provider=(System.Windows.Controls.ComboBox)controls.Single(c=>c.Name=="TranslationProvider");
        var engine=(System.Windows.Controls.ComboBox)controls.Single(c=>c.Name=="SpeechEngine");
        var keyPanel=controls.Single(c=>c.Name=="SharedApiKeyPanel");
        var keyInput=(System.Windows.Controls.PasswordBox)controls.Single(c=>c.Name=="SharedApiKey");
        Check(keyPanel.Visibility==System.Windows.Visibility.Visible&&keyInput.Password=="test-key", "API translation reveals the existing shared key with local speech");
        provider.SelectedValue="agent";
        Check(keyInput.Password=="test-key", "Switching away from API translation retains the key");
        engine.SelectedValue="openai";
        Check(keyPanel.Visibility==System.Windows.Visibility.Visible&&keyInput.Password=="test-key", "Selecting API speech reuses the translation key without re-entry");
        dialog.Close();

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preview = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var split = new Reply(() => new(HttpStatusCode.OK) { Content = new StreamContent(new SplitStream(Delta("안녕"), Delta("하세요") + Completed, release.Task)) });
        using (var api = new OpenAiTranslator(settings, "test-key", split))
        {
            var translation = api.TranslateAsync(new(1, "a", 0, 3, "Hello."), text => preview.TrySetResult(text), CancellationToken.None);
            Check(await preview.Task.WaitAsync(TimeSpan.FromSeconds(3)) == "안녕" && !translation.IsCompleted, "Translation preview appears before completion");
            release.SetResult();
            Check(await translation == "안녕하세요", "SSE deltas assemble into final translation");
            var body = JsonDocument.Parse(split.Bodies[0]);
            Check(body.RootElement.GetProperty("stream").GetBoolean() && !body.RootElement.GetProperty("store").GetBoolean() && body.RootElement.GetProperty("reasoning").GetProperty("effort").GetString() == "none", "Streaming uses no server history or reasoning delay");
        }
        settings.Topic = "Programming"; settings.Terms = "CatchLessons"; settings.CustomInstruction = "Use polite Korean.";
        var history = new Reply(() => Response(Delta("번역") + Completed));
        using (var api = new OpenAiTranslator(settings, "test-key", history))
        {
            for (int i = 1; i <= 10; i++) await api.TranslateAsync(new(i, i.ToString(), i, i + 1, "Source " + i), _ => { }, CancellationToken.None);
            using var request = JsonDocument.Parse(history.Bodies.Last());
            using var input = JsonDocument.Parse(request.RootElement.GetProperty("input").GetString()!);
            var context = input.RootElement.GetProperty("context");
            Check(context.GetArrayLength() == 8 && context[0].GetProperty("original").GetString() == "Source 2" && context[7].GetProperty("translated").GetString() == "번역", "Recent original/translation context is bounded and ordered");
            Check(input.RootElement.GetProperty("Topic").GetString() == "Programming" && request.RootElement.GetProperty("instructions").GetString()!.Contains("Use polite Korean."), "Topic and custom instruction reach API translation");
        }
        using (var fresh = new OpenAiTranslator(settings, "test-key", history = new Reply(() => Response(Delta("새 녹음") + Completed))))
        {
            await fresh.TranslateAsync(new(1,"new",0,1,"New recording"), _ => { }, CancellationToken.None);
            using var request = JsonDocument.Parse(history.Bodies[0]);using var input = JsonDocument.Parse(request.RootElement.GetProperty("input").GetString()!);
            Check(input.RootElement.GetProperty("context").GetArrayLength() == 0, "New recordings do not inherit another recording's context");
        }
        foreach (var invalid in new[] { Delta("unfinished"), Completed, Delta("partial") + "data: {\"type\":\"response.incomplete\"}\n\n", "data: {\"type\":\"error\"}\n\n", "data: {\"type\":\"response.refusal.delta\"}\n\n" })
        {
            using var api = new OpenAiTranslator(settings, "test-key", new Reply(() => Response(invalid)));
            bool rejected = false;
            try { await api.TranslateAsync(new(1,"a",0,1,"Source"), _ => { }, CancellationToken.None); } catch (IOException) { rejected = true; }
            Check(rejected, "Failed/empty/truncated streams cannot become finalized translations");
        }
        using (var api = new OpenAiTranslator(settings, "test-key", new Reply(() => new(HttpStatusCode.TooManyRequests))))
        {
            bool rejected = false;
            try { await api.CheckConnectionAsync(CancellationToken.None); } catch (IOException ex) { rejected = ex.Message.Contains("429") && !ex.Message.Contains("test-key"); }
            Check(rejected, "Quota failures are actionable and do not expose credentials");
        }
        using (var api = new OpenAiTranslator(settings, "test-key", new Reply(() => new(HttpStatusCode.OK) { Content = new StreamContent(new SplitStream(Delta("draft"), Completed, new TaskCompletionSource().Task)) })))
        {
            using var cancel = new CancellationTokenSource();
            bool canceled = false;
            try { await api.TranslateAsync(new(1,"a",0,1,"Source"), _ => cancel.Cancel(), cancel.Token); } catch (OperationCanceledException) { canceled = true; }
            Check(canceled, "Cancellation interrupts an in-progress translation");
        }
        var row = new CaptionRow(new(1,"a",0,1,"Source","임시 번역","translating"));
        Check(row.DisplayKorean == "임시 번역", "Windows displays streamed text");
        row.Update(row.Value with { Korean = "", State = "failed" });
        Check(!row.DisplayKorean.Contains("임시 번역"), "Failed previews are removed");

        var folder = Path.Combine(Environment.CurrentDirectory, "artifacts", "api-translation-test", Guid.NewGuid().ToString("N"));
        var store = new SessionStore(folder);
        var session = new CaptionSession(settings);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(CaptionSession).GetField("store", flags)!.SetValue(session, store);
        typeof(CaptionSession).GetField("apiTranslator", flags)!.SetValue(session, new OpenAiTranslator(settings, "test-key", new Reply(() => Response(Delta("초안")))));
        var raw = (Channel<Caption>)typeof(CaptionSession).GetField("rawTranscripts", flags)!.GetValue(session)!;
        raw.Writer.TryWrite(new(1,"a",0,1,"Keep original")); raw.Writer.TryComplete();
        var updates = new List<Caption>();session.Updated += updates.Add;
        await (Task)typeof(CaptionSession).GetMethod("AssembleAsync", flags)!.Invoke(session,null)!;
        await (Task)typeof(CaptionSession).GetMethod("TranslateLoopAsync", flags)!.Invoke(session,null)!;
        Check(updates.Any(c => c.State == "translating") && updates.Last().State == "failed" && updates.Last().Korean == "", "Pipeline replaces an interrupted preview with a failed result");
        Check(store.Read().Single().Original == "Keep original" && store.Read().Single().State == "failed", "Failed translation preserves original and state in storage");
        await session.StopAsync();
        return JsonSerializer.Serialize(new { passed = checks, cloudRequests = 0, transport = "mock SSE" });
    }
}
