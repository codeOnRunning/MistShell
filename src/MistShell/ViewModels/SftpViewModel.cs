using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using MistShell.Infrastructure;
using MistShell.Models;
using MistShell.Services;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace MistShell.ViewModels;

public sealed class SftpViewModel : ObservableObject, IDisposable
{
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private SshSession? _session;
    private SftpClient? _client;
    private string _currentPath = "/";
    private string _statusText = "SFTP 尚未连接";
    private bool _isLoading;
    private bool _isConnected;
    private string? _errorMessage;
    private SftpFileItem? _selectedItem;
    private int _disposed;

    public ObservableCollection<SftpFileItem> Files { get; } = new();
    public ObservableCollection<TransferItem> Transfers { get; } = new();

    public string CurrentPath
    {
        get => _currentPath;
        set => SetProperty(ref _currentPath, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public SftpFileItem? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public void Attach(SshSession session) => _session = session;

    public void Reset()
    {
        try { _client?.Dispose(); } catch { }
        _client = null;
        IsConnected = false;
        Files.Clear();
        Transfers.Clear();
        StatusText = "SFTP 尚未连接";
        CurrentPath = "/";
        ErrorMessage = null;
    }

    public async Task EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        if (_client?.IsConnected == true) return;
        if (_session is null) throw new InvalidOperationException("SFTP 会话尚未绑定。");

        await _connectGate.WaitAsync(cancellationToken);
        try
        {
            if (_client?.IsConnected == true) return;
            IsLoading = true;
            ErrorMessage = null;
            StatusText = "正在连接 SFTP…";
            _client?.Dispose();
            _client = await _session.CreateSftpClientAsync(cancellationToken);
            IsConnected = true;
            var working = string.IsNullOrWhiteSpace(_client.WorkingDirectory) ? "/" : _client.WorkingDirectory;
            await LoadCoreAsync(working, cancellationToken);
            StatusText = "SFTP 已连接";
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ErrorMessage = FriendlyError(ex);
            StatusText = "SFTP 连接失败";
            throw;
        }
        finally
        {
            IsLoading = false;
            _connectGate.Release();
        }
    }

    public async Task LoadAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(path) ? CurrentPath : path.Trim();
        if (!target.StartsWith('/')) target = SftpFileItem.CombineUnix(CurrentPath, target);
        try
        {
            await EnsureConnectedAsync(cancellationToken);
            await LoadCoreAsync(target, cancellationToken);
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"{FriendlyError(ex)}（{target}）";
            AppLog.Write($"SFTP load failed: {target}", ex);
        }
    }

    public Task RefreshAsync() => LoadAsync(CurrentPath);

    public Task NavigateUpAsync()
    {
        var parent = ParentPath(CurrentPath);
        return LoadAsync(parent);
    }

    public async Task<bool> OpenAsync(SftpFileItem item)
    {
        if (!item.IsDirectory) return false;
        await LoadAsync(item.FullPath);
        return true;
    }

