using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LwpTerm.Core;
using LwpTerm.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace LwpTerm.Core.Tests;

public class SessionStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly AppPaths _paths;

    public SessionStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lwpterm-sess-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _paths = AppPaths.Portable(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private JsonSessionStore NewStore() => new(_paths, NullLogger<JsonSessionStore>.Instance);

    [Fact]
    public async Task Missing_file_yields_a_seed_tree()
    {
        var tree = await NewStore().LoadAsync();
        tree.Roots.Should().ContainSingle().Which.Should().BeOfType<SessionFolder>();
    }

    [Fact]
    public async Task Nested_folders_and_every_protocol_round_trip()
    {
        var tree = new SessionTree();
        var prod = new SessionFolder { Name = "Prod" };
        var nested = new SessionFolder { Name = "DB" };
        prod.Children.Add(nested);
        tree.Roots.Add(prod);

        foreach (var protocol in Enum.GetValues<ProtocolType>())
        {
            nested.Children.Add(new SessionItem
            {
                Name = protocol + " box",
                Protocol = protocol,
                Settings = ConnectionSettings.CreateDefault(protocol)
            });
        }

        var store = NewStore();
        await store.SaveAsync(tree);
        var reloaded = await store.LoadAsync();

        var reloadedNested = ((SessionFolder)reloaded.Roots.Single()).Children.OfType<SessionFolder>().Single();
        reloadedNested.Children.Should().HaveCount(Enum.GetValues<ProtocolType>().Length);
        reloadedNested.Children.OfType<SessionItem>()
            .Select(i => i.Settings.Protocol)
            .Should().BeEquivalentTo(Enum.GetValues<ProtocolType>());
    }

    [Fact]
    public async Task Ssh_settings_survive_a_round_trip_with_values()
    {
        var item = new SessionItem
        {
            Name = "web",
            Protocol = ProtocolType.Ssh,
            Settings = new SshConnectionSettings
            {
                Host = "web01.example.com",
                Port = 2222,
                Username = "deploy",
                AuthMethod = SshAuthMethod.PrivateKey,
                PrivateKeyPath = @"C:\keys\id_ed25519",
                ProtectedPassphrase = "v1.dpapi.AAAA",
                StartupCommand = "tmux attach",
                Environment = { ["LANG"] = "en_US.UTF-8" }
            }
        };
        var tree = new SessionTree { Roots = { item } };

        var store = NewStore();
        await store.SaveAsync(tree);
        var reloaded = await store.LoadAsync();

        var ssh = reloaded.Roots.OfType<SessionItem>().Single().Settings.Should().BeOfType<SshConnectionSettings>().Subject;
        ssh.Host.Should().Be("web01.example.com");
        ssh.Port.Should().Be(2222);
        ssh.AuthMethod.Should().Be(SshAuthMethod.PrivateKey);
        ssh.PrivateKeyPath.Should().Be(@"C:\keys\id_ed25519");
        ssh.Environment["LANG"].Should().Be("en_US.UTF-8");
    }

    [Fact]
    public async Task Folder_appearance_fields_round_trip()
    {
        var tree = new SessionTree
        {
            Roots =
            {
                new SessionFolder
                {
                    Name = "Prod",
                    ColorHex = "#FF8800",
                    IconGlyph = "",
                    LabelWeight = FolderLabelWeight.Bold,
                    Notes = "critical",
                    IsExpanded = false
                }
            }
        };

        var store = NewStore();
        await store.SaveAsync(tree);
        var folder = (SessionFolder)(await store.LoadAsync()).Roots.Single();

        folder.ColorHex.Should().Be("#FF8800");
        folder.IconGlyph.Should().Be("");
        folder.LabelWeight.Should().Be(FolderLabelWeight.Bold);
        folder.Notes.Should().Be("critical");
        folder.IsExpanded.Should().BeFalse();
    }

    [Fact]
    public async Task Old_folders_without_appearance_fields_load_with_defaults()
    {
        await System.IO.File.WriteAllTextAsync(_paths.SessionsFile,
            """{"version":1,"roots":[{"node":"folder","name":"Legacy","children":[]}]}""");

        var folder = (SessionFolder)(await NewStore().LoadAsync()).Roots.Single();

        folder.Name.Should().Be("Legacy");
        folder.LabelWeight.Should().Be(FolderLabelWeight.Auto);
        folder.ColorHex.Should().BeNull();
        folder.IconGlyph.Should().BeNull();
    }

    [Fact]
    public async Task Save_is_atomic_and_leaves_no_tmp_file()
    {
        var store = NewStore();
        await store.SaveAsync(SessionTree.CreateSeed());
        await store.SaveAsync(SessionTree.CreateSeed());

        File.Exists(_paths.SessionsFile).Should().BeTrue();
        File.Exists(_paths.SessionsFile + ".tmp").Should().BeFalse();
    }
}
