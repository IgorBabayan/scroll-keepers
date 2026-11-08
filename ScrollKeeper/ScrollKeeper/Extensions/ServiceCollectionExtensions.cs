using System.Net.Http;
using ScrollKeeper.Presentation.Services;
using ScrollKeeper.Presentation.Services.Toasts;
using ScrollKeeper.Presentation.ViewModels.Settings;
using ScrollKeeper.Presentation.ViewModels.Shell;
using ScrollKeeper.Presentation.Views;
using ScrollKeeper.Presentation.Views.Settings;
using ScrollKeeper.Services;
using ScrollKeeper.Services.Addons;
using ScrollKeeper.Services.Catalog;
using ScrollKeeper.Services.Images;
using ScrollKeeper.Services.Settings;
using ScrollKeeper.Services.Theming;
using ScrollKeeper.Services.Updates;

namespace ScrollKeeper.Extensions;

static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddApplicationServices() => services
            .AddInfrastructure()
            .AddPresentation();

        private IServiceCollection AddInfrastructure()
        {
            services.AddSingleton<ISettingsService, JsonSettingsService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<IUpdateService, GitHubUpdateService>();

            // One HTTP client for ESOUI (catalog, archives, images).
            services.AddSingleton<HttpClient>(_ => HttpClientFactory.Create());
            services.AddSingleton<IAddonCatalog, EsoUiCatalog>();
            services.AddSingleton<IImageCache, ImageCache>();

            // AddOns folder
            services.AddSingleton<ILocalAddonScanner, LocalAddonScanner>();
            services.AddSingleton<IInstallRecordStore, JsonInstallRecordStore>();
            services.AddSingleton<IAddonInstaller, AddonInstaller>();

            return services;
        }

        private IServiceCollection AddPresentation()
        {
            // Services
            services.AddSingleton<IWindowService, WindowService>();
            services.AddSingleton<IFolderPickerService, FolderPickerService>();
            services.AddSingleton<IToastService, ToastService>();
            services.AddSingleton<IAutoUpdater, AutoUpdater>();

            // Shell
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();

            // Settings (new instance every time the dialog opens)
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<SettingsWindow>();

            // The addon page is created by WindowService for a given addon.
            return services;
        }
    }
}
