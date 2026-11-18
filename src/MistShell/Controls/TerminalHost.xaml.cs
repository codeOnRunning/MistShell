using System.Collections.Concurrent;
using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MistShell.Models;
using MistShell.Services;

namespace MistShell.Controls;

public partial class TerminalHost : UserControl, IDisposable
{
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _initLock = new();
    private Task? _initTask;
    private bool _disposed;
    private ThemePalette _palette = ThemePalettes.Mist;
    private string _fontFamily = "JetBrains Mono";
    private double _fontSize = 14;
    private double _lineHeight = 1.25;
    private int _scrollback = 2000;
    private string? _customForeground;

    public TerminalHost()
    {
        InitializeComponent();
        AppPaths.EnsureCreated();
        WebView.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = AppPaths.WebViewDataFolder,
            AdditionalBrowserArguments =
                "--disable-background-networking --disable-component-update --disable-sync --no-first-run " +
                "--disable-extensions --disable-default-apps --mute-audio --disable-breakpad " +
                "--disable-gpu --in-process-gpu --disable-software-rasterizer --single-process " +
                "--disable-features=msWebOOUI,msPdfOOUI,msSmartScreenProtection,OptimizationHints " +
                "--js-flags=--max-old-space-size=64"
        };
        WebView.DefaultBackgroundColor = ColorTranslator.FromHtml(_palette.TerminalBackground);
    }

    public event Action<string, byte[]>? InputReceived;
    public event Action<string, int, int>? ResizeRequested;
    public event Action<string, string>? TitleChanged;
    public event Action<string, string>? CwdChanged;
    public event Action<string, string>? CommandLineSubmitted;
    public event Action<int>? FontSizeChanged;

    public Task EnsureInitializedAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TerminalHost));
        lock (_initLock)
        {
            return _initTask ??= InitializeCoreAsync();
        }
    }

    private async Task InitializeCoreAsync()
    {
        try
        {
            for (var i = 0; i < 80 && (!IsVisible || ActualWidth < 2 || ActualHeight < 2); i++)
                await Task.Delay(50);
            if (!IsVisible || ActualWidth < 2 || ActualHeight < 2)
                throw new InvalidOperationException("终端控件尚未显示。");

            AppPaths.EnsureCreated();
            var assets = AppPaths.TerminalAssets;
            if (!System.IO.File.Exists(System.IO.Path.Combine(assets, "index.html")))
                throw new FileNotFoundException("终端资源文件缺失。", System.IO.Path.Combine(assets, "index.html"));

            if (WebView.CoreWebView2 is null)
                await WebView.EnsureCoreWebView2Async();

            var core = WebView.CoreWebView2
                       ?? throw new InvalidOperationException("WebView2 控制器未初始化。");
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsWebMessageEnabled = true;
            core.WebMessageReceived += OnWebMessageReceived;
            core.SetVirtualHostNameToFolderMapping("mistshell.local", assets, CoreWebView2HostResourceAccessKind.Allow);
            core.Navigate("https://mistshell.local/index.html");

            await Task.WhenAny(_ready.Task, Task.Delay(TimeSpan.FromSeconds(12)));
            if (!_ready.Task.IsCompleted)
                throw new TimeoutException("终端页面未在预期时间内就绪。");

            LoadingOverlay.Visibility = Visibility.Collapsed;
            SendTheme();
            SendFontSettings();
        }
        catch
        {
            lock (_initLock) _initTask = null;
            throw;
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeElement)) return;
            var type = typeElement.GetString();
            switch (type)
            {
                case "ready":
                    _ready.TrySetResult(true);
                    while (_pending.TryDequeue(out var json))
                        WebView.CoreWebView2?.PostWebMessageAsJson(json);
                    break;
                case "input":
                    if (root.TryGetProperty("sessionId", out var sid) &&
                        root.TryGetProperty("data", out var data))
                    {
                        var text = data.GetString() ?? "";
                        InputReceived?.Invoke(sid.GetString() ?? "", Encoding.UTF8.GetBytes(text));
                    }
                    break;
                case "resize":
                    if (root.TryGetProperty("sessionId", out var rsid) &&
                        root.TryGetProperty("cols", out var cols) &&
                        root.TryGetProperty("rows", out var rows))
                    {
                        ResizeRequested?.Invoke(rsid.GetString() ?? "", cols.GetInt32(), rows.GetInt32());
                    }
                    break;
                case "title":
                    if (root.TryGetProperty("sessionId", out var tsid) &&
                        root.TryGetProperty("title", out var title))
                    {
                        TitleChanged?.Invoke(tsid.GetString() ?? "", title.GetString() ?? "");
                    }
                    break;
                case "cwd":
                    if (root.TryGetProperty("sessionId", out var csid) &&
                        root.TryGetProperty("path", out var cwd))
                    {
                        CwdChanged?.Invoke(csid.GetString() ?? "", cwd.GetString() ?? "");
                    }
                    break;
                case "commandLine":
                    if (root.TryGetProperty("sessionId", out var lsid) &&
                        root.TryGetProperty("line", out var line))
                    {
                        CommandLineSubmitted?.Invoke(lsid.GetString() ?? "", line.GetString() ?? "");
                    }
                    break;
                case "pasteRequest":
                    if (root.TryGetProperty("sessionId", out var psid) && Clipboard.ContainsText())
                    {
                        SendRaw(new { type = "paste", sessionId = psid.GetString() ?? "", text = Clipboard.GetText() });
                    }
                    break;
                case "fontSizeChanged":
                    if (root.TryGetProperty("fontSize", out var fontElement))
                    {
                        var size = Math.Clamp(fontElement.GetDouble(), 10, 26);
                        _fontSize = size;
                        FontSizeChanged?.Invoke((int)Math.Round(size));
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Invalid terminal message.", ex);
        }
    }

    public async Task CreateSessionAsync(string sessionId)
    {
        await EnsureInitializedAsync();
        SendRaw(new
        {
            type = "create",
            sessionId,
            fontSize = _fontSize,
            scrollback = _scrollback,
            fontFamily = TerminalFonts.BuildCssStack(_fontFamily),
            lineHeight = _lineHeight
        });
        SendRaw(new { type = "activate", sessionId });
    }

    public void ActivateSession(string sessionId)
    {
        SendRaw(new { type = "activate", sessionId });
    }

    public void CloseSession(string sessionId)
    {
        SendRaw(new { type = "close", sessionId });
    }

    public void ClearSession(string sessionId)
    {
        SendRaw(new { type = "clear", sessionId });
    }

    public void FocusTerminal()
    {
        SendRaw(new { type = "focus" });
    }

    public void Write(string sessionId, byte[] data)
    {
        SendRaw(new { type = "write", sessionId, data = Convert.ToBase64String(data) });
    }

    public void SetTheme(ThemePalette palette, string? customForeground = null)
    {
        _palette = palette;
        _customForeground = customForeground;
        try
        {
            WebView.DefaultBackgroundColor = ColorTranslator.FromHtml(palette.TerminalBackground);
        }
        catch
        {
            // ignored
        }
        SendTheme();
    }

    public void SetFontSize(double size)
    {
        _fontSize = Math.Clamp(size, 10, 26);
        SendFontSettings();
    }

    public void SetFontFamily(string family)
    {
        _fontFamily = string.IsNullOrWhiteSpace(family) ? "JetBrains Mono" : family;
        SendFontSettings();
    }

    public void SetLineHeight(double lineHeight)
    {
        _lineHeight = Math.Clamp(lineHeight, 1.0, 1.6);
        SendFontSettings();
    }

    public void SetScrollback(int lines)
    {
        _scrollback = Math.Clamp(lines, 500, 10000);
    }

    private void SendFontSettings()
    {
        SendRaw(new
        {
            type = "font",
            fontFamily = TerminalFonts.BuildCssStack(_fontFamily),
            fontSize = _fontSize,
            lineHeight = _lineHeight
        });
    }

    private void SendTheme()
    {
        if (!_ready.Task.IsCompleted) return;
        SendRaw(new
        {
            type = "theme",
            theme = ThemeManager.GetTerminalTheme(_palette, _customForeground),
            background = _palette.TerminalBackground,
            menuBackground = _palette.SurfaceElevated,
            menuBorder = _palette.Border,
            menuHover = _palette.SurfaceHover,
            menuText = _palette.TextPrimary,
            selection = _palette.TerminalSelection
        });
    }

    private void SendRaw(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        if (!_ready.Task.IsCompleted || WebView.CoreWebView2 is null)
        {
            _pending.Enqueue(json);
            return;
        }
        try
        {
            WebView.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch (Exception ex)
        {
            AppLog.Write("Posting terminal message failed.", ex);
            _pending.Enqueue(json);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (WebView.CoreWebView2 is not null)
                WebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            WebView.Dispose();
        }
        catch
        {
            // ignored
        }
    }
}
