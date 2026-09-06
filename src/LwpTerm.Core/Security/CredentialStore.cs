using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LwpTerm.Core;
using Microsoft.Extensions.Logging;

namespace LwpTerm.Core.Security;

public sealed class VaultLockedException : InvalidOperationException
{
    public VaultLockedException() : base("The credential vault is locked. Enter the master password first.") { }
}

public sealed class InvalidMasterPasswordException : Exception
{
    public InvalidMasterPasswordException() : base("The master password is incorrect.") { }
}

public interface ICredentialStore
{
    /// <summary>True when a master password has been configured for this profile.</summary>
    bool MasterPasswordEnabled { get; }

    /// <summary>True when a master password is configured but has not been supplied yet.</summary>
    bool IsLocked { get; }

    /// <summary>Verify and cache the master password key. Throws <see cref="InvalidMasterPasswordException"/>.</summary>
    void Unlock(string masterPassword);

    /// <summary>Configure a master password for the first time (no existing secrets are re-encrypted here).</summary>
    void EnableMasterPassword(string newPassword);

    /// <summary>Remove the master password requirement. Requires the current password.</summary>
    void DisableMasterPassword(string currentPassword);

    /// <summary>Encrypt a secret to an opaque, self-describing blob.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypt a blob produced by <see cref="Protect"/> (or a legacy DPAPI blob).</summary>
    string Unprotect(string blob);
}

/// <summary>
/// Two-mode secret protection:
/// <list type="bullet">
/// <item>Default: Windows DPAPI (per-user). Blobs are tagged <c>v1.dpapi.*</c>.</item>
/// <item>Master password: PBKDF2-SHA256 (200k) → AES-256-GCM. Blobs are tagged <c>v1.mk.*</c>.</item>
/// </list>
/// A <c>vault.json</c> next to the sessions file holds the KDF salt and a verifier
/// so the entered password can be checked without storing it. Blobs are tag-routed
/// on decrypt, so DPAPI secrets keep working after a master password is added
/// (until they are rewritten).
/// </summary>
public sealed class CredentialStore : ICredentialStore
{
    private const int Pbkdf2Iterations = 200_000;
    private const int KeyBytes = 32;
    private const int SaltBytes = 16;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const string DpapiPrefix = "v1.dpapi.";
    private const string MasterKeyPrefix = "v1.mk.";
    private static readonly byte[] VerifierPlaintext = Encoding.UTF8.GetBytes("LWP-TERM-VAULT-VERIFIER-v1");
    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("LWP-TERM/DPAPI/v1");

    private readonly string _vaultPath;
    private readonly ILogger<CredentialStore> _log;

    private VaultInfo? _vault;
    private byte[]? _masterKey;

    public CredentialStore(AppPaths paths, ILogger<CredentialStore> log)
    {
        _vaultPath = Path.Combine(paths.Root, "vault.json");
        _log = log;

        if (File.Exists(_vaultPath))
        {
            _vault = JsonSerializer.Deserialize<VaultInfo>(File.ReadAllText(_vaultPath))
                     ?? throw new InvalidDataException("vault.json is unreadable.");
        }
    }

    public bool MasterPasswordEnabled => _vault is not null;

    public bool IsLocked => MasterPasswordEnabled && _masterKey is null;

    public void Unlock(string masterPassword)
    {
        if (_vault is null)
        {
            return; // nothing to unlock
        }

        var key = DeriveKey(masterPassword, _vault.Salt);
        if (!TryDecryptGcm(key, _vault.VerifierNonce, _vault.VerifierCipher, _vault.VerifierTag, out var plain)
            || !CryptographicOperations.FixedTimeEquals(plain, VerifierPlaintext))
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidMasterPasswordException();
        }

