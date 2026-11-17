using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using MistShell.Models;

namespace MistShell.Services;

public static class ThemeManager
{
    public static ThemePalette Current { get; private set; } = ThemePalettes.Mist;

    public static ThemePalette Resolve(string setting)
    {
        if (string.Equals(setting, "System", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var light = key?.GetValue("AppsUseLightTheme") as int? ?? 1;
                return light == 0 ? ThemePalettes.Slate : ThemePalettes.Mist;
            }
            catch
            {
                return ThemePalettes.Mist;
            }
        }
        return ThemePalettes.Get(setting);
    }

    public static void Apply(ThemePalette palette)
    {
        Current = palette;
        var resources = Application.Current.Resources;
        Set(resources, "WindowBackgroundBrush", palette.WindowBackground);
        Set(resources, "SurfaceBrush", palette.Surface);
        Set(resources, "SurfaceElevatedBrush", palette.SurfaceElevated);
        Set(resources, "SurfaceHoverBrush", palette.SurfaceHover);
        Set(resources, "SurfaceActiveBrush", palette.SurfaceActive);
        Set(resources, "SidebarBrush", palette.SidebarBackground);
        Set(resources, "TitleBarBrush", palette.TitleBar);
        Set(resources, "BorderBrush", palette.Border);
        Set(resources, "BorderStrongBrush", palette.BorderStrong);
        Set(resources, "TextPrimaryBrush", palette.TextPrimary);
        Set(resources, "TextSecondaryBrush", palette.TextSecondary);
        Set(resources, "TextMutedBrush", palette.TextMuted);
        Set(resources, "AccentBrush", palette.Accent);
        Set(resources, "AccentHoverBrush", palette.AccentHover);
        Set(resources, "AccentSoftBrush", palette.AccentSoft);
        Set(resources, "AccentTextBrush", palette.AccentText);
        Set(resources, "DangerBrush", palette.Danger);
        Set(resources, "DangerSoftBrush", palette.DangerSoft);
        Set(resources, "WarningBrush", palette.Warning);
        Set(resources, "SuccessBrush", palette.Success);
        Set(resources, "TerminalBackgroundBrush", palette.TerminalBackground);
        Set(resources, "TerminalForegroundBrush", palette.TerminalForeground);

        if (Application.Current.MainWindow is not null)
            Application.Current.MainWindow.Background = Brush(palette.WindowBackground);
    }

    public static Brush Brush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static void Set(ResourceDictionary resources, string key, string hex)
        => resources[key] = Brush(hex);

    public static object GetTerminalTheme(ThemePalette p) => new
    {
        background = p.TerminalBackground,
        foreground = p.TerminalForeground,
        cursor = p.TerminalCursor,
        cursorAccent = p.TerminalBackground,
        selectionBackground = p.TerminalSelection,
        black = p.TerminalBlack,
        red = p.TerminalRed,
        green = p.TerminalGreen,
        yellow = p.TerminalYellow,
        blue = p.TerminalBlue,
        magenta = p.TerminalMagenta,
        cyan = p.TerminalCyan,
        white = p.TerminalWhite,
        brightBlack = p.TextMuted,
        brightRed = p.Danger,
        brightGreen = p.Success,
        brightYellow = p.Warning,
        brightBlue = p.TerminalBlue,
        brightMagenta = p.TerminalMagenta,
        brightCyan = p.TerminalCyan,
        brightWhite = p.TextPrimary
    };
}
