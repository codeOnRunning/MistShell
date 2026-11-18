using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MistShell.Models;
using MistShell.Services;

namespace MistShell.Views;

public partial class SettingsWindow : Window
{
    private readonly Action<string>? _themePreview;
    private double _fontSize;
    private string _fontFamily = "JetBrains Mono";

    public SettingsWindow(AppSettings settings, Action<string>? themePreview = null)
    {
        InitializeComponent();
        _themePreview = themePreview;
        _fontSize = Math.Clamp(settings.TerminalFontSize, 10, 26);
        _fontFamily = settings.TerminalFontFamily;
        TerminalForegroundBox.Text = settings.TerminalForegroundColor;
        FontSizeText.Text = _fontSize.ToString("0");
        ScrollbackBox.Text = settings.ScrollbackLines.ToString();
        LowMemoryCheck.IsChecked = settings.LowMemoryMode;
        FollowDirectoryCheck.IsChecked = settings.FollowTerminalDirectory;
        DataPathText.Text = AppPaths.Root;
        BuildFontChoices();
        SelectLineHeight(settings.TerminalLineHeight);
        BuildThemeChoices(settings.Theme);
    }

    public string SelectedTheme { get; private set; } = "Mist";
    public string SelectedForeground { get; private set; } = "";
    public string SelectedFontFamily => _fontFamily;
    public double SelectedLineHeight { get; private set; } = 1.25;
    public double SelectedFontSize => _fontSize;
    public int SelectedScrollback { get; private set; } = 3000;
    public bool SelectedLowMemory { get; private set; }
    public bool SelectedFollowDirectory { get; private set; } = true;

    private void BuildThemeChoices(string selectedKey)
    {
        foreach (var palette in ThemePalettes.All)
        {
            var radio = new RadioButton
            {
                Content = palette.DisplayName + (palette.IsDark ? " · 深色" : " · 浅色"),
                Style = (Style)FindResource("ChoiceRadio"),
                GroupName = "Theme",
                Tag = palette.Key,
                MinWidth = 108,
                Margin = new Thickness(0, 0, 7, 7)
            };
            radio.Checked += (_, _) =>
            {
                SelectedTheme = palette.Key;
                _themePreview?.Invoke(palette.Key);
            };
            ThemePanel.Children.Add(radio);
            if (string.Equals(palette.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
                radio.IsChecked = true;
        }

        var system = new RadioButton
        {
            Content = "跟随系统",
            Style = (Style)FindResource("ChoiceRadio"),
            GroupName = "Theme",
            Tag = "System",
            MinWidth = 108,
            Margin = new Thickness(0, 0, 0, 7)
        };
        system.Checked += (_, _) =>
        {
            SelectedTheme = "System";
            _themePreview?.Invoke("System");
        };
        ThemePanel.Children.Add(system);
        if (string.Equals(selectedKey, "System", StringComparison.OrdinalIgnoreCase))
            system.IsChecked = true;

        if (ThemePanel.Children.OfType<RadioButton>().All(radio => radio.IsChecked != true))
            ((RadioButton)ThemePanel.Children[0]).IsChecked = true;
    }

    private void BuildFontChoices()
    {
        foreach (var font in TerminalFonts.All)
        {
            var radio = new RadioButton
            {
                Content = font.DisplayName,
                Style = (Style)FindResource("ChoiceRadio"),
                GroupName = "TerminalFont",
                Tag = font.Family,
                MinWidth = 128,
                Margin = new Thickness(0, 0, 7, 7)
            };
            radio.Checked += FontRadio_Checked;
            FontPanel.Children.Add(radio);
            if (string.Equals(font.Family, _fontFamily, StringComparison.OrdinalIgnoreCase))
                radio.IsChecked = true;
        }

        if (FontPanel.Children.OfType<RadioButton>().All(r => r.IsChecked != true))
            ((RadioButton)FontPanel.Children[0]).IsChecked = true;
        UpdateFontPreview();
    }

    private void SelectLineHeight(double lineHeight)
    {
        var radio = lineHeight >= 1.35 ? LineRelaxedRadio : lineHeight >= 1.18 ? LineNormalRadio : LineCompactRadio;
        radio.IsChecked = true;
        SelectedLineHeight = double.Parse((string)radio.Tag, CultureInfo.InvariantCulture);
    }

    private void FontRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string family })
        {
            _fontFamily = family;
            UpdateFontPreview();
        }
    }

    private void UpdateFontPreview()
    {
        if (FontPreviewText is null) return;
        try { FontPreviewText.FontFamily = new FontFamily(_fontFamily); }
        catch { FontPreviewText.FontFamily = new FontFamily("Consolas"); }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void FontMinus_Click(object sender, RoutedEventArgs e)
    {
        _fontSize = Math.Clamp(_fontSize - 1, 10, 26);
        FontSizeText.Text = _fontSize.ToString("0");
    }

    private void FontPlus_Click(object sender, RoutedEventArgs e)
    {
        _fontSize = Math.Clamp(_fontSize + 1, 10, 26);
        FontSizeText.Text = _fontSize.ToString("0");
    }

    private void TerminalForegroundBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (TerminalForegroundBox is null) return;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(TerminalForegroundBox.Text.Trim());
            TerminalForegroundBox.BorderBrush = new SolidColorBrush(color);
        }
        catch
        {
            TerminalForegroundBox.BorderBrush = (Brush)FindResource("BorderBrush");
        }
    }

    private void ClearForeground_Click(object sender, RoutedEventArgs e)
    {
        TerminalForegroundBox.Text = "";
    }

    private void Shortcuts_Click(object sender, RoutedEventArgs e)
    {
        new ShortcutHelpWindow { Owner = this }.ShowDialog();
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.Root) { UseShellExecute = true });
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ScrollbackBox.Text.Trim(), out var scrollback) || scrollback is < 500 or > 20000)
        {
            MessageBox.Show(this, "回滚行数必须在 500–20000 之间。", "MistShell", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var foreground = TerminalForegroundBox.Text.Trim();
        if (foreground.Length > 0)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(foreground);
                foreground = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }
            catch
            {
                MessageBox.Show(this, "终端文字颜色格式不正确，请输入 #RRGGBB。", "MistShell",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }
        SelectedForeground = foreground;
        // SelectedTheme is set by the checked theme radio button.
        SelectedScrollback = scrollback;
        SelectedLowMemory = LowMemoryCheck.IsChecked == true;
        SelectedFollowDirectory = FollowDirectoryCheck.IsChecked == true;
        if (LineCompactRadio.IsChecked == true) SelectedLineHeight = 1.1;
        else if (LineRelaxedRadio.IsChecked == true) SelectedLineHeight = 1.45;
        else SelectedLineHeight = 1.25;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
