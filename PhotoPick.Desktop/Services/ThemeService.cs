using System;
using System.Windows;
using System.Windows.Media;

namespace PhotoPick.Desktop.Services;

public enum AppTheme
{
    Dark,
    Light
}

public static class ThemeService
{
    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;
    public static event Action<AppTheme>? ThemeChanged;

    public static void SetTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        var app = Application.Current;
        if (app == null) return;

        if (theme == AppTheme.Dark)
        {
            app.Resources["BgWorkspace"] = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));
            app.Resources["BgPanel"] = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
            app.Resources["BgCard"] = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18));
            app.Resources["BgCardHover"] = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));
            app.Resources["BorderSubtle"] = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
            app.Resources["BorderFocus"] = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D));
            app.Resources["TextPrimary"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            app.Resources["TextNormal"] = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4));
            app.Resources["TextMuted"] = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            app.Resources["ScrollThumb"] = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
            app.Resources["ScrollThumbHover"] = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A));
        }
        else
        {
            app.Resources["BgWorkspace"] = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF3));
            app.Resources["BgPanel"] = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
            app.Resources["BgCard"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            app.Resources["BgCardHover"] = new SolidColorBrush(Color.FromRgb(0xE5, 0xE5, 0xEA));
            app.Resources["BorderSubtle"] = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xE2));
            app.Resources["BorderFocus"] = new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xC4));
            app.Resources["TextPrimary"] = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));
            app.Resources["TextNormal"] = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x24));
            app.Resources["TextMuted"] = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x72));
            app.Resources["ScrollThumb"] = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB8));
            app.Resources["ScrollThumbHover"] = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x98));
        }

        ThemeChanged?.Invoke(CurrentTheme);
    }

    public static void ToggleTheme()
    {
        SetTheme(CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
    }
}
