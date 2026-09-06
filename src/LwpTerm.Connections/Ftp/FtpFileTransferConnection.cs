using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using LwpTerm.Core.Transfer;
using Microsoft.Extensions.Logging;
using CoreFtp = LwpTerm.Core.Sessions;

namespace LwpTerm.Connections.Ftp;

/// <summary><see cref="IFileTransferConnection"/> backed by FluentFTP's async client (FTP / FTPS).</summary>
public sealed class FtpFileTransferConnection : IFileTransferConnection
{
    private readonly CoreFtp.FtpConnectionSettings _settings;
    private readonly string? _password;
    private readonly ILogger _log;

    private AsyncFtpClient? _client;

    public FtpFileTransferConnection(CoreFtp.FtpConnectionSettings settings, string? password, ILogger log)
    {
        _settings = settings;
        _password = password;
        _log = log;
    }

    public bool IsConnected => _client?.IsConnected == true;

    public async Task<string> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null)
        {
            throw new InvalidOperationException("Already connected.");
        }

        var port = _settings.Port <= 0 ? 21 : _settings.Port;
        var client = new AsyncFtpClient(_settings.Host, _settings.Username, _password ?? string.Empty, port);

        client.Config.EncryptionMode = _settings.EncryptionMode switch
        {
            CoreFtp.FtpEncryptionMode.Explicit => FtpEncryptionMode.Explicit,
            CoreFtp.FtpEncryptionMode.Implicit => FtpEncryptionMode.Implicit,
            _ => FtpEncryptionMode.None
        };
        client.Config.DataConnectionType = _settings.DataConnectionMode == CoreFtp.FtpDataConnectionMode.Active
            ? FtpDataConnectionType.AutoActive
            : FtpDataConnectionType.AutoPassive;
        client.Config.ValidateAnyCertificate = true;
        client.Config.ConnectTimeout = 20_000;

        try
        {
            await client.Connect(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _client = client;

        var start = string.IsNullOrWhiteSpace(_settings.InitialRemotePath) ? "/" : _settings.InitialRemotePath;
        return start;
    }

    public async Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
    {
        var client = Require();
        var items = await client.GetListing(path, cancellationToken).ConfigureAwait(false);
        var entries = new List<RemoteEntry>(items.Length);

        foreach (var item in items)
        {
            if (item.Name is "." or "..")
            {
                continue;
            }

            var isDir = item.Type == FtpObjectType.Directory;
            entries.Add(new RemoteEntry(
                item.Name,
                item.FullName,
                isDir,
                isDir || item.Size < 0 ? 0 : item.Size,
                item.Modified == default ? DateTimeOffset.MinValue : new DateTimeOffset(item.Modified),
                string.IsNullOrEmpty(item.RawPermissions) ? null : item.RawPermissions));
        }

        return entries;
    }

    public async Task DownloadAsync(string remotePath, string localPath, IProgress<TransferProgress>? progress, CancellationToken cancellationToken = default)
    {
        var client = Require();
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

        var reporter = progress is null
            ? null
            : new Progress<FtpProgress>(p => progress.Report(new TransferProgress(p.TransferredBytes, TotalFrom(p))));

        var status = await client.DownloadFile(
            localPath, remotePath, FtpLocalExists.Overwrite, FtpVerify.None, reporter, cancellationToken).ConfigureAwait(false);

        if (status == FtpStatus.Failed)
        {
            throw new IOException($"FTP download of '{remotePath}' failed.");
        }
    }

    public async Task UploadAsync(string localPath, string remotePath, IProgress<TransferProgress>? progress, CancellationToken cancellationToken = default)
    {
        var client = Require();

        var reporter = progress is null
            ? null
            : new Progress<FtpProgress>(p => progress.Report(new TransferProgress(p.TransferredBytes, TotalFrom(p))));

        var status = await client.UploadFile(
            localPath, remotePath, FtpRemoteExists.Overwrite, createRemoteDir: true, FtpVerify.None, reporter, cancellationToken)
            .ConfigureAwait(false);

        if (status == FtpStatus.Failed)
        {
            throw new IOException($"FTP upload to '{remotePath}' failed.");
        }
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
        Require().CreateDirectory(path, cancellationToken);

    public async Task DeleteAsync(RemoteEntry entry, CancellationToken cancellationToken = default)
    {
        var client = Require();
        if (entry.IsDirectory)
        {
            await client.DeleteDirectory(entry.FullPath, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await client.DeleteFile(entry.FullPath, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task RenameAsync(string fromPath, string toPath, CancellationToken cancellationToken = default) =>
        Require().Rename(fromPath, toPath, cancellationToken);

    public string Combine(string directory, string name) => PosixPath.Combine(directory, name);

    public string Parent(string path) => PosixPath.Parent(path);

    private static long TotalFrom(FtpProgress p)
    {
        if (p.Progress > 0 && p.TransferredBytes > 0)
        {
            return (long)Math.Round(p.TransferredBytes / (p.Progress / 100.0));
        }

        return 0;
    }

    private AsyncFtpClient Require() =>
        _client ?? throw new InvalidOperationException("FTP is not connected.");

    public async ValueTask DisposeAsync()
    {
        var client = _client;
        _client = null;
        if (client is not null)
        {
            try
            {
                await client.Disconnect().ConfigureAwait(false);
            }
            catch { /* ignore */ }

            await client.DisposeAsync().ConfigureAwait(false);
        }
    }
}
