using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LwpTerm.Core.Security;
using LwpTerm.Core.Sessions;
using LwpTerm.Core.Terminal;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace LwpTerm.Connections.Ssh;

/// <summary>
/// <see cref="ITerminalConnection"/> over an SSH shell channel (SSH.NET
/// <see cref="ShellStream"/>). Host keys are checked against
/// <see cref="KnownHostsStore"/> and, when unknown or changed, referred to
/// <see cref="IHostKeyVerifier"/>.
/// </summary>
public sealed class SshTerminalConnection : ITerminalConnection
{
    private readonly SshConnectionSettings _settings;
    private readonly string? _password;
    private readonly string? _passphrase;
    private readonly IHostKeyVerifier _hostKeyVerifier;
    private readonly KnownHostsStore _knownHosts;
    private readonly ILogger _log;
    private readonly object _writeLock = new();

    private SshClient? _client;
    private ShellStream? _shell;
    private Thread? _reader;
    private volatile bool _closed;

    public SshTerminalConnection(
        SshConnectionSettings settings,
        string? password,
        string? passphrase,
        IHostKeyVerifier hostKeyVerifier,
        KnownHostsStore knownHosts,
        ILogger log)
    {
        _settings = settings;
        _password = password;
        _passphrase = passphrase;
        _hostKeyVerifier = hostKeyVerifier;
        _knownHosts = knownHosts;
        _log = log;
    }

    public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;
    public event EventHandler<Exception?>? Closed;

    public bool IsConnected => _client?.IsConnected == true && !_closed;

    public async Task ConnectAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        if (_client is not null)
        {
            throw new InvalidOperationException("Already connected.");
        }

        var connectionInfo = new ConnectionInfo(
            _settings.Host,
            _settings.Port <= 0 ? 22 : _settings.Port,
            _settings.Username,
            BuildAuthMethods())
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        var client = new SshClient(connectionInfo);
        if (_settings.KeepAliveSeconds > 0)
        {
            client.KeepAliveInterval = TimeSpan.FromSeconds(_settings.KeepAliveSeconds);
        }

        client.HostKeyReceived += OnHostKeyReceived;

        try
        {
            await Task.Run(() => client.Connect(), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;

        _shell = client.CreateShellStream(
            string.IsNullOrWhiteSpace(_settings.TerminalType) ? "xterm-256color" : _settings.TerminalType,
            (uint)Math.Max(1, columns),
            (uint)Math.Max(1, rows),
            0,
            0,
            8192);

        _shell.ErrorOccurred += (_, e) => MarkClosed(e.Exception);
        _shell.Closed += (_, _) => MarkClosed(null);

        _reader = new Thread(ReadLoop) { IsBackground = true, Name = $"ssh-reader-{_settings.Host}" };
        _reader.Start();

        if (!string.IsNullOrWhiteSpace(_settings.StartupCommand))
        {
            var bytes = Encoding.UTF8.GetBytes(_settings.StartupCommand.TrimEnd() + "\n");
            await WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var shell = _shell;
        if (shell is null || _closed)
        {
            return Task.CompletedTask;
        }

        try
        {
            var array = data.ToArray();
            lock (_writeLock)
            {
                shell.Write(array, 0, array.Length);
                shell.Flush();
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SshException)
        {
            MarkClosed(ex);
        }

        return Task.CompletedTask;
    }

    public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        // SSH.NET's public ShellStream API has no window-change request, so the
        // remote PTY keeps the size it was created with. Tracked for a later
        // SSH.NET upgrade.
        _log.LogTrace("SSH resize to {Cols}x{Rows} ignored (not supported by SSH.NET)", columns, rows);
        return Task.CompletedTask;
    }

    private AuthenticationMethod[] BuildAuthMethods()
    {
        var user = _settings.Username;
        return _settings.AuthMethod switch
        {
            SshAuthMethod.PrivateKey => new AuthenticationMethod[]
            {
                new PrivateKeyAuthenticationMethod(user, LoadKey())
            },
            SshAuthMethod.Agent => throw new NotSupportedException(
                "SSH agent authentication is not available yet. Use a key file or password."),
            _ => new AuthenticationMethod[]
            {
                new PasswordAuthenticationMethod(user, _password ?? string.Empty),
                new KeyboardInteractiveAuthenticationMethod(user)
            }
        };
    }

    private PrivateKeyFile LoadKey()
    {
        if (string.IsNullOrWhiteSpace(_settings.PrivateKeyPath) || !File.Exists(_settings.PrivateKeyPath))
        {
            throw new FileNotFoundException("Private key file not found.", _settings.PrivateKeyPath);
        }

        return string.IsNullOrEmpty(_passphrase)
            ? new PrivateKeyFile(_settings.PrivateKeyPath)
            : new PrivateKeyFile(_settings.PrivateKeyPath, _passphrase);
    }

    private void OnHostKeyReceived(object? sender, HostKeyEventArgs e)
    {
        var host = _settings.Host;
        var port = _settings.Port <= 0 ? 22 : _settings.Port;
        var sha256 = e.FingerPrintSHA256;

        switch (_knownHosts.Check(host, port, sha256))
        {
            case KnownHostsStore.MatchResult.Trusted:
                e.CanTrust = true;
                return;

            case KnownHostsStore.MatchResult.Changed:
                var changedOk = _hostKeyVerifier.Verify(new HostKeyPrompt(
                    host, port, e.HostKeyName, sha256, e.FingerPrintMD5, IsChanged: true));
                if (changedOk)
                {
                    _knownHosts.Trust(host, port, sha256, e.HostKeyName);
                }

                e.CanTrust = changedOk;
                return;

            default:
                var ok = _hostKeyVerifier.Verify(new HostKeyPrompt(
                    host, port, e.HostKeyName, sha256, e.FingerPrintMD5, IsChanged: false));
                if (ok)
                {
                    _knownHosts.Trust(host, port, sha256, e.HostKeyName);
                }

                e.CanTrust = ok;
                return;
        }
    }

    private void ReadLoop()
    {
        var buffer = new byte[8192];
        var shell = _shell!;

        try
        {
            while (!_closed)
            {
                var read = shell.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                DataReceived?.Invoke(this, new ReadOnlyMemory<byte>(buffer, 0, read));
            }

            MarkClosed(null);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SshException)
        {
            MarkClosed(null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "SSH reader loop faulted");
            MarkClosed(ex);
        }
    }

    private void MarkClosed(Exception? error)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        if (error is not null)
        {
            _log.LogWarning(error, "SSH session '{Host}' closed with error", _settings.Host);
        }

        Closed?.Invoke(this, error);
    }

    public async ValueTask DisposeAsync()
    {
        _closed = true;

        try { _shell?.Dispose(); } catch { /* ignore */ }

        var client = _client;
        _client = null;
        if (client is not null)
        {
            client.HostKeyReceived -= OnHostKeyReceived;
            try
            {
                if (client.IsConnected)
                {
                    client.Disconnect();
                }
            }
            catch { /* ignore */ }

            client.Dispose();
        }

        if (_reader is { IsAlive: true })
        {
            await Task.Run(() => _reader.Join(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
        }
    }
}
