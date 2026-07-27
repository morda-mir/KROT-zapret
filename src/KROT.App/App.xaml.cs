using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using KROT.App.Services;
using KROT.App.ViewModels;
using KROT.Core.Models;
using KROT.Infrastructure.Logging;
using KROT.Infrastructure.Storage;
using KROT.Localization;

namespace KROT.App;

public partial class App
{
    private const string SingleInstanceMutexName = "Local\\KROT-zapret-GUI-v1";
    private Mutex? _singleInstanceMutex;
    private MainWindowViewModel? _viewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var ownsMutex);
        if (!ownsMutex)
        {
            Shutdown();
            return;
        }

        try
        {
            var settingsStore = new AtomicJsonSettingsStore();
            var isFirstRun = !File.Exists(AppPaths.SettingsFile);
            var settings = await settingsStore.LoadAsync(CancellationToken.None);
            if (isFirstRun)
            {
                settings.Language = CultureInfo.CurrentUICulture.Name.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
                    ? "ru"
                    : "en";
            }

            var localization = new DictionaryLocalizationService(settings.Language);
            var log = new RotatingFileLogService(settings.DetailedLogs);
            var autoStart = new RegistryAutoStartManager();
            var serviceClient = new FakeServiceClient();
            _viewModel = new MainWindowViewModel(
                settings,
                settingsStore,
                localization,
                serviceClient,
                autoStart,
                log);

            var window = new MainWindow(_viewModel);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"KROT could not start.\n\n{ex.Message}",
                "KROT zapret",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        if (_singleInstanceMutex != null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // This process did not own the mutex.
            }

            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}

