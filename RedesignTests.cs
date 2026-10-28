using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CatchLessons;

internal static class RedesignTests
{
    public static Task<string> RunAsync()
    {
        int checks = 0;
        void Check(bool good, string why) { if (!good) throw new Exception(why); checks++; }
        var defaults = JsonSerializer.Deserialize<Settings>("{}")!;
        Check(defaults.SourceLanguage == "auto" && defaults.TargetLanguage == "ko", "Default/legacy language migration");
        Check(defaults.LocalModel == "balanced", "Local inference default");
        Check(LocalTranscriber.NativeLanguage("ja") == "ja" && LocalTranscriber.NativeLanguage("jv") == "jw", "Whisper language mapping");
        var dropdown=new ComboBox{Style=(Style)Application.Current.FindResource(typeof(ComboBox)),ItemsSource=new[]{"one","two"},SelectedIndex=0};dropdown.Measure(new Size(240,36));dropdown.Arrange(new Rect(0,0,240,36));dropdown.ApplyTemplate();
        var toggle=(System.Windows.Controls.Primitives.ToggleButton)dropdown.Template.FindName("Toggle",dropdown);
        Check(!SettingsWindow.IsSettingInput(toggle),"Dropdown internal toggle never schedules settings autosave");
        Check(SettingsWindow.IsSettingInput(dropdown)&&SettingsWindow.IsSettingInput(new CheckBox())&&SettingsWindow.IsSettingInput(new TextBox()),"Actual selections, toggles and text still autosave");
        Check(!SettingsWindow.IsSettingInput(new TabControl()),"Tab navigation is not a settings edit");
        Check(!SettingsWindow.HasOpenDropdown(dropdown),"Closed dropdown does not defer pending save");
        if(!UpdateService.RepositoryConfigured)
        {
            var korean=new SettingsWindow(new Settings{UiLanguage="ko"});var shell=(Grid)((Border)korean.Content).Child;var pages=shell.Children.OfType<TabControl>().Single();
            var general=(StackPanel)((ScrollViewer)((TabItem)pages.Items[0]).Content).Content;
            Check(general.Children.OfType<TextBlock>().Any(t=>t.Text=="업데이트 저장소가 설정되지 않았습니다."),"Missing repository message is shown in Korean");korean.Close();UiText.Language="en";
        }
        var fastToggle=new System.Windows.Controls.Primitives.ToggleButton{Style=(Style)Application.Current.FindResource("SummarySwitch")};
        fastToggle.Measure(new Size(36,36));fastToggle.Arrange(new Rect(0,0,36,36));fastToggle.ApplyTemplate();
        var trackBorder=(Border)fastToggle.Template.FindName("Track",fastToggle);
        var checkedTrigger=fastToggle.Template.Triggers.OfType<Trigger>().First(t=>t.Property==System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty);
        Check(trackBorder.BorderThickness==new Thickness(0)&&checkedTrigger.EnterActions.Count>0&&checkedTrigger.ExitActions.Count>0&&fastToggle.Template.Triggers.OfType<Trigger>().Any(t=>t.Property==UIElement.IsKeyboardFocusedProperty),"Switch has no focus border, retains keyboard cue and animates both states");
        fastToggle.IsChecked=true;fastToggle.IsChecked=false;Check(trackBorder.BorderThickness==new Thickness(0),"Toggling does not introduce a focus border");
        var folder = Path.Combine(Environment.CurrentDirectory, "artifacts", "redesign-tests"); Directory.CreateDirectory(folder);
        using (var store = new SessionStore(folder, "ja"))
        {
            store.Save(new(1, "japanese", 0, 1, "Hello", "こんにちは", "done"));
            Check(File.Exists(Path.Combine(store.Folder, "txt", "translation.txt")) && !File.Exists(Path.Combine(store.Folder, "translation.ko.txt")), "Target-language exports");
        }
        var translator = new CodexTranslator(Environment.ProcessPath!, folder, targetLanguage: "ja");
        Check(translator.TranslationPrompt([], [new(1, "a", 0, 1, "Bonjour")]).Contains("Japanese"), "Translation prompt target");
        Check(SummaryNotes.Prompt(SummaryNotes.Resolve("lecture"), "Bonjour", false, "fr").Contains("Output language: French"), "Summary prompt target");
        Check(!CaptionAssembler.NeedsContinuation("これは音声認識の動作を確認するための文章です。"), "Japanese sentence boundary");
        Check(Languages.Direction("مرحبا بالعالم") == CaptionDirection.RightToLeft && Languages.Direction("こんにちは") == CaptionDirection.LeftToRight, "Caption writing direction");
        foreach (string mode in new[] { "dark", "light" })
        {
            var window = new MainWindow(); ThemeManager.Apply(mode); window.SetPreview();
            window.HeaderControls.Visibility = window.StatusControls.Visibility = window.FooterControls.Visibility = Visibility.Visible;
            Check(window.Icon != null && window.StartButton.Tag is Geometry, "Registered application icon and library glyph");
            Check(Contrast("Text", "Surface") >= 4.5 && Contrast("Muted", "Raised") >= 4.5 && Contrast("AccentInk", "Accent") >= 4.5, "Theme text/button contrast");
            Render((FrameworkElement)window.Content, 920, 460, $"artifacts/redesign-{mode}.png");
            var track=(System.Windows.Controls.Primitives.Track)window.FontSizeSlider.Template.FindName("PART_Track",window.FontSizeSlider);
            Check(Equals(track.Thumb.ToolTip,window.FontSizeSlider.ToolTip)&&Equals(track.IncreaseRepeatButton.ToolTip,window.FontSizeSlider.ToolTip),"Slider hover surfaces expose tooltip");
            Check(Texts((FrameworkElement)window.Content).Any(t => t == Languages.Auto.Label), "Language picker renders a label, not a record object");
            Render((FrameworkElement)window.Content, 480, 280, $"artifacts/redesign-{mode}-compact.png");
            var content=(FrameworkElement)window.Content;
            var hoverHeight=window.HistoryScroll.ActualHeight;
            foreach(var controls in new[]{window.HeaderControls,window.StatusControls,window.FooterControls})controls.ClearValue(UIElement.VisibilityProperty);
            Render(content,480,280,$"artifacts/redesign-{mode}-captions-only.png");
            Check(window.HeaderControls.Visibility==Visibility.Collapsed&&window.StatusControls.Visibility==Visibility.Collapsed&&window.FooterControls.Visibility==Visibility.Collapsed,"Non-hover menus collapse their layout space");
            Check(window.HistoryScroll.ActualHeight>hoverHeight+80&&window.HistoryScroll.TranslatePoint(new Point(),content).Y<25,"Captions reclaim top and bottom menu space");
            window.HeaderControls.Visibility=window.StatusControls.Visibility=window.FooterControls.Visibility=Visibility.Visible;
            content.Measure(new Size(480,280));content.Arrange(new Rect(0,0,480,280));content.UpdateLayout();
            Check(Math.Abs(window.HistoryScroll.ActualHeight-hoverHeight)<1,"Hover layout restores without accumulating space");
            window.Close();
            var settings = new SettingsWindow(new Settings { Theme = mode }); ThemeManager.Apply(mode);
            var layout = (Grid)((Border)settings.Content).Child;
            var tabs = layout.Children.OfType<TabControl>().Single();
            var general=(StackPanel)((ScrollViewer)((TabItem)tabs.Items[0]).Content).Content;
            Check(!general.Children.OfType<TextBox>().Any(),"General settings do not expose a repository input");
            if(!UpdateService.RepositoryConfigured)Check(general.Children.OfType<TextBlock>().Any(t=>t.Text==UiText.T("업데이트 저장소가 설정되지 않았습니다."))&&general.Children.OfType<Button>().Any(b=>Equals(b.Content,UiText.T("업데이트 확인"))&&!b.IsEnabled),"Missing build repository is shown and update check is disabled");
            for (int index = 0; index < tabs.Items.Count; index++)
            {
                tabs.SelectedIndex = index;
                Render((FrameworkElement)settings.Content, 700, settings.Height, $"artifacts/settings-{mode}-{index}.png");
            }
            settings.Close();
        }
        return Task.FromResult(JsonSerializer.Serialize(new { passed = checks, cloudRequests = 0, liveUiActions = 0, renderedThemes = 2 }));
    }
    private static double Contrast(string a, string b)
    {
        double L(string key)
        {
            var c = ((SolidColorBrush)Application.Current.FindResource(key)).Color;
            double V(byte x) { double v = x / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * V(c.R) + .7152 * V(c.G) + .0722 * V(c.B);
        }
        return (Math.Max(L(a), L(b)) + .05) / (Math.Min(L(a), L(b)) + .05);
    }
    private static IEnumerable<string> Texts(DependencyObject root)
    {
        if (root is TextBlock text) yield return text.Text;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var value in Texts(VisualTreeHelper.GetChild(root, i))) yield return value;
    }
    private static void Render(FrameworkElement content, double width, double height, string path)
    {
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }
}



