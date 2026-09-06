using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LwpTerm.Core.Security;
using LwpTerm.Core.Sessions;
using LwpTerm.Core.Transfer;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace LwpTerm.Connections.Sftp;

/// <summary>
/// <see cref="IFileTransferConnection"/> backed by SSH.NET's <see cref="SftpClient"/>.
/// Host-key handling mirrors <c>SshTerminalConnection</c>.
/// </summary>
public sealed class SftpFileTransferConnection : IFileTransferConnection
{
    private readonly SftpConnectionSettings _settings;
    private readonly string? _password;
    private readonly string? _passphrase;
    private readonly IHostKeyVerifier _hostKeyVerifier;
    private readonly KnownHostsStore _knownHosts;
    private readonly ILogger _log;

    private SftpClient? _client;

    public SftpFileTransferConnection(
        SftpConnectionSettings settings,
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

    public bool IsConnected => _client?.IsConnected == true;

    public async Task<string> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null)
        {
            throw new InvalidOperationException("Already connected.");
        }

        var port = _settings.Port <= 0 ? 22 : _settings.Port;
        var connectionInfo = new ConnectionInfo(_settings.Host, port, _settings.Username, BuildAuthMethods())
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        var client = new SftpClient(connectionInfo);
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

        var start = string.IsNullOrWhiteSpace(_settings.InitialRemotePath) || _settings.InitialRemotePath == "."
            ? client.WorkingDirectory
            : _settings.InitialRemotePath;

        return string.IsNullOrEmpty(start) ? "/" : start;
    }

    public async Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
    {
        var client = Require();
        var entries = new List<RemoteEntry>();

        await foreach (var file in client.ListDirectoryAsync(path, cancellationToken).ConfigureAwait(false))
        {
            if (file.Name is "." or "..")
            {
                continue;
            }

            entries.Add(new RemoteEntry(
                file.Name,
                file.FullName,
                file.IsDirectory,
                file.IsDirectory ? 0 : file.Length,
                new DateTimeOffset(DateTime.SpecifyKind(file.LastWriteTime, DateTimeKind.Local)),
                DescribePermissions(file)));
        }

        return entries;
    }

    public async Task DownloadAsync(string remotePath, string localPath, IProgress<TransferProgress>? progress, CancellationToken cancellationToken = default)
    {
        var client = Require();
        var total = SafeSize(() => client.GetAttributes(remotePath).Size);

        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await using var output = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var reporter = progress is null
            ? null
            : new Progress<DownloadFileProgressReport>(r => progress.Report(new TransferProgress((long)r.TotalBytesDownloaded, total)));
        await client.DownloadFileAsync(remotePath, output, reporter, cancellationToken).ConfigureAwait(false);
        progress?.Report(new TransferProgress(total, total));
    }

    public async Task UploadAsync(string localPath, string remotePath, IProgress<TransferProgress>? progress, CancellationToken cancellationToken = default)
    {
        var client = Require();
        var total = new FileInfo(localPath).Length;

        await using var input = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var reporter = progress is null
            ? null
            : new Progress<UploadFileProgressReport>(r => progress.Report(new TransferProgress((long)r.TotalBytesUploaded, total)));
        await client.UploadFileAsync(input, remotePath, canOverride: true, reporter, cancellationToken).ConfigureAwait(false);
        progress?.Report(new TransferProgress(total, total));
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
        Require().CreateDirectoryAsync(path, cancellationToken);

    public async Task DeleteAsync(RemoteEntry entry, CancellationToken cancellationToken = default)
    {
        var client = Require();
        if (!entry.IsDirectory)
        {
            await client.DeleteFileAsync(entry.FullPath, cancellationToken).ConfigureAwait(false);
            return;
        }

        await DeleteDirectoryRecursiveAsync(client, entry.FullPath, cancellationToken).ConfigureAwait(false);
    }

    public Task RenameAsync(string fromPath, string toPath, CancellationToken cancellationToken = default) =>
        Require().RenameFileAsync(fromPath, toPath, cancellationToken);

    public string Combine(string directory, string name) => PosixPath.Combine(directory, name);

    public string Parent(string path) => PosixPath.Parent(path);

    private static async Task DeleteDirectoryRecursiveAsync(SftpClient client, string path, CancellationToken ct)
    {
        await foreach (var file in client.ListDirectoryAsync(path, ct).ConfigureAwait(false))
        {
            if (file.Name is "." or "..")
            {
                continue;
            }

            if (file.IsDirectory)
            {
                await DeleteDirectoryRecursiveAsync(client, file.FullName, ct).ConfigureAwait(false);
            }
            else
            {
                await client.DeleteFileAsync(file.FullName, ct).ConfigureAwait(false);
            }
        }

        await client.DeleteDirectoryAsync(path, ct).ConfigureAwait(false);
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
                e.CanTrust = TrustPrompt(host, port, e, isChanged: true);
                return;
            default:
                e.CanTrust = TrustPrompt(host, port, e, isChanged: false);
                return;
        }
    }

    private bool TrustPrompt(string host, int port, HostKeyEventArgs e, bool isChanged)
    {
        var ok = _hostKeyVerifier.Verify(new HostKeyPrompt(
            host, port, e.HostKeyName, e.FingerPrintSHA256, e.FingerPrintMD5, isChanged));
        if (ok)
        {
            _knownHosts.Trust(host, port, e.FingerPrintSHA256, e.HostKeyName);
        }

        return ok;
    }

    private static string DescribePermissions(ISftpFile f)
    {
        Span<char> p = stackalloc char[10];
        p[0] = f.IsDirectory ? 'd' : (f.IsSymbolicLink ? 'l' : '-');
        p[1] = f.OwnerCanRead ? 'r' : '-';
        p[2] = f.OwnerCanWrite ? 'w' : '-';
        p[3] = f.OwnerCanExecute ? 'x' : '-';
        p[4] = f.GroupCanRead ? 'r' : '-';
        p[5] = f.GroupCanWrite ? 'w' : '-';
        p[6] = f.GroupCanExecute ? 'x' : '-';
        p[7] = f.OthersCanRead ? 'r' : '-';
        p[8] = f.OthersCanWrite ? 'w' : '-';
        p[9] = f.OthersCanExecute ? 'x' : '-';
        return new string(p);
    }

    private static long SafeSize(Func<long> get)
    {
        try
        {
            return get();
        }
        catch
        {
            return 0;
        }
    }

    private SftpClient Require() =>
        _client ?? throw new InvalidOperationException("SFTP is not connected.");

    public async ValueTask DisposeAsync()
    {
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

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
