using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LwpTerm.App.ViewModels.Tabs;
using LwpTerm.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;

namespace LwpTerm.App.Views.Tabs;

public partial class TerminalView : UserControl
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private TerminalTabViewModel? _vm;
    private bool _webReady;

    public TerminalView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.Output -= OnVmOutput;
            _vm.Notice -= OnVmNotice;
        }

        _vm = e.NewValue as TerminalTabViewModel;

        if (_vm is not null)
        {
            _vm.Output += OnVmOutput;
            _vm.Notice += OnVmNotice;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_webReady)
        {
            return;
        }

        try
        {
            var paths = App.Services.GetRequiredService<AppPaths>();
            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(paths.Root, "webview2"));
            await Web.EnsureCoreWebView2Async(env);

            var core = Web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;

            var assetDir = Path.Combine(AppContext.BaseDirectory, "Assets", "webterm");
            core.SetVirtualHostNameToFolderMapping(
                "webterm", assetDir, CoreWebView2HostResourceAccessKind.Allow);

            core.WebMessageReceived += OnWebMessage;
            Web.NavigationCompleted += (_, _) => LoadingText.Visibility = Visibility.Collapsed;

            _webReady = true;
            core.Navigate("https://webterm/index.html");
        }
        catch (Exception ex)
        {
            LoadingText.Text = "WebView2 failed to start:\n" + ex.Message +
                               "\n\nInstall the Microsoft Edge WebView2 Runtime.";
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.Output -= OnVmOutput;
            _vm.Notice -= OnVmNotice;
        }

        try
        {
            Web.Dispose();
        }
        catch
        {
            // ignore
        }
    }

    // ---- page -> host --------------------------------------------------

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        InboundMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<InboundMessage>(e.WebMessageAsJson, JsonOpts);
        }
        catch
        {
            return;
        }

        if (msg is null)
        {
            return;
        }

        switch (msg.Type)
        {
            case "ready":
                _vm.OnTerminalReady(Math.Max(1, msg.Cols), Math.Max(1, msg.Rows));
                break;
            case "input":
                if (msg.Data is not null)
                {
                    _vm.OnTerminalInput(Convert.FromBase64String(msg.Data));
                }
                break;
            case "resize":
                _vm.OnTerminalResize(Math.Max(1, msg.Cols), Math.Max(1, msg.Rows));
                break;
            case "bell":
                SystemSounds_Beep();
                break;
        }
    }

    private static void SystemSounds_Beep()
    {
        try
        {
            System.Media.SystemSounds.Beep.Play();
        }
        catch
        {
            // ignore
        }
    }

    // ---- host -> page ------------------------------------------------

    private void OnVmOutput(byte[] bytes) => PostOutput(bytes);

    private void OnVmNotice(string text) => PostOutput(Encoding.UTF8.GetBytes(text));

    private void PostOutput(byte[] bytes)
    {
        if (!_webReady || Web.CoreWebView2 is null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(
            new { type = "output", data = Convert.ToBase64String(bytes) }, JsonOpts);
        Web.CoreWebView2.PostWebMessageAsString(payload);
    }

    private sealed record InboundMessage
    {
        public string Type { get; init; } = string.Empty;
        public string? Data { get; init; }
        public int Cols { get; init; }
        public int Rows { get; init; }
    }
}
