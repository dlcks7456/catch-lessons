using System.Text.Json;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace CatchLessons;

internal static class QualityTests
{
    internal static async Task<string> RunAsync()
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        var channel = Channel.CreateUnbounded<Caption>();
        channel.Writer.TryWrite(new(1, "a", 0, 2, "Cool, so now you can do R dot"));
        channel.Writer.TryWrite(new(2, "b", 2, 3, "Severity."));
        channel.Writer.TryWrite(new(3, "c", 10, 11, "Next."));
        channel.Writer.TryComplete();
        var groups = new List<Caption>();
        await foreach (var group in CaptionAssembler.ReadAsync(channel.Reader, CancellationToken.None)) groups.Add(group);
        Check(groups.Count == 2, "Join adjacent incomplete fragments, not a separate utterance");
        Check(groups[0].Original.EndsWith("R dot Severity."), "Keep every source fragment");
        Check(groups[0].Start == 0 && groups[0].End == 3 && groups[0].ItemId == "a+b", "Merged source timeline");
        Check(groups[1].Original == "Next.", "Flush the final short fragment on stop");
        Check(CaptionAssembler.NeedsContinuation("Less sort of"), "Incomplete phrase must wait");
        Check(!CaptionAssembler.NeedsContinuation("You should compare the score and confidence before trusting this answer."), "Complete sentence must not wait");
        Check(new Settings().Segmentation == (1.2, 12), "Balanced defaults");
        Check(new Settings { CaptionPace = "context" }.Segmentation == (1.6, 20), "Context preset");

        // Detached WPF controls: no real window or mouse injection.
        var scroll = new ScrollViewer();
        var follow = new CaptionScroll(scroll);
        follow.Resume(); // Deliberately queue a follow before the user's upward wheel event.
        scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        follow.ContentChanged();
        await scroll.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(!follow.Following, "Queued rendering updates must not cancel reading mode");
        follow.Resume();
        Check(follow.Following, "Latest button resumes following");
        return JsonSerializer.Serialize(new { passed = checks, cloudRequests = 0, realWindowsOpened = 0 });
    }
}
