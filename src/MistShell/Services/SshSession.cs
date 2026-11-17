using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using MistShell.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace MistShell.Services;

public sealed class SshSessionException : Exception
{
    public SshSessionException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed record CommandResult(string Output, string Error, int ExitStatus, TimeSpan Duration);

public sealed class SshSession : IDisposable
{
    private readonly ConnectionProfile _profile;
    private readonly string _password;
    private readonly string _passphrase;
    private readonly string? _privateKeyContent;
    private readonly Func<HostKeyInfo, HostKeyDecision> _hostKeyVerifier;
    private readonly Func<string, string?> _expectedFingerprintProvider;
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private SshClient? _client;
    private ShellStream? _shell;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private int _disconnectedRaised;

    public SshSession(
        ConnectionProfile profile,
        string password,
        string passphrase,
        Func<HostKeyInfo, HostKeyDecision> hostKeyVerifier,
        Func<string, string?> expectedFingerprintProvider,
        string? privateKeyContent = null)
    {
        _profile = profile;
        _password = password;
        _passphrase = passphrase;
        _privateKeyContent = privateKeyContent;
        _hostKeyVerifier = hostKeyVerifier;
        _expectedFingerprintProvider = expectedFingerprintProvider;
    }

    public event Action<byte[]>? DataReceived;
    public event Action<string>? Disconnected;
    public string? LastFingerprint { get; private set; }
    public DateTimeOffset? ConnectedAt { get; private set; }

    public bool IsConnected => _client?.IsConnected == true && _shell is not null;

    public async Task ConnectAsync(int columns, int rows)
    {
        if (IsConnected) return;
        await Task.Run(() =>
        {
            try
            {
                var info = BuildConnectionInfo(_profile, _password, _passphrase, _privateKeyContent);
                var client = new SshClient(info)
                {
                    KeepAliveInterval = TimeSpan.FromSeconds(30)
                };
                client.HostKeyReceived += OnHostKeyReceived;
                client.ErrorOccurred += (_, args) => AppLog.Write("SSH error: " + args.Exception.Message, args.Exception);
                client.Connect();
                if (!client.IsConnected)
                    throw new SshSessionException("SSH 连接未建立。");

                var shell = client.CreateShellStream("xterm-256color",
                    (uint)Math.Max(20, columns), (uint)Math.Max(5, rows), 0, 0, 32768);
                _client = client;
                _shell = shell;
                _readCts = new CancellationTokenSource();
                ConnectedAt = DateTimeOffset.UtcNow;
                _readTask = Task.Run(() => ReadLoop(_readCts.Token));
            }
            catch (SshAuthenticationException ex)
            {
                throw new SshSessionException("认证失败：用户名、密码或私钥不正确。", ex);
            }
            catch (SocketException ex)
            {
                throw new SshSessionException($"无法连接到 {_profile.Host}:{_profile.Port}，请检查网络和端口。", ex);
            }
            catch (SshConnectionException ex)
            {
                throw new SshSessionException($"SSH 连接失败：{ex.Message}", ex);
            }
            catch (SshSessionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new SshSessionException("连接失败：" + ex.Message, ex);
            }
        });
    }

