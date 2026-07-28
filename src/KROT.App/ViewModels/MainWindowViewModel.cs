using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KROT.App.Services;
using KROT.Core.Contracts;
using KROT.Core.Models;
using KROT.Core.States;

namespace KROT.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    public const string ProjectRepositoryUrl = "https://github.com/morda-mir/KROT-zapret";
    public const string ZapretOfficialUrl = "https://github.com/bol-van/zapret";

    private readonly KrotSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly ILocalizationService _localization;
    private readonly IServiceClient _serviceClient;
    private readonly RegistryAutoStartManager _autoStartManager;
    private readonly ILogService _log;
    private readonly CancellationTokenSource _lifetime = new();
    private AppState _appState = AppState.Off;
    private bool _isHelpOpen;
    private bool _autoStart;
    private bool _detailedLogs;
    private readonly Task _statusPollingTask;
    private ServiceSnapshot _lastSnapshot = new();

    public MainWindowViewModel(
        KrotSettings settings,
        ISettingsStore settingsStore,
        ILocalizationService localization,
        IServiceClient serviceClient,
        RegistryAutoStartManager autoStartManager,
        ILogService log)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _localization = localization;
        _serviceClient = serviceClient;
        _autoStartManager = autoStartManager;
        _log = log;
        _autoStart = settings.AutoStart;
        _detailedLogs = settings.DetailedLogs;

        Services = CreateServices(settings);
        ToggleRuntimeCommand = new AsyncRelayCommand(ToggleRuntimeAsync, () => !IsBusy);
        ToggleHelpCommand = new RelayCommand(() => IsHelpOpen = !IsHelpOpen);
        SetRussianCommand = new RelayCommand(() => SetLanguage("ru"));
        SetEnglishCommand = new RelayCommand(() => SetLanguage("en"));
        OpenProjectRepositoryCommand = new RelayCommand(() => OpenUrl(ProjectRepositoryUrl));
        OpenZapretCommand = new RelayCommand(() => OpenUrl(ZapretOfficialUrl));

        _localization.LanguageChanged += OnLanguageChanged;
        _serviceClient.SnapshotChanged += OnSnapshotChanged;
        RefreshLocalization();
        UpdateChannelStates(new ServiceSnapshot { AppState = AppState.Off });
        _statusPollingTask = PollServiceStatusAsync(_lifetime.Token);
    }

    public event EventHandler? RequestOpenLogs;

    public ObservableCollection<ServiceItemViewModel> Services { get; }

    public IAsyncRelayCommand ToggleRuntimeCommand { get; }

    public IRelayCommand ToggleHelpCommand { get; }

    public IRelayCommand SetRussianCommand { get; }

    public IRelayCommand SetEnglishCommand { get; }

    public IRelayCommand OpenProjectRepositoryCommand { get; }

    public IRelayCommand OpenZapretCommand { get; }

    public string Title => _localization.Get("App.Title");

    public string AutoStartText => _localization.Get("AutoStart");

    public string PrimaryActionText =>
        AppState switch
        {
            AppState.Off or AppState.FatalError => _localization.Get("Action.Enable"),
            AppState.Starting or
            AppState.TestingDirect or
            AppState.TestingSavedProfiles or
            AppState.SearchingProfiles => _localization.Get("Action.Starting"),
            _ => _localization.Get("Action.Disable")
        };

    public string LanguageText => _localization.Get("Menu.Language");

    public string LogsText => _localization.Get("Menu.Logs");

    public string ViewLogsText => _localization.Get("Menu.ViewLogs");

    public string AboutText => _localization.Get("Menu.About");

    public string ProjectRepositoryText => _localization.Get("Menu.ProjectRepository");

    public string ZapretText => _localization.Get("Menu.Zapret");

    public string LicensesText => _localization.Get("Menu.Licenses");

    public string VersionText => _localization.Get("Menu.Version");

    public AppState AppState
    {
        get => _appState;
        private set
        {
            if (SetProperty(ref _appState, value))
            {
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsConfigurationEditable));
                OnPropertyChanged(nameof(PrimaryActionText));
                ToggleRuntimeCommand.NotifyCanExecuteChanged();
                foreach (var service in Services)
                {
                    service.CanEdit = IsConfigurationEditable;
                }
            }
        }
    }

    public bool IsRunning => AppState is AppState.Running or AppState.PartialFailure;

    public bool IsBusy => AppState is not (AppState.Off or AppState.Running or AppState.PartialFailure or AppState.FatalError);

    public bool IsConfigurationEditable => AppState is AppState.Off or AppState.FatalError;

    public bool IsHelpOpen
    {
        get => _isHelpOpen;
        set => SetProperty(ref _isHelpOpen, value);
    }

    public bool AutoStart
    {
        get => _autoStart;
        set
        {
            if (!SetProperty(ref _autoStart, value))
            {
                return;
            }

            try
            {
                _autoStartManager.SetEnabled(value);
                _settings.AutoStart = value;
                _ = SaveSettingsSafeAsync();
            }
            catch (Exception ex)
            {
                _log.Error("autostart.failed", "Failed to update autostart.", ex);
            }
        }
    }

    public bool DetailedLogs
    {
        get => _detailedLogs;
        set
        {
            if (SetProperty(ref _detailedLogs, value))
            {
                _settings.DetailedLogs = value;
                _ = SaveSettingsSafeAsync();
            }
        }
    }

    public string CurrentLanguage => _localization.Language.ToUpperInvariant();

    public string TrayOpenText => _localization.Get("Action.Open");

    public string TrayExitText => _localization.Get("Action.Exit");

    public string TrayToggleText => _localization.Get("Action.Toggle");

    public void OpenLogs() => RequestOpenLogs?.Invoke(this, EventArgs.Empty);

    public async Task StopForExitAsync()
    {
        try
        {
            if (AppState != AppState.Off)
            {
                await _serviceClient.StopAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _log.Error("exit.stop.failed", "Failed to stop runtime during exit.", ex);
        }
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        _serviceClient.SnapshotChanged -= OnSnapshotChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
        GC.KeepAlive(_statusPollingTask);
    }

    private async Task ToggleRuntimeAsync()
    {
        try
        {
            if (IsRunning || AppState == AppState.FatalError)
            {
                AppState = AppState.Stopping;
                UpdateChannelStates(new ServiceSnapshot { AppState = AppState.Stopping });
                await _serviceClient.StopAsync(_lifetime.Token);
            }
            else
            {
                AppState = AppState.Starting;
                UpdateChannelStates(new ServiceSnapshot { AppState = AppState.Starting });
                var options = new KrotStartOptions
                {
                    Services = Services.Where(x => x.IsSelected).Select(x => x.Id).ToList(),
                    DetailedLogs = DetailedLogs
                };
                await _serviceClient.StartAsync(options, _lifetime.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Application is exiting.
        }
        catch (Exception ex)
        {
            AppState = AppState.FatalError;
            _log.Error("runtime.command.failed", "Runtime command failed.", ex);
        }
    }

    private void OnSnapshotChanged(object? sender, ServiceSnapshot snapshot)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => ApplySnapshot(snapshot)));
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(ServiceSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        AppState = snapshot.AppState;
        UpdateChannelStates(snapshot);
    }

    private void UpdateChannelStates(ServiceSnapshot snapshot)
    {
        var fallbackState = snapshot.AppState switch
        {
            AppState.Starting or AppState.TestingDirect => ChannelState.Testing,
            AppState.TestingSavedProfiles or AppState.SearchingProfiles => ChannelState.Searching,
            AppState.Running => ChannelState.WaitingForActivity,
            AppState.FatalError or AppState.PartialFailure => ChannelState.Failed,
            _ => ChannelState.Unknown
        };

        foreach (var service in Services)
        {
            foreach (var channel in service.Channels)
            {
                var status = snapshot.Channels.FirstOrDefault(item =>
                    item.ServiceId == service.Id
                    && string.Equals(
                        item.ChannelId,
                        channel.Id,
                        StringComparison.Ordinal));
                channel.State = !service.IsSelected
                    ? ChannelState.Disabled
                    : status?.State ?? fallbackState;
                channel.StrategyId = status?.StrategyId ?? string.Empty;
                RefreshChannelTooltip(channel);
            }

            service.RefreshChannels();
        }

        OnPropertyChanged(nameof(Services));
    }

    private async Task PollServiceStatusAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (IsBusy)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    continue;
                }

                var snapshot = await _serviceClient
                    .GetStatusAsync(cancellationToken);
                OnSnapshotChanged(this, snapshot);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // The Windows service can be briefly unavailable during a Debug rebuild.
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private ObservableCollection<ServiceItemViewModel> CreateServices(KrotSettings settings)
    {
        bool IsSelected(ServiceId id) =>
            settings.Services.FirstOrDefault(x => x.Id == id)?.IsEnabled ?? false;

        return new ObservableCollection<ServiceItemViewModel>
        {
            new(
                ServiceId.Discord,
                "D",
                "pack://application:,,,/KROT;component/assets/services/discord-512px.png",
                IsSelected(ServiceId.Discord),
                OnServiceSelectionChanged,
                Channel(
                    "text",
                    "M6,2 H19 V21 H6 Z M3,5 H6 M3,5 V23 H17 V21 M9,7 H16 M9,11 H16 M9,15 H16 M9,19 H16",
                    "Channel.Discord.Text"),
                Channel(
                    "media",
                    "M4,7 H8 L9.5,4 H14.5 L16,7 H20 A2,2 0 0 1 22,9 V19 A2,2 0 0 1 20,21 H4 A2,2 0 0 1 2,19 V9 A2,2 0 0 1 4,7 Z M12,10 A4,4 0 1 0 12,18 A4,4 0 1 0 12,10 M19,10 L19.01,10",
                    "Channel.Discord.Media"),
                Channel(
                    "voice",
                    "M12,3 A3,3 0 0 0 9,6 V12 A3,3 0 0 0 15,12 V6 A3,3 0 0 0 12,3 M5,11 V12 A7,7 0 0 0 19,12 V11 M12,19 V22 M8,22 H16",
                    "Channel.Discord.Voice")),
            new(
                ServiceId.YouTube,
                "▶",
                "pack://application:,,,/KROT;component/assets/services/youtube-512px.png",
                IsSelected(ServiceId.YouTube),
                OnServiceSelectionChanged,
                Channel(
                    "site",
                    "M6,2 H19 V21 H6 Z M3,5 H6 M3,5 V23 H17 V21 M9,7 H16 M9,11 H16 M9,15 H16 M9,19 H16",
                    "Channel.YouTube.Site"),
                Channel(
                    "video",
                    "M4,7 H8 L9.5,4 H14.5 L16,7 H20 A2,2 0 0 1 22,9 V19 A2,2 0 0 1 20,21 H4 A2,2 0 0 1 2,19 V9 A2,2 0 0 1 4,7 Z M12,10 A4,4 0 1 0 12,18 A4,4 0 1 0 12,10 M19,10 L19.01,10",
                    "Channel.YouTube.Video"))
        };
    }

    private static ChannelIndicatorViewModel Channel(
        string id,
        string geometryData,
        string tooltipKey) =>
        new()
        {
            Id = id,
            GeometryData = geometryData,
            TooltipKey = tooltipKey
        };

    private void OnServiceSelectionChanged(ServiceItemViewModel item)
    {
        var setting = _settings.Services.FirstOrDefault(x => x.Id == item.Id);
        if (setting == null)
        {
            _settings.Services.Add(new ServiceSelection { Id = item.Id, IsEnabled = item.IsSelected });
        }
        else
        {
            setting.IsEnabled = item.IsSelected;
        }

        _ = SaveSettingsSafeAsync();
        UpdateChannelStates(_lastSnapshot);
    }

    private async Task SaveSettingsSafeAsync()
    {
        try
        {
            await _settingsStore.SaveAsync(_settings, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            // Application is exiting.
        }
        catch (Exception ex)
        {
            _log.Error("settings.save.failed", "Failed to save settings.", ex);
        }
    }

    private void SetLanguage(string language)
    {
        _settings.Language = language;
        _localization.SetLanguage(language);
        _ = SaveSettingsSafeAsync();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshLocalization();

    private void RefreshLocalization()
    {
        foreach (var service in Services)
        {
            service.Tooltip = _localization.Get($"Service.{service.Id}");
            foreach (var channel in service.Channels)
            {
                RefreshChannelTooltip(channel);
            }
        }

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(AutoStartText));
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(LanguageText));
        OnPropertyChanged(nameof(LogsText));
        OnPropertyChanged(nameof(ViewLogsText));
        OnPropertyChanged(nameof(AboutText));
        OnPropertyChanged(nameof(ProjectRepositoryText));
        OnPropertyChanged(nameof(ZapretText));
        OnPropertyChanged(nameof(LicensesText));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged(nameof(TrayOpenText));
        OnPropertyChanged(nameof(TrayExitText));
        OnPropertyChanged(nameof(TrayToggleText));
    }

    private void RefreshChannelTooltip(ChannelIndicatorViewModel channel)
    {
        var label = _localization.Get(channel.TooltipKey);
        var state = _localization.Get($"ChannelState.{channel.State}");
        channel.Tooltip = $"{label} — {state}";
    }

    private void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

}
