using LwpTerm.App.Services;
using LwpTerm.App.ViewModels;
using LwpTerm.App.ViewModels.Panels;
using Microsoft.Extensions.DependencyInjection;

namespace LwpTerm.App.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLwpTermServices(this IServiceCollection services)
    {
        // Services
        services.AddSingleton<ILayoutPersistenceService, LayoutPersistenceService>();
        services.AddSingleton<ISessionLauncher, SessionLauncher>();

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
