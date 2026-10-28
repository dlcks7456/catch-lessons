using System.Text.Json;

namespace CatchLessons;

internal static class SummaryTests
{
    internal static async Task<string> RunAsync()
    {
        int passed = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; }
        Check(new Settings().SummaryCategory == "lecture", "Default category");
        Check(SummaryNotes.Resolve("unknown").Id == "lecture", "Unknown category fallback");
        Check(SummaryNotes.Categories.Select(c => c.Prompt).Distinct().Count() == 5, "Category-specific instructions");
        var original = new string('a', 23999) + "😀\n" + new string('b', 25000) + "\nLAST SOURCE SENTENCE";
        var chunks = SummaryNotes.Split(original);
        Check(string.Concat(chunks) == original && chunks.All(c => c.Length <= 24000), "No transcript truncation");
        Check(chunks.All(c => !char.IsHighSurrogate(c[^1]) && !char.IsLowSurrogate(c[0])), "Do not split surrogate pairs");
        var folder = Path.Combine(Environment.CurrentDirectory, "artifacts", "summary-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "original.txt"), original);
        await File.WriteAllTextAsync(Path.Combine(folder, "translation.ko.txt"), "기존 번역 유지");
        var received = new List<string>();
        Task<string> Fake(string prompt, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var json = JsonDocument.Parse(prompt[(prompt.LastIndexOf('\n') + 1)..]);
            received.Add(json.RootElement.GetProperty("source").GetString()!);
            return Task.FromResult("# 테스트 노트\n\n## 핵심 내용\n- 테스트 결과입니다.");
        }
        var path = await SummaryNotes.CreateAsync(folder, "lecture", Fake, _ => { }, CancellationToken.None, "마지막 전사 미확인");
        Check(string.Concat(received.Take(chunks.Count)) == original, "Every source chunk reaches the generator");
        var note = await File.ReadAllTextAsync(path);
        Check(!note.StartsWith("---") && !note.Contains("category:") && !note.Contains("original.txt") &&
              !note.Contains("translation.ko.txt") && !note.Contains("## 원본 자료") &&
              note.Contains("# 테스트 노트") && note.Contains("처리 상태:"), "Note body without frontmatter or file links; preserve warnings");
        var retry = await SummaryNotes.CreateAsync(folder, "lecture", Fake, _ => { }, CancellationToken.None);
        Check(retry != path && await File.ReadAllTextAsync(path) == note, "Never overwrite an existing note");
        int filesBefore = Directory.GetFiles(folder, "*.md").Length;
        try
        {
            await SummaryNotes.CreateAsync(folder, "meeting", (_, _) => throw new IOException("fake failure"), _ => { }, CancellationToken.None);
            throw new Exception("Expected failure");
        }
        catch (IOException) { }
        Check(Directory.GetFiles(folder, "*.md").Length == filesBefore, "No final note on failure");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            await SummaryNotes.CreateAsync(folder, "meeting", Fake, _ => { }, canceled.Token);
            throw new Exception("Expected cancellation");
        }
        catch (OperationCanceledException) { }
        Check(await File.ReadAllTextAsync(Path.Combine(folder, "original.txt")) == original &&
              await File.ReadAllTextAsync(Path.Combine(folder, "translation.ko.txt")) == "기존 번역 유지", "Failure/cancellation preserve originals");
        return JsonSerializer.Serialize(new { passed, cloudRequests = 0, folder });
    }
}
