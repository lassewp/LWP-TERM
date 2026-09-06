using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LwpTerm.App.Composition;
using LwpTerm.App.Services;
using LwpTerm.App.ViewModels;
using LwpTerm.App.Views.Editor;
using LwpTerm.Core;
using LwpTerm.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace LwpTerm.App;

public partial class App : Application
{
    private IHost? _host;

    public static IServiceProvider Services =>
        ((App)Current)._host?.Services
        ?? throw new InvalidOperationException("Host is not initialised yet.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                     ?? AppContext.BaseDirectory;
        var paths = AppPaths.Resolve(exeDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "lwpterm-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.Sink(InMemoryLogSink.Instance)
            .CreateLogger();

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled AppDomain exception");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddSingleton(paths);
                services.AddLwpTermServices();
            })
            .Build();

        await _host.StartAsync();

        if (!TryUnlockVault())
        {
            Shutdown();
            return;
        }

        var settings = _host.Services.GetRequiredService<LwpTerm.Core.Settings.ISettingsStore>().Current;
        _host.Services.GetRequiredService<Services.ThemeService>().Apply(settings.Theme);

        await _host.Services.GetRequiredService<SessionTreeViewModel>().LoadAsync();

        var shell = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = shell;
        shell.Show();
    }

    private bool TryUnlockVault()
    {
        var credentials = _host!.Services.GetRequiredService<ICredentialStore>();
        if (!credentials.IsLocked)
        {
            return true;
        }

        var prompt = new MasterPasswordWindow(credentials);
        prompt.ShowDialog();
        return prompt.Unlocked;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled dispatcher exception");
        MessageBox.Show(
            e.Exception.Message,
            "LWP-TERM — unexpected error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host is not null)
            {
                await _host.StopAsync(TimeSpan.FromSeconds(3));
                _host.Dispose();
            }
        }
        finally
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
