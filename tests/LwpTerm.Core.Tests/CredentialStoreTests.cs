using System;
using System.IO;
using FluentAssertions;
using LwpTerm.Core;
using LwpTerm.Core.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace LwpTerm.Core.Tests;

public class CredentialStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly AppPaths _paths;

    public CredentialStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lwpterm-cred-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _paths = AppPaths.Portable(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private CredentialStore NewStore() => new(_paths, NullLogger<CredentialStore>.Instance);

    [Fact]
    public void Dpapi_mode_round_trips_a_secret()
    {
        var store = NewStore();
        store.MasterPasswordEnabled.Should().BeFalse();
        store.IsLocked.Should().BeFalse();

        var blob = store.Protect("hunter2");
        blob.Should().StartWith("v1.dpapi.");
        store.Unprotect(blob).Should().Be("hunter2");
    }

    [Fact]
    public void Master_password_mode_round_trips_and_persists_across_instances()
    {
        var first = NewStore();
        first.EnableMasterPassword("correct horse battery staple");
        var blob = first.Protect("s3cr3t");
        blob.Should().StartWith("v1.mk.");

        // A fresh instance sees the vault file and starts locked.
        var second = NewStore();
        second.MasterPasswordEnabled.Should().BeTrue();
        second.IsLocked.Should().BeTrue();
        Assert.Throws<VaultLockedException>(() => second.Unprotect(blob));

        second.Unlock("correct horse battery staple");
        second.IsLocked.Should().BeFalse();
        second.Unprotect(blob).Should().Be("s3cr3t");
    }

    [Fact]
    public void Wrong_master_password_is_rejected()
    {
        NewStore().EnableMasterPassword("right-one");

        var store = NewStore();
        Assert.Throws<InvalidMasterPasswordException>(() => store.Unlock("wrong-one"));
        store.IsLocked.Should().BeTrue();
    }

    [Fact]
    public void Dpapi_blobs_still_decrypt_after_master_password_is_enabled()
    {
        var store = NewStore();
        var legacy = store.Protect("legacy-value");

        store.EnableMasterPassword("new-master");
        store.Unprotect(legacy).Should().Be("legacy-value");

        var fresh = store.Protect("new-value");
        fresh.Should().StartWith("v1.mk.");
        store.Unprotect(fresh).Should().Be("new-value");
    }

    [Fact]
    public void Disable_master_password_removes_the_vault()
    {
        NewStore().EnableMasterPassword("temp");

        var store = NewStore();
        store.DisableMasterPassword("temp");
        store.MasterPasswordEnabled.Should().BeFalse();

        NewStore().MasterPasswordEnabled.Should().BeFalse();
    }
}
