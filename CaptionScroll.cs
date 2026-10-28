using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace CatchLessons;

// Content updates follow the tail only until the reader scrolls away.
internal sealed class CaptionScroll
{
    private readonly ScrollViewer view;
    public bool Following { get; private set; } = true;
    public event Action<bool>? Changed;
    public CaptionScroll(ScrollViewer view)
    {
        this.view = view;
        view.PreviewMouseWheel += (_, e) => { if (e.Delta > 0) SetFollowing(false); };
        view.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Up or Key.PageUp or Key.Home) SetFollowing(false);
        };
        view.ScrollChanged += (_, e) =>
        {
            if (e.OriginalSource != view || e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0) return;
            if (e.VerticalChange < 0) SetFollowing(false);
            else if (e.VerticalChange > 0 && IsAtBottom(view.VerticalOffset, view.ScrollableHeight)) SetFollowing(true);
        };
    }
    internal static bool IsAtBottom(double offset, double scrollableHeight) => scrollableHeight - offset <= 2;
    private void SetFollowing(bool value)
    {
        if (Following == value) return;
        Following = value;
        Changed?.Invoke(value);
    }
    public void Resume()
    {
        SetFollowing(true);
        ContentChanged();
    }
    public void ContentChanged()
    {
        if (!Following) return;
        view.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (Following) view.ScrollToEnd();
        }));
    }
}