    public async Task WriteAsync(byte[] data)
    {
        var shell = _shell;
        if (shell is null) return;
        await _writeGate.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                try
                {
                    shell.Write(data, 0, data.Length);
                    shell.Flush();
                }
                catch (Exception ex)
                {
                    AppLog.Write("Terminal write failed.", ex);
                }
            });
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task WriteTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        await WriteAsync(Encoding.UTF8.GetBytes(text));
    }

    public void Resize(int columns, int rows)
    {
        try
        {
            if (_shell is null || columns < 2 || rows < 2) return;
            _shell.ChangeWindowSize((uint)columns, (uint)rows, 0, 0);
        }
        catch (Exception ex)
        {
            AppLog.Write("Terminal resize failed.", ex);
        }
    }

    public async Task<CommandResult> RunCommandAsync(string command, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var client = _client;
        if (client is null || !client.IsConnected)
            throw new SshSessionException("会话未连接。");

        await _commandGate.WaitAsync(cancellationToken);
        var sw = Stopwatch.StartNew();
        try
        {
            return await Task.Run(async () =>
            {
                using var cmd = client.CreateCommand(command);
                cmd.CommandTimeout = timeout ?? TimeSpan.FromSeconds(20);
                await cmd.ExecuteAsync(cancellationToken);
                sw.Stop();
                return new CommandResult(cmd.Result ?? "", cmd.Error ?? "", cmd.ExitStatus ?? -1, sw.Elapsed);
            }, cancellationToken);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<long> MeasureLatencyAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        await RunCommandAsync("true", TimeSpan.FromSeconds(8), cancellationToken);
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    public async Task<SftpClient> CreateSftpClientAsync(CancellationToken cancellationToken = default)
    {
        var info = BuildConnectionInfo(_profile, _password, _passphrase, _privateKeyContent);
        return await Task.Run(() =>
        {
            var client = new SftpClient(info)
            {
                KeepAliveInterval = TimeSpan.FromSeconds(30),
                OperationTimeout = TimeSpan.FromSeconds(30)
            };
            client.HostKeyReceived += OnHostKeyReceived;
            cancellationToken.ThrowIfCancellationRequested();
            client.Connect();
            if (!client.IsConnected)
            {
                client.Dispose();
                throw new SshSessionException("SFTP 连接未建立。");
            }
            return client;
        }, cancellationToken);
    }

    public void Disconnect(string reason = "已断开")
    {
        try
        {
            _readCts?.Cancel();
            _shell?.Dispose();
            _client?.Disconnect();
            _client?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Write("Disconnect failed.", ex);
        }
        finally
        {
            _shell = null;
            _client = null;
            RaiseDisconnected(reason);
        }
    }

    private void ReadLoop(CancellationToken token)
    {
        var buffer = new byte[32768];
        try
        {
            while (!token.IsCancellationRequested && _shell is not null)
            {
                var read = _shell.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;
                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                DataReceived?.Invoke(chunk);
            }
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            AppLog.Write("Terminal read loop ended with error.", ex);
        }
        finally
        {
            if (!token.IsCancellationRequested)
                RaiseDisconnected("连接已断开");
        }
    }

    private void OnHostKeyReceived(object? sender, HostKeyEventArgs e)
    {
        var expected = _expectedFingerprintProvider(e.HostKeyName);
        var info = new HostKeyInfo(
            _profile.Host,
            _profile.Port,
            e.HostKeyName,
            "SHA256:" + e.FingerPrintSHA256,
            expected);

        // A previously pinned key must not require UI interaction. This also
        // prevents the SSH session timeout from expiring while a dialog waits.
        if (!string.IsNullOrWhiteSpace(expected) &&
            string.Equals(expected, info.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            LastFingerprint = info.Fingerprint;
            e.CanTrust = true;
            return;
        }

        HostKeyDecision decision;
        try
        {
            decision = _hostKeyVerifier(info);
        }
        catch (Exception ex)
        {
            AppLog.Write("Host key verification failed.", ex);
            decision = HostKeyDecision.Reject;
        }

        if (decision != HostKeyDecision.Reject)
        {
            LastFingerprint = info.Fingerprint;
            e.CanTrust = true;
        }
        else
        {
            e.CanTrust = false;
        }
    }

    private void RaiseDisconnected(string reason)
    {
        if (Interlocked.Exchange(ref _disconnectedRaised, 1) == 1) return;
        Disconnected?.Invoke(reason);
    }

    public static ConnectionInfo BuildConnectionInfo(ConnectionProfile profile, string password, string passphrase,
        string? privateKeyContent = null)
    {
        var methods = new List<AuthenticationMethod>();
        if (profile.AuthMethod == SshAuthMethod.PrivateKey)
        {
            PrivateKeyFile key;
            try
            {
                if (!string.IsNullOrWhiteSpace(privateKeyContent))
                {
                    using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(privateKeyContent));
                    key = string.IsNullOrEmpty(passphrase)
                        ? new PrivateKeyFile(keyStream)
                        : new PrivateKeyFile(keyStream, passphrase);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(profile.PrivateKeyPath))
                        throw new SshSessionException("没有选择私钥文件或已保存私钥。");
                    if (!File.Exists(profile.PrivateKeyPath))
                        throw new SshSessionException("私钥文件不存在：" + profile.PrivateKeyPath);
                    key = string.IsNullOrEmpty(passphrase)
                        ? new PrivateKeyFile(profile.PrivateKeyPath)
                        : new PrivateKeyFile(profile.PrivateKeyPath, passphrase);
                }
            }
            catch (SshSessionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new SshSessionException("无法读取私钥，请检查私钥格式或口令。", ex);
            }
            methods.Add(new PrivateKeyAuthenticationMethod(profile.Username, key));
        }
        else
        {
            methods.Add(new PasswordAuthenticationMethod(profile.Username, password));
            var keyboard = new KeyboardInteractiveAuthenticationMethod(profile.Username);
            keyboard.AuthenticationPrompt += (_, e) =>
            {
                foreach (var prompt in e.Prompts)
                    prompt.Response = password;
            };
            methods.Add(keyboard);
        }

        return new ConnectionInfo(profile.Host, profile.Port, profile.Username, methods.ToArray())
        {
            // Leave enough time for the first-connection fingerprint dialog.
            Timeout = TimeSpan.FromSeconds(90)
        };
    }

    public void Dispose()
    {
        Disconnect("已关闭");
        _commandGate.Dispose();
        _writeGate.Dispose();
        _readCts?.Dispose();
    }
}
