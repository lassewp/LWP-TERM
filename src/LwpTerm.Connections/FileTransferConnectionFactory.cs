using System;
using LwpTerm.Connections.Ftp;
using LwpTerm.Connections.Sftp;
using LwpTerm.Core.Security;
using LwpTerm.Core.Sessions;
using LwpTerm.Core.Transfer;
using Microsoft.Extensions.Logging;

namespace LwpTerm.Connections;

public interface IFileTransferConnectionFactory
{
    bool Supports(ProtocolType protocol);

    IFileTransferConnection Create(ConnectionSettings settings);

    /// <summary>Build an SFTP connection that reuses an SSH session's host and credentials.</summary>
    IFileTransferConnection CreateSftpForSsh(SshConnectionSettings ssh);
}

public sealed class FileTransferConnectionFactory : IFileTransferConnectionFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ICredentialStore _credentials;
    private readonly IHostKeyVerifier _hostKeyVerifier;
    private readonly KnownHostsStore _knownHosts;

    public FileTransferConnectionFactory(
        ILoggerFactory loggerFactory,
        ICredentialStore credentials,
        IHostKeyVerifier hostKeyVerifier,
        KnownHostsStore knownHosts)
    {
        _loggerFactory = loggerFactory;
        _credentials = credentials;
        _hostKeyVerifier = hostKeyVerifier;
        _knownHosts = knownHosts;
    }

    public bool Supports(ProtocolType protocol) => protocol is ProtocolType.Sftp or ProtocolType.Ftp;

    public IFileTransferConnection Create(ConnectionSettings settings) => settings switch
    {
        SftpConnectionSettings sftp => BuildSftp(sftp),
        FtpConnectionSettings ftp => new FtpFileTransferConnection(
            ftp, Reveal(ftp.ProtectedPassword), _loggerFactory.CreateLogger<FtpFileTransferConnection>()),
        _ => throw new NotSupportedException($"No file transport for '{settings.Protocol}'.")
    };

    public IFileTransferConnection CreateSftpForSsh(SshConnectionSettings ssh)
    {
        var sftp = new SftpConnectionSettings
        {
            Host = ssh.Host,
            Port = ssh.Port,
            Username = ssh.Username,
            AuthMethod = ssh.AuthMethod,
            PrivateKeyPath = ssh.PrivateKeyPath,
            ProtectedPassword = ssh.ProtectedPassword,
            ProtectedPassphrase = ssh.ProtectedPassphrase,
            InitialRemotePath = "."
        };
        return BuildSftp(sftp);
    }

    private SftpFileTransferConnection BuildSftp(SftpConnectionSettings sftp) => new(
        sftp,
        Reveal(sftp.ProtectedPassword),
        Reveal(sftp.ProtectedPassphrase),
        _hostKeyVerifier,
        _knownHosts,
        _loggerFactory.CreateLogger<SftpFileTransferConnection>());

    private string? Reveal(string? blob) =>
        string.IsNullOrEmpty(blob) ? null : _credentials.Unprotect(blob);
}
