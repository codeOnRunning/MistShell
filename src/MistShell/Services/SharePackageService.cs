using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace MistShell.Services;

public sealed record SharePackageResult(string Path, bool IsInstaller, string Detail);

public static class SharePackageService
{
    public static async Task<SharePackageResult> CreateAsync(
        string destination,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        string? sourceDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(destination))
            throw new ArgumentException("请选择保存位置。", nameof(destination));

        var baseDirectory = (sourceDirectory ?? AppContext.BaseDirectory).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var work = Path.Combine(Path.GetTempPath(), "MistShell-share-" + Guid.NewGuid().ToString("N"));
        var payloadDirectory = Path.Combine(work, "payload");
        var payloadZip = Path.Combine(work, "payload.zip");
        var installScript = Path.Combine(work, "install.cmd");
        var generatedSetup = Path.Combine(work, "setup.exe");
        Directory.CreateDirectory(payloadDirectory);

        try
        {
            progress?.Report("正在收集程序文件…");
            await Task.Run(() => CopyPayload(baseDirectory, payloadDirectory), cancellationToken);

            progress?.Report("正在压缩安装文件…");
            await Task.Run(() => ZipFile.CreateFromDirectory(
                payloadDirectory, payloadZip, CompressionLevel.Optimal, false), cancellationToken);

            if (string.Equals(Path.GetExtension(destination), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
                File.Copy(payloadZip, destination, true);
                return new SharePackageResult(destination, false,
                    "已生成便携压缩包。包内不含服务器配置、私钥、主机指纹或日志。");
            }

            await File.WriteAllTextAsync(installScript, BuildInstallScript().ReplaceLineEndings("\r\n"),
                new UTF8Encoding(false), cancellationToken);

            var iexpress = Path.Combine(Environment.SystemDirectory, "iexpress.exe");
            if (File.Exists(iexpress))
            {
                progress?.Report("正在生成安装包…");
                var sed = Path.Combine(work, "package.sed");
                await File.WriteAllTextAsync(sed, BuildSed(work, generatedSetup).ReplaceLineEndings("\r\n"),
                    new UTF8Encoding(false), cancellationToken);
                await RunIExpressAsync(iexpress, sed, generatedSetup, cancellationToken);
                if (File.Exists(generatedSetup))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
                    File.Copy(generatedSetup, destination, true);
                    return new SharePackageResult(destination, true,
                        "已生成安装包。包内不含服务器配置、私钥、主机指纹或日志。");
                }
            }

            // On systems where IExpress is unavailable, still provide a clean portable
            // package rather than failing the share operation.
            var portable = Path.ChangeExtension(destination, ".zip");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(portable))!);
            File.Copy(payloadZip, portable, true);
            return new SharePackageResult(portable, false,
                "当前系统无法生成 Setup.exe，已改为生成便携压缩包。包内不含服务器配置、私钥、主机指纹或日志。");
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }

    private static void CopyPayload(string sourceRoot, string targetRoot)
    {
        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, source).Replace('\\', '/');
            if (ShouldExclude(relative)) continue;

            var target = Path.Combine(targetRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            var targetDirectory = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(targetDirectory)) Directory.CreateDirectory(targetDirectory);
            File.Copy(source, target, true);
        }
    }

    private static bool ShouldExclude(string relativePath)
    {
        if (relativePath.Equals("data", StringComparison.OrdinalIgnoreCase) ||
            relativePath.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
            return true;

        var fileName = Path.GetFileName(relativePath);
        if (fileName.Equals("mistshell.log", StringComparison.OrdinalIgnoreCase)) return true;
        if (fileName.StartsWith("mistshell.log.", StringComparison.OrdinalIgnoreCase)) return true;
        if (relativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string BuildInstallScript() => """
        @echo off
        setlocal
        set "TARGET=%LOCALAPPDATA%\Programs\MistShell"
        if not exist "%TARGET%" mkdir "%TARGET%"

        powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath '%~dp0payload.zip' -DestinationPath '%TARGET%' -Force"
        if errorlevel 1 goto failed

        powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([IO.Path]::Combine([Environment]::GetFolderPath('Desktop'),'MistShell.lnk')); $s.TargetPath=[IO.Path]::Combine('%TARGET%','MistShell.exe'); $s.WorkingDirectory='%TARGET%'; $s.Save()"
        start "" "%TARGET%\MistShell.exe"
        exit /b 0

        :failed
        echo.
        echo 安装失败，请确认当前用户对 %TARGET% 有写入权限。
        pause
        exit /b 1
        """;

    private static string BuildSed(string sourceDirectory, string targetFile)
    {
        var root = sourceDirectory.TrimEnd(Path.DirectorySeparatorChar);
        return $"""
            [Version]
            Class=IEXPRESS
            SEDVersion=3
            [Options]
            PackagePurpose=InstallApp
            ShowInstallProgramWindow=0
            HideExtractAnimation=1
            UseLongFileNames=1
            AppLaunched=install.cmd
            PostInstallCmd=<None>
            AdminQuietInstCmd=
            UserQuietInstCmd=
            SourceFiles=SourceFiles
            TargetName={targetFile}
            [Strings]
            FILE0="payload.zip"
            FILE1="install.cmd"
            [SourceFiles]
            SourceFiles0={root}
            [SourceFiles0]
            %FILE0%=
            %FILE1%=
            """;
    }

    private static async Task RunIExpressAsync(
        string iexpress,
        string sedFile,
        string expectedOutput,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = iexpress,
            UseShellExecute = false,
            CreateNoWindow = false
        };
        startInfo.ArgumentList.Add("/N");
        startInfo.ArgumentList.Add("/Q");
        startInfo.ArgumentList.Add(sedFile);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动安装包生成器。");
        await process.WaitForExitAsync(cancellationToken);

        // IExpress can return before the final file is flushed on some systems.
        for (var i = 0; i < 30 && !File.Exists(expectedOutput); i++)
            await Task.Delay(200, cancellationToken);
        if (process.ExitCode != 0 || !File.Exists(expectedOutput))
            throw new InvalidOperationException("安装包生成器未能生成文件。");
    }
}
