using System;
using System.Windows;
using LwpTerm.Core.Security;

namespace LwpTerm.App.Services;

/// <summary>
/// Prompts the user (on the UI thread) to accept an unknown or changed SSH host
/// key. Called synchronously from the SSH connect path on a background thread.
/// </summary>
public sealed class DialogHostKeyVerifier : IHostKeyVerifier
{
    public bool Verify(HostKeyPrompt prompt)
    {
        var app = Application.Current;
        if (app is null)
        {
            return false;
        }

        return app.Dispatcher.Invoke(() => Ask(prompt));
    }

    private static bool Ask(HostKeyPrompt p)
    {
        var header = p.IsChanged
            ? "WARNING: the host key for this server has CHANGED.\n\n" +
              "This can mean the server was reinstalled — or that the connection is being intercepted.\n\n"
            : "The authenticity of this host can't be established.\n\n";

        var body =
            $"{header}" +
            $"Host:        {p.Host}:{p.Port}\n" +
            $"Key type:    {p.KeyAlgorithm}\n" +
            $"SHA256:      {p.FingerprintSha256}\n" +
            $"MD5:         {p.FingerprintMd5}\n\n" +
            "Trust this host and continue connecting?";

        var caption = p.IsChanged ? "SSH host key changed" : "Unknown SSH host key";
        var icon = p.IsChanged ? MessageBoxImage.Warning : MessageBoxImage.Question;
        var owner = Application.Current?.MainWindow;

        var result = owner is not null
            ? MessageBox.Show(owner, body, caption, MessageBoxButton.YesNo, icon, MessageBoxResult.No)
            : MessageBox.Show(body, caption, MessageBoxButton.YesNo, icon, MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }
}
