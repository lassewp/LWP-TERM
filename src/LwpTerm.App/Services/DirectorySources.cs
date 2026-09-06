using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LwpTerm.Core.Transfer;

namespace LwpTerm.App.Services;

public sealed record FileNode(
    string Name,
    string FullPath,
    bool IsDirectory,
    long Size,
    DateTimeOffset Modified,
    string? Permissions);

/// <summary>One side of the dual-pane browser: the local disk, or a remote store.</summary>
public interface IDirectorySource
{
    string Kind { get; }
    bool IsRemote { get; }

    Task<string> InitializeAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<FileNode>> ListAsync(string path, CancellationToken cancellationToken);
    string Combine(string directory, string name);
    string Parent(string path);
    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken);
    Task DeleteAsync(FileNode node, CancellationToken cancellationToken);
    Task RenameAsync(FileNode node, string newName, CancellationToken cancellationToken);
}

public sealed class LocalDirectorySource : IDirectorySource
{
    public string Kind => "Local";
    public bool IsRemote => false;

    public Task<string> InitializeAsync(CancellationToken cancellationToken)
    {
        var start = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(start) || !Directory.Exists(start))
        {
            start = Path.GetPathRoot(Environment.CurrentDirectory) ?? @"C:\";
        }

        return Task.FromResult(start);
    }

    public Task<IReadOnlyList<FileNode>> ListAsync(string path, CancellationToken cancellationToken)
    {
        var dir = new DirectoryInfo(path);
        var nodes = new List<FileNode>();

        foreach (var info in dir.EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var isDir = (info.Attributes & FileAttributes.Directory) != 0;
                if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                {
                    continue;
                }

                nodes.Add(new FileNode(
                    info.Name,
                    info.FullName,
                    isDir,
                    isDir ? 0 : ((FileInfo)info).Length,
                    info.LastWriteTime,
                    null));
            }
            catch (IOException)
            {
                // unreadable entry — skip
            }
        }

        return Task.FromResult<IReadOnlyList<FileNode>>(nodes);
    }

    public string Combine(string directory, string name) => Path.Combine(directory, name);

    public string Parent(string path) => Directory.GetParent(path)?.FullName ?? path;

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(FileNode node, CancellationToken cancellationToken)
    {
        if (node.IsDirectory)
        {
            Directory.Delete(node.FullPath, recursive: true);
        }
        else
        {
            File.Delete(node.FullPath);
        }

        return Task.CompletedTask;
    }

    public Task RenameAsync(FileNode node, string newName, CancellationToken cancellationToken)
    {
        var target = Path.Combine(Path.GetDirectoryName(node.FullPath)!, newName);
        if (node.IsDirectory)
        {
            Directory.Move(node.FullPath, target);
        }
        else
        {
            File.Move(node.FullPath, target);
        }

        return Task.CompletedTask;
    }
}

public sealed class RemoteDirectorySource : IDirectorySource
{
    private readonly IFileTransferConnection _connection;

    public RemoteDirectorySource(IFileTransferConnection connection, string kind)
    {
        _connection = connection;
        Kind = kind;
    }

    public string Kind { get; }
    public bool IsRemote => true;

    public IFileTransferConnection Connection => _connection;

    public Task<string> InitializeAsync(CancellationToken cancellationToken) =>
        _connection.ConnectAsync(cancellationToken);

    public async Task<IReadOnlyList<FileNode>> ListAsync(string path, CancellationToken cancellationToken)
    {
        var entries = await _connection.ListAsync(path, cancellationToken).ConfigureAwait(false);
        return entries
            .Select(e => new FileNode(e.Name, e.FullPath, e.IsDirectory, e.Size, e.Modified, e.Permissions))
            .ToList();
    }

    public string Combine(string directory, string name) => _connection.Combine(directory, name);

    public string Parent(string path) => _connection.Parent(path);

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken) =>
        _connection.CreateDirectoryAsync(path, cancellationToken);

    public Task DeleteAsync(FileNode node, CancellationToken cancellationToken) =>
        _connection.DeleteAsync(
            new RemoteEntry(node.Name, node.FullPath, node.IsDirectory, node.Size, node.Modified, node.Permissions),
            cancellationToken);

    public Task RenameAsync(FileNode node, string newName, CancellationToken cancellationToken)
    {
        var target = _connection.Combine(_connection.Parent(node.FullPath), newName);
        return _connection.RenameAsync(node.FullPath, target, cancellationToken);
    }
}
