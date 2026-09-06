using System;
using Velopack;

namespace LwpTerm.App;

/// <summary>
/// Explicit entry point so Velopack's install / update / uninstall hooks run at
/// the very top of the process (before WPF spins up) and exit fast when invoked
/// for one of those operations.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
