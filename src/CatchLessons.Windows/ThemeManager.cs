using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace CatchLessons;

internal static class ThemeManager
{
    public static void Apply(string preference)
    {
        bool light = preference == "light";
        if (preference == "system")
        {
            try { light = (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) == 1; }
            catch { light = false; }
        }
        var colors = light
            ? new[] { "#F5F7F7", "#E8EEEE", "#D9E4E2", "#172321", "#4C6260", "#768D88", "#14796A", "#F5FAF9" }
            : new[] { "#181D21", "#242B30", "#323C42", "#F1F5F4", "#AFBFBE", "#607570", "#55C7B5", "#102D27" };
        var keys = new[] { "Surface", "Raised", "Hover", "Text", "Muted", "Line", "Accent", "AccentInk" };
        for (int i = 0; i < keys.Length; i++)
            Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
    }
}
