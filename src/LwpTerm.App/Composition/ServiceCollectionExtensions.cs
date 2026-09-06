using LwpTerm.App.Services;
using LwpTerm.App.ViewModels;
using LwpTerm.App.ViewModels.Panels;
using LwpTerm.Connections;
using LwpTerm.Core.Security;
using LwpTerm.Core.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace LwpTerm.App.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLwpTermServices(this IServiceCollection services)
    {
        // Core services
        services.AddSingleton<ISessionStore, JsonSessionStore>();
        services.AddSingleton<ICredentialStore, CredentialStore>();
        services.AddSingleton<KnownHostsStore>();
        services.AddSingleton<IHostKeyVerifier, DialogHostKeyVerifier>();
        services.AddSingleton<ITerminalConnectionFactory, TerminalConnectionFactory>();

        // App services
        services.AddSingleton<ILayoutPersistenceService, LayoutPersistenceService>();
        services.AddSingleton<ISessionLauncher, SessionLauncher>();
        services.AddSingleton<IDialogService, DialogService>();

        // Panel view models (singletons — one instance per shell)
        services.AddSingleton<SessionTreeViewModel>();
        services.AddSingleton<TransfersViewModel>();
        services.AddSingleton<LogViewModel>();

        // Shell
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
