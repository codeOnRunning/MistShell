using System.Windows;
using System.Windows.Threading;
using MistShell.Services;

namespace MistShell;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstanceMutex = new Mutex(true, "MistShell.SingleInstance." + Environment.UserName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("MistShell 已经在运行。\n\n请切换到已有窗口，避免同时启动多个实例。",
                "MistShell", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        try
        {
            AppPaths.EnsureCreated();
        }
        catch (Exception ex)
        {
            AppLog.Write("Preparing data directory failed.", ex);
            MessageBox.Show("无法创建 MistShell 数据目录：\n\n" + ex.Message, "MistShell",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        AppLog.Write("MistShell starting.");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Write("Unhandled AppDomain exception.", args.ExceptionObject as Exception);

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            AppLog.Write("Fatal startup error.", ex);
            MessageBox.Show("MistShell 启动失败：\n\n" + ex.Message, "MistShell",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private int _uiErrorCount;

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write("Unhandled UI exception.", e.Exception);
        e.Handled = true;
        if (Interlocked.Increment(ref _uiErrorCount) > 2) return;
        MessageBox.Show("发生未处理的错误：\n\n" + e.Exception.Message +
                        "\n\n详细信息已写入日志。", "MistShell",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Write("MistShell exiting.");
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
