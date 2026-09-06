using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LwpTerm.Core.Transfer;

public readonly record struct TransferProgress(long BytesTransferred, long TotalBytes)
{
    public double Fraction => TotalBytes > 0 ? Math.Clamp((double)BytesTransferred / TotalBytes, 0, 1) : 0;
}

public sealed record RemoteEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long Size,
    DateTimeOffset Modified,
    string? Permissions);

/// <summary>
/// A remote file store the browser can navigate and transfer against: SFTP over
/// an SSH connection, or FTP/FTPS. Paths are always forward-slash and rooted.
/// </summary>
public interface IFileTransferConnection : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>Connect and return the directory to open first (home, or a configured start path).</summary>
    Task<string> ConnectAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken cancellationToken = default);

    Task DownloadAsync(string remotePath, string localPath, IProgress<TransferProgress>? progress, CancellationToken cancellationToken = default);

    Task UploadAsync(string localPath, string remotePath, IProgress<TransferProgress>? progress, CancellationToken cancellationToken = default);

    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Delete a file, or a directory and its contents.</summary>
    Task DeleteAsync(RemoteEntry entry, CancellationToken cancellationToken = default);

    Task RenameAsync(string fromPath, string toPath, CancellationToken cancellationToken = default);

    string Combine(string directory, string name);

    string Parent(string path);
}

/// <summary>Path helpers for POSIX-style ("/"-separated) remote file systems.</summary>
public static class PosixPath
{
    public static string Combine(string directory, string name)
    {
        if (string.IsNullOrEmpty(directory) || directory == "/")
        {
            return "/" + name.TrimStart('/');
        }

        return directory.TrimEnd('/') + "/" + name.TrimStart('/');
    }

    public static string Parent(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return "/";
        }

        var trimmed = path.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash <= 0 ? "/" : trimmed[..slash];
    }

    public static string Name(string path)
    {
        var trimmed = path.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }
}