        _masterKey = key;
        _log.LogInformation("Credential vault unlocked");
    }

    public void EnableMasterPassword(string newPassword)
    {
        if (_vault is not null)
        {
            throw new InvalidOperationException("A master password is already configured.");
        }

        if (string.IsNullOrEmpty(newPassword))
        {
            throw new ArgumentException("Master password must not be empty.", nameof(newPassword));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = DeriveKey(newPassword, salt);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var cipher = new byte[VerifierPlaintext.Length];
        var tag = new byte[TagBytes];
        using (var gcm = new AesGcm(key, TagBytes))
        {
            gcm.Encrypt(nonce, VerifierPlaintext, cipher, tag);
        }

        _vault = new VaultInfo
        {
            Version = 1,
            Salt = salt,
            VerifierNonce = nonce,
            VerifierCipher = cipher,
            VerifierTag = tag
        };
        File.WriteAllText(_vaultPath, JsonSerializer.Serialize(_vault));
        _masterKey = key;
        _log.LogInformation("Master password enabled");
    }

    public void DisableMasterPassword(string currentPassword)
    {
        if (_vault is null)
        {
            return;
        }

        Unlock(currentPassword);
        File.Delete(_vaultPath);
        _vault = null;
        if (_masterKey is not null)
        {
            CryptographicOperations.ZeroMemory(_masterKey);
            _masterKey = null;
        }

        _log.LogInformation("Master password disabled");
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var bytes = Encoding.UTF8.GetBytes(plaintext);

        if (MasterPasswordEnabled)
        {
            if (_masterKey is null)
            {
                throw new VaultLockedException();
            }

            var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
            var cipher = new byte[bytes.Length];
            var tag = new byte[TagBytes];
            using var gcm = new AesGcm(_masterKey, TagBytes);
            gcm.Encrypt(nonce, bytes, cipher, tag);

            var packed = new byte[NonceBytes + TagBytes + cipher.Length];
            Buffer.BlockCopy(nonce, 0, packed, 0, NonceBytes);
            Buffer.BlockCopy(tag, 0, packed, NonceBytes, TagBytes);
            Buffer.BlockCopy(cipher, 0, packed, NonceBytes + TagBytes, cipher.Length);
            return MasterKeyPrefix + Convert.ToBase64String(packed);
        }

        var protectedBytes = ProtectedData.Protect(bytes, DpapiEntropy, DataProtectionScope.CurrentUser);
        return DpapiPrefix + Convert.ToBase64String(protectedBytes);
    }

    public string Unprotect(string blob)
    {
        ArgumentException.ThrowIfNullOrEmpty(blob);

        if (blob.StartsWith(DpapiPrefix, StringComparison.Ordinal))
        {
            var raw = Convert.FromBase64String(blob[DpapiPrefix.Length..]);
            var plain = ProtectedData.Unprotect(raw, DpapiEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }

        if (blob.StartsWith(MasterKeyPrefix, StringComparison.Ordinal))
        {
            if (_masterKey is null)
            {
                throw new VaultLockedException();
            }

            var packed = Convert.FromBase64String(blob[MasterKeyPrefix.Length..]);
            var nonce = packed[..NonceBytes];
            var tag = packed[NonceBytes..(NonceBytes + TagBytes)];
            var cipher = packed[(NonceBytes + TagBytes)..];
            if (!TryDecryptGcm(_masterKey, nonce, cipher, tag, out var plain))
            {
                throw new CryptographicException("Secret could not be decrypted with the current master password.");
            }

            return Encoding.UTF8.GetString(plain);
        }

        throw new FormatException($"Unrecognised secret blob format: '{blob[..Math.Min(12, blob.Length)]}…'");
    }

    private static byte[] DeriveKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeyBytes);

    private static bool TryDecryptGcm(byte[] key, byte[] nonce, byte[] cipher, byte[] tag, out byte[] plaintext)
    {
        plaintext = new byte[cipher.Length];
        try
        {
            using var gcm = new AesGcm(key, TagBytes);
            gcm.Decrypt(nonce, cipher, tag, plaintext);
            return true;
        }
        catch (CryptographicException)
        {
            plaintext = Array.Empty<byte>();
            return false;
        }
    }

    private sealed class VaultInfo
    {
        public int Version { get; set; }
        public byte[] Salt { get; set; } = Array.Empty<byte>();
        public byte[] VerifierNonce { get; set; } = Array.Empty<byte>();
        public byte[] VerifierCipher { get; set; } = Array.Empty<byte>();
        public byte[] VerifierTag { get; set; } = Array.Empty<byte>();
    }
}
