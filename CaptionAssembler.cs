using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace CatchLessons;

internal static class CaptionAssembler
{
    internal static bool NeedsContinuation(string text) =>
        (text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < 9 &&
         !(text.Length >= 18 && Regex.IsMatch(text, @"[\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}]"))) ||
        !Regex.IsMatch(text.TrimEnd(), "[.!?。！？؟।॥][\"')」』]*$") ||
        Regex.IsMatch(text.TrimEnd(), @"\b(and|or|but|because|the|a|of|to|with|dot)[.!?]*$", RegexOptions.IgnoreCase);

    internal static Caption Join(Caption first, Caption next) => first with
    {
        ItemId = first.ItemId + "+" + next.ItemId,
        End = next.End,
        Original = first.Original.TrimEnd() + " " + next.Original.TrimStart()
    };

    internal static async IAsyncEnumerable<Caption> ReadAsync(ChannelReader<Caption> reader,
        [EnumeratorCancellation] CancellationToken token)
    {
        while (await reader.WaitToReadAsync(token))
        {
            if (!reader.TryRead(out var caption)) continue;
            var clock = Stopwatch.StartNew();
            int count = 1;
            while (NeedsContinuation(caption.Original) && count < 4)
            {
                if (!reader.TryPeek(out var next))
                {
                    var remaining = TimeSpan.FromSeconds(2) - clock.Elapsed;
                    if (remaining <= TimeSpan.Zero) break;
                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
                    wait.CancelAfter(remaining);
                    bool available;
                    try { available = await reader.WaitToReadAsync(wait.Token); }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested) { break; }
                    if (!available || !reader.TryPeek(out next)) break;
                }
                if (next.Start - caption.End > 3 || next.End - caption.Start > 26 ||
                    caption.Original.Length + next.Original.Length > 1800) break;
                reader.TryRead(out _);
                caption = Join(caption, next);
                count++;
            }
            yield return caption;
        }
    }
}
