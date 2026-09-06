using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LwpTerm.App.ViewModels.Tabs;
using LwpTerm.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace LwpTerm.App.Services;

/// <summary>
/// A long-lived terminal surface (xterm.js in WebView2) owned by the tab view
/// model. <see cref="View"/> is re-parented as the tab docks / floats, so the
/// session and its scrollback survive. A bounded output buffer is replayed if
/// the page ever reloads.
/// </summary>
public sealed class TerminalSessionHost
{
    private const int MaxBufferedBytes = 1_000_000;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly AppPaths _paths;
    private readonly Grid _root;
    private readonly WebView2 _web;
    private readonly TextBlock _loading;

    private readonly Queue<byte[]> _buffer = new();
    private long _bufferedBytes;

    private TerminalConfig _config = TerminalConfig.Default;
    private bool _initStarted;
    private bool _webReady;

    public TerminalSessionHost(AppPaths paths)
    {
        _paths = paths;

        _web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1E, 0x1E, 0x1E) };
        _loading = new TextBlock
        {
            Text = "Starting terminal…",
            Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _root = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)) };
        _root.Children.Add(_web);
        _root.Children.Add(_loading);
    }

    public FrameworkElement View => _root;

    public event Action<byte[]>? InputReceived;

    public event Action<int, int>? Resized;

    /// <summary>Raised after the page is up (and any buffer replayed) with the initial size.</summary>
    public event Action<int, int>? Ready;

    public event Action? Bell;

    public void SetConfig(TerminalConfig config) => _config = config;

    public async void EnsureInitialized()
    {
        if (_initStarted)
        {
            return;
        }

        _initStarted = true;

        try
        {
            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(_paths.Root, "webview2"));
            await _web.EnsureCoreWebView2Async(env);

            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;

            var assetDir = Path.Combine(AppContext.BaseDirectory, "Assets", "webterm");
            core.SetVirtualHostNameToFolderMapping(
                "webterm", assetDir, CoreWebView2HostResourceAccessKind.Allow);

            core.WebMessageReceived += OnWebMessage;
            _web.NavigationCompleted += (_, _) => _loading.Visibility = Visibility.Collapsed;

            core.Navigate("https://webterm/index.html");
        }
        catch (Exception ex)
        {
            _loading.Text = "WebView2 failed to start:\n" + ex.Message +
                            "\n\nInstall the Microsoft Edge WebView2 Runtime.";
        }
    }

    public void SendOutput(ReadOnlySpan<byte> bytes)
    {
        var copy = bytes.ToArray();
        Buffer(copy);
        Post(copy);
    }

    public void SendNotice(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Buffer(bytes);
        Post(bytes);
    }

    public void Dispose()
    {
        try
        {
            _web.Dispose();
        }
        catch
        {
            // ignore
        }
    }

    // ---- page -> host ---------------------------------------------------

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
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
                _webReady = true;
                PostConfig();
                ReplayBuffer();
                Ready?.Invoke(Math.Max(1, msg.Cols), Math.Max(1, msg.Rows));
                break;
            case "input":
                if (msg.Data is not null)
                {
                    InputReceived?.Invoke(Convert.FromBase64String(msg.Data));
                }

                break;
            case "resize":
                Resized?.Invoke(Math.Max(1, msg.Cols), Math.Max(1, msg.Rows));
                break;
            case "bell":
                Bell?.Invoke();
                break;
        }
    }

    // ---- host -> page --------------------------------------------------

    private void Post(byte[] bytes)
    {
        if (!_webReady || _web.CoreWebView2 is null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(
            new { type = "output", data = Convert.ToBase64String(bytes) }, JsonOpts);
        _web.CoreWebView2.PostWebMessageAsString(payload);
    }

    private void PostConfig()
    {
        if (_web.CoreWebView2 is null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(
            new { type = "config", fontFamily = _config.FontFamily, fontSize = _config.FontSize, scrollback = _config.Scrollback },
            JsonOpts);
        _web.CoreWebView2.PostWebMessageAsString(payload);
    }

    private void Buffer(byte[] chunk)
    {
        _buffer.Enqueue(chunk);
        _bufferedBytes += chunk.Length;
        while (_bufferedBytes > MaxBufferedBytes && _buffer.Count > 1)
        {
            _bufferedBytes -= _buffer.Dequeue().Length;
        }
    }

    private void ReplayBuffer()
    {
        if (_buffer.Count == 0 || _web.CoreWebView2 is null)
        {
            return;
        }

        var total = new byte[_bufferedBytes];
        var offset = 0;
        foreach (var chunk in _buffer)
        {
            System.Buffer.BlockCopy(chunk, 0, total, offset, chunk.Length);
            offset += chunk.Length;
        }

        var payload = JsonSerializer.Serialize(
            new { type = "output", data = Convert.ToBase64String(total) }, JsonOpts);
        _web.CoreWebView2.PostWebMessageAsString(payload);
    }

    private sealed record InboundMessage
    {
        public string Type { get; init; } = string.Empty;
        public string? Data { get; init; }
        public int Cols { get; init; }
        public int Rows { get; init; }
    }
}