    private async Task LoadCoreAsync(string path, CancellationToken cancellationToken)
    {
        if (_client is null) throw new InvalidOperationException("SFTP 尚未连接。");
        IsLoading = true;
        StatusText = "正在读取目录…";
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var entries = await Task.Run(() => _client.ListDirectory(path).ToList(), cancellationToken);
            var list = entries
                .Where(f => f.Name is not "." and not "..")
                .Select(f => SftpFileItem.From(f, path))
                .OrderByDescending(f => f.IsDirectory)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Files.Clear();
            foreach (var item in list) Files.Add(item);
            CurrentPath = path;
            StatusText = $"{list.Count} 个项目";
        }
        catch (SftpPermissionDeniedException ex)
        {
            throw new InvalidOperationException("没有权限访问该目录。", ex);
        }
        finally
        {
            IsLoading = false;
            _operationGate.Release();
        }
    }

    public async Task CreateFolderAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        await EnsureConnectedAsync();
        await RunSftpOperationAsync(() =>
        {
            var path = SftpFileItem.CombineUnix(CurrentPath, name.Trim());
            _client!.CreateDirectory(path);
        });
        await RefreshAsync();
    }

    public async Task RenameAsync(SftpFileItem item, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;
        await EnsureConnectedAsync();
        await RunSftpOperationAsync(() =>
        {
            var destination = SftpFileItem.CombineUnix(CurrentPath, newName.Trim());
            _client!.RenameFile(item.FullPath, destination);
        });
        await RefreshAsync();
    }

    public async Task DeleteAsync(SftpFileItem item, bool recursive = false)
    {
        await EnsureConnectedAsync();
        await RunSftpOperationAsync(() =>
        {
            if (item.IsDirectory)
            {
                if (recursive) DeleteDirectoryRecursive(_client!, item.FullPath);
                else _client!.DeleteDirectory(item.FullPath);
            }
            else
            {
                _client!.DeleteFile(item.FullPath);
            }
        });
        await RefreshAsync();
    }

    public async Task<string> ReadTextFileAsync(SftpFileItem item, CancellationToken cancellationToken = default)
    {
        const long maxEditableBytes = 8 * 1024 * 1024;
        if (item.Size > maxEditableBytes)
            throw new InvalidDataException("文件超过 8 MB，请使用下载功能。" );

        await EnsureConnectedAsync(cancellationToken);
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var bytes = await Task.Run(() =>
            {
                if (_client is null) throw new InvalidOperationException("SFTP 尚未连接。");
                using var stream = new MemoryStream();
                _client.DownloadFile(item.FullPath, stream);
                return stream.ToArray();
            }, cancellationToken);

            if (bytes.Length > maxEditableBytes)
                throw new InvalidDataException("文件超过 8 MB，请使用下载功能。");
            if (bytes.Contains((byte)0))
                throw new InvalidDataException("这是二进制文件，暂不支持直接在编辑器里打开。请先下载到本地。");

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException("文件不是 UTF-8 文本，请先下载到本地编辑。", ex);
            }
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            ErrorMessage = FriendlyError(ex);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task WriteTextFileAsync(SftpFileItem item, string text)
    {
        await EnsureConnectedAsync();
        var bytes = new UTF8Encoding(false).GetBytes(text);
        await RunSftpOperationAsync(() =>
        {
            if (_client is null) throw new InvalidOperationException("SFTP 尚未连接。");
            using var stream = new MemoryStream(bytes);
            _client.UploadFile(stream, item.FullPath, true);
        });
        await RefreshAsync();
    }

    public async Task DeleteManyAsync(IEnumerable<SftpFileItem> items)
    {
        var selected = items.ToList();
        if (selected.Count == 0) return;

        await EnsureConnectedAsync();
        await RunSftpOperationAsync(() =>
        {
            if (_client is null) throw new InvalidOperationException("SFTP 尚未连接。");
            foreach (var item in selected)
            {
                if (item.IsDirectory) DeleteDirectoryRecursive(_client, item.FullPath);
                else _client.DeleteFile(item.FullPath);
            }
        });
        await RefreshAsync();
    }

    public async Task ChangePermissionsAsync(SftpFileItem item, string octal)
    {
        var normalized = octal.Trim();
        if (normalized.Length is < 3 or > 4)
            throw new ArgumentException("权限格式应为 644、755 或 2755 这样的八进制数字。");
        if (!int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            throw new ArgumentException("权限必须是八进制数字。");
        var mode = Convert.ToInt32(normalized, 8);
        await EnsureConnectedAsync();
        await RunSftpOperationAsync(() => _client!.ChangePermissions(item.FullPath, (short)mode));
        await RefreshAsync();
    }

    public async Task UploadFilesAsync(IEnumerable<string> localPaths)
    {
        await EnsureConnectedAsync();
        foreach (var localPath in localPaths)
        {
            if (!File.Exists(localPath)) continue;
            var fileInfo = new FileInfo(localPath);
            var remotePath = SftpFileItem.CombineUnix(CurrentPath, fileInfo.Name);
            var transfer = new TransferItem
            {
                Name = fileInfo.Name,
                LocalPath = localPath,
                RemotePath = remotePath,
                Direction = TransferDirection.Upload,
                TotalBytes = fileInfo.Length
            };
            Transfers.Insert(0, transfer);
            _ = ProcessUploadAsync(transfer);
        }
    }

    public async Task DownloadAsync(SftpFileItem item, string localPath)
    {
        if (item.IsDirectory) throw new InvalidOperationException("请使用文件夹下载。");
        await EnsureConnectedAsync();
        var transfer = new TransferItem
        {
            Name = item.Name,
            LocalPath = localPath,
            RemotePath = item.FullPath,
            Direction = TransferDirection.Download,
            TotalBytes = item.Size
        };
        Transfers.Insert(0, transfer);
        _ = ProcessDownloadAsync(transfer);
    }

    public async Task UploadFolderAsync(string localFolder)
    {
        if (!Directory.Exists(localFolder))
            throw new DirectoryNotFoundException("本地文件夹不存在：" + localFolder);

        await EnsureConnectedAsync();
        var folderInfo = new DirectoryInfo(localFolder);
        var folderName = string.IsNullOrWhiteSpace(folderInfo.Name) ? "upload" : folderInfo.Name;
        var remoteRoot = SftpFileItem.CombineUnix(CurrentPath, folderName);

        var directories = await Task.Run(() => Directory
            .EnumerateDirectories(localFolder, "*", SearchOption.AllDirectories)
            .Select(path => new LocalFolderEntry(path, Path.GetRelativePath(localFolder, path).Replace('\\', '/')))
            .ToList());
        var files = await Task.Run(() => Directory
            .EnumerateFiles(localFolder, "*", SearchOption.AllDirectories)
            .Select(path => new LocalFileEntry(path,
                Path.GetRelativePath(localFolder, path).Replace('\\', '/'),
                new FileInfo(path).Length))
            .ToList());

        var transfer = new TransferItem
        {
            Name = folderName + "/",
            LocalPath = localFolder,
            RemotePath = remoteRoot,
            Direction = TransferDirection.Upload,
            TotalBytes = files.Sum(file => file.Length),
            IsFolder = true,
            TotalFiles = files.Count
        };
        Transfers.Insert(0, transfer);
        _ = ProcessUploadFolderAsync(transfer, remoteRoot, directories, files);
    }

    public async Task DownloadFolderAsync(SftpFileItem item, string localParent)
    {
        if (!item.IsDirectory) throw new InvalidOperationException("请选择文件夹。");
        await EnsureConnectedAsync();
        StatusText = "正在扫描远端目录…";
        var tree = await Task.Run(() => BuildRemoteTree(item.FullPath));
        var files = tree.Where(entry => !entry.IsDirectory).ToList();
        var localRoot = Path.Combine(localParent, SanitizeLocalName(item.Name));
        var transfer = new TransferItem
        {
            Name = item.Name + "/",
            LocalPath = localRoot,
            RemotePath = item.FullPath,
            Direction = TransferDirection.Download,
            TotalBytes = files.Sum(file => file.Size),
            IsFolder = true,
            TotalFiles = files.Count
        };
        Transfers.Insert(0, transfer);
        StatusText = $"{files.Count} 个文件";
        _ = ProcessDownloadFolderAsync(transfer, localRoot, tree);
    }

    private async Task ProcessUploadFolderAsync(
        TransferItem transfer,
        string remoteRoot,
        IReadOnlyList<LocalFolderEntry> directories,
        IReadOnlyList<LocalFileEntry> files)
    {
        var gateHeld = false;
        var completed = false;
        try
        {
            await _operationGate.WaitAsync(transfer.Cancellation.Token);
            gateHeld = true;
            transfer.MarkStarted();
            await Task.Run(() => EnsureRemoteDirectory(remoteRoot), transfer.Cancellation.Token);

            foreach (var directory in directories)
            {
                transfer.Cancellation.Token.ThrowIfCancellationRequested();
                await Task.Run(
                    () => EnsureRemoteDirectory(SftpFileItem.CombineUnix(remoteRoot, directory.RelativePath)),
                    transfer.Cancellation.Token);
            }

            var progress = new Progress<long>(value => transfer.TransferredBytes = value);
            var completedBytes = 0L;
            foreach (var file in files)
            {
                transfer.Cancellation.Token.ThrowIfCancellationRequested();
                var remotePath = SftpFileItem.CombineUnix(remoteRoot, file.RelativePath);
                await Task.Run(
                    () => EnsureRemoteDirectory(ParentDirectory(remotePath)),
                    transfer.Cancellation.Token);

                using var stream = File.OpenRead(file.FullPath);
                var before = completedBytes;
                await Task.Run(() => _client!.UploadFile(stream, remotePath, true,
                    count =>
                    {
                        if (transfer.Cancellation.IsCancellationRequested) throw new OperationCanceledException();
                        ((IProgress<long>)progress).Report(before + (long)count);
                    }), transfer.Cancellation.Token);
                completedBytes += file.Length;
                transfer.TransferredBytes = completedBytes;
                transfer.CompletedFiles++;
            }

            transfer.MarkCompleted();
            completed = true;
        }
        catch (OperationCanceledException)
        {
            transfer.Status = "已取消";
        }
        catch (Exception ex)
        {
            transfer.MarkFailed(FriendlyError(ex));
        }
        finally
        {
            if (gateHeld) _operationGate.Release();
        }

        if (completed)
        {
            try { await RefreshAsync(); }
            catch (Exception ex) { AppLog.Write("SFTP refresh after folder upload failed.", ex); }
        }
    }

    private async Task ProcessDownloadFolderAsync(
        TransferItem transfer,
        string localRoot,
        IReadOnlyList<RemoteTreeEntry> tree)
    {
        var gateHeld = false;
        try
        {
            await _operationGate.WaitAsync(transfer.Cancellation.Token);
            gateHeld = true;
            transfer.MarkStarted();
            Directory.CreateDirectory(localRoot);

            foreach (var directory in tree.Where(entry => entry.IsDirectory && entry.RelativePath.Length > 0))
            {
                Directory.CreateDirectory(ToLocalPath(localRoot, directory.RelativePath));
            }

            var files = tree.Where(entry => !entry.IsDirectory).ToList();
            var progress = new Progress<long>(value => transfer.TransferredBytes = value);
            var completedBytes = 0L;
            foreach (var file in files)
            {
                transfer.Cancellation.Token.ThrowIfCancellationRequested();
                var localPath = ToLocalPath(localRoot, file.RelativePath);
                var parent = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

                using var stream = File.Create(localPath);
                var before = completedBytes;
                await Task.Run(() => _client!.DownloadFile(file.FullPath, stream,
                    count =>
                    {
                        if (transfer.Cancellation.IsCancellationRequested) throw new OperationCanceledException();
                        ((IProgress<long>)progress).Report(before + (long)count);
                    }), transfer.Cancellation.Token);
                completedBytes += file.Size;
                transfer.TransferredBytes = completedBytes;
                transfer.CompletedFiles++;
            }

            transfer.MarkCompleted();
        }
        catch (OperationCanceledException)
        {
            transfer.Status = "已取消";
        }
        catch (Exception ex)
        {
            transfer.MarkFailed(FriendlyError(ex));
        }
        finally
        {
            if (gateHeld) _operationGate.Release();
        }
    }

    private void EnsureRemoteDirectory(string path)
    {
        if (_client is null || string.IsNullOrWhiteSpace(path) || path == "/") return;

        // CurrentPath is known to exist. Only create the new components below
        // it, never try to recreate system directories such as /home first.
        var basePath = string.IsNullOrWhiteSpace(CurrentPath) ? "/" : CurrentPath.TrimEnd('/');
        if (basePath.Length == 0) basePath = "/";
        var current = basePath == "/" ? "" : basePath;
        var relative = path.StartsWith(basePath == "/" ? "/" : basePath + "/", StringComparison.Ordinal)
            ? path[(basePath == "/" ? 1 : basePath.Length + 1)..]
            : path.TrimStart('/');

        foreach (var part in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + part;
            if (!RemoteDirectoryExists(current)) _client.CreateDirectory(current);
        }
    }

    private bool RemoteDirectoryExists(string path)
    {
        if (_client is null || string.IsNullOrEmpty(path) || path == "/") return true;
        var parent = ParentDirectory(path);
        var name = path[(path.LastIndexOf('/') + 1)..];
        try
        {
            return _client.ListDirectory(parent).Any(entry =>
                string.Equals(entry.Name, name, StringComparison.Ordinal) && entry.IsDirectory);
        }
        catch
        {
            return false;
        }
    }

    private List<RemoteTreeEntry> BuildRemoteTree(string rootPath)
    {
        var entries = new List<RemoteTreeEntry>();
        Walk(rootPath, "");
        return entries;

        void Walk(string remoteDirectory, string relativeDirectory)
        {
            foreach (var file in _client!.ListDirectory(remoteDirectory))
            {
                if (file.Name is "." or "..") continue;
                var fullPath = SftpFileItem.CombineUnix(remoteDirectory, file.Name);
                var relativePath = string.IsNullOrEmpty(relativeDirectory)
                    ? file.Name
                    : relativeDirectory + "/" + file.Name;
                if (file.IsDirectory)
                {
                    entries.Add(new RemoteTreeEntry(fullPath, relativePath, true, 0));
                    if (!file.IsSymbolicLink) Walk(fullPath, relativePath);
                }
                else
                {
                    entries.Add(new RemoteTreeEntry(fullPath, relativePath, false, file.Length));
                }
            }
        }
    }

    private static string ParentDirectory(string path)
    {
        var index = path.LastIndexOf('/');
        return index <= 0 ? "/" : path[..index];
    }

    private static string ToLocalPath(string root, string relativePath)
    {
        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeLocalName)
            .ToArray();
        return parts.Length == 0 ? root : Path.Combine(new[] { root }.Concat(parts).ToArray());
    }

    private static string SanitizeLocalName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim().TrimEnd('.');
        return result is "" or "." or ".." ? "_" : result;
    }

    private sealed record LocalFolderEntry(string FullPath, string RelativePath);
    private sealed record LocalFileEntry(string FullPath, string RelativePath, long Length);
    private sealed record RemoteTreeEntry(string FullPath, string RelativePath, bool IsDirectory, long Size);

    private async Task ProcessUploadAsync(TransferItem transfer)
    {
        var gateHeld = false;
        var completed = false;
        try
        {
            await _operationGate.WaitAsync(transfer.Cancellation.Token);
            gateHeld = true;
            transfer.MarkStarted();
            using var stream = File.OpenRead(transfer.LocalPath);
            var progress = new Progress<ulong>(value => transfer.TransferredBytes = (long)value);
            await Task.Run(() => _client!.UploadFile(stream, transfer.RemotePath, true,
                count =>
                {
                    if (transfer.Cancellation.IsCancellationRequested) throw new OperationCanceledException();
                    ((IProgress<ulong>)progress).Report(count);
                }), transfer.Cancellation.Token);
            transfer.MarkCompleted();
            completed = true;
        }
        catch (OperationCanceledException)
        {
            transfer.Status = "已取消";
        }
        catch (Exception ex)
        {
            transfer.MarkFailed(FriendlyError(ex));
        }
        finally
        {
            // Never refresh while holding the gate: RefreshAsync needs it too.
            if (gateHeld) _operationGate.Release();
        }

        if (completed)
        {
            // Let the SFTP list show the new file immediately after upload.
            try { await RefreshAsync(); }
            catch (Exception ex) { AppLog.Write("SFTP refresh after upload failed.", ex); }
        }
    }

    private async Task ProcessDownloadAsync(TransferItem transfer)
    {
        var gateHeld = false;
        try
        {
            await _operationGate.WaitAsync(transfer.Cancellation.Token);
            gateHeld = true;
            transfer.MarkStarted();
            var directory = Path.GetDirectoryName(transfer.LocalPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            using var stream = File.Create(transfer.LocalPath);
            var progress = new Progress<ulong>(value => transfer.TransferredBytes = (long)value);
            await Task.Run(() => _client!.DownloadFile(transfer.RemotePath, stream,
                count =>
                {
                    if (transfer.Cancellation.IsCancellationRequested) throw new OperationCanceledException();
                    ((IProgress<ulong>)progress).Report(count);
                }), transfer.Cancellation.Token);
            transfer.MarkCompleted();
        }
        catch (OperationCanceledException)
        {
            transfer.Status = "已取消";
            TryDelete(transfer.LocalPath);
        }
        catch (Exception ex)
        {
            transfer.MarkFailed(FriendlyError(ex));
            TryDelete(transfer.LocalPath);
        }
        finally
        {
            if (gateHeld) _operationGate.Release();
        }
    }

    private async Task RunSftpOperationAsync(Action action)
    {
        await _operationGate.WaitAsync();
        try
        {
            await Task.Run(action);
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = FriendlyError(ex);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private static void DeleteDirectoryRecursive(SftpClient client, string path)
    {
        foreach (var entry in client.ListDirectory(path))
        {
            if (entry.Name is "." or "..") continue;
            if (entry.IsDirectory) DeleteDirectoryRecursive(client, entry.FullName);
            else client.DeleteFile(entry.FullName);
        }
        client.DeleteDirectory(path);
    }

    public async Task RetryAsync(TransferItem transfer)
    {
        if (!transfer.CanRetry) return;

        transfer.ResetForRetry();
        try
        {
            await EnsureConnectedAsync();
            ErrorMessage = null;

            if (transfer.IsFolder && transfer.Direction == TransferDirection.Upload)
            {
                if (!Directory.Exists(transfer.LocalPath))
                    throw new DirectoryNotFoundException("本地文件夹不存在：" + transfer.LocalPath);
                var directories = await Task.Run(() => Directory
                    .EnumerateDirectories(transfer.LocalPath, "*", SearchOption.AllDirectories)
                    .Select(path => new LocalFolderEntry(path,
                        Path.GetRelativePath(transfer.LocalPath, path).Replace('\\', '/')))
                    .ToList());
                var files = await Task.Run(() => Directory
                    .EnumerateFiles(transfer.LocalPath, "*", SearchOption.AllDirectories)
                    .Select(path => new LocalFileEntry(path,
                        Path.GetRelativePath(transfer.LocalPath, path).Replace('\\', '/'),
                        new FileInfo(path).Length))
                    .ToList());
                _ = ProcessUploadFolderAsync(transfer, transfer.RemotePath, directories, files);
                return;
            }

            if (transfer.IsFolder && transfer.Direction == TransferDirection.Download)
            {
                var tree = await Task.Run(() => BuildRemoteTree(transfer.RemotePath));
                _ = ProcessDownloadFolderAsync(transfer, transfer.LocalPath, tree);
                return;
            }

            if (transfer.Direction == TransferDirection.Upload)
                _ = ProcessUploadAsync(transfer);
            else
                _ = ProcessDownloadAsync(transfer);
        }
        catch (Exception ex)
        {
            transfer.MarkFailed(FriendlyError(ex));
            AppLog.Write("SFTP retry failed.", ex);
        }
    }

    public void CancelTransfer(TransferItem transfer)
    {
        try
        {
            transfer.Cancellation.Cancel();
        }
        catch
        {
            // ignored
        }
    }

    public void ClearFinishedTransfers()
    {
        foreach (var transfer in Transfers.Where(t => t.Status == "已完成").ToList())
            Transfers.Remove(transfer);
    }

    private static string ParentPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/") return "/";
        var trimmed = path.TrimEnd('/');
        var index = trimmed.LastIndexOf('/');
        return index <= 0 ? "/" : trimmed[..index];
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        SftpPermissionDeniedException => "权限不足。",
        SshAuthenticationException => "认证失败，请重新连接。",
        SftpPathNotFoundException => "路径不存在。",
        SshConnectionException => "SFTP 连接已断开。",
        OperationCanceledException => "操作已取消。",
        _ => ex.Message
    };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // ignored
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        try { _client?.Dispose(); } catch { }
        _client = null;
        _connectGate.Dispose();
        _operationGate.Dispose();
    }
}
