using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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
    public const string AuthorDiscordUrl = "discord://-/users/1110545127501475891";
    public const string AuthorDiscordWebUrl = "https://discord.com/users/1110545127501475891";
    public const string ZapretOfficialUrl = "https://github.com/bol-van/zapret";
    public const string TelegramProxyRepositoryUrl = "https://github.com/Flowseal/tg-ws-proxy";
    public const string WinDivertOfficialUrl = "https://github.com/basil00/WinDivert";
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromDays(1);
    private static readonly TimeSpan UpdateNotificationInterval = TimeSpan.FromDays(3);

    private readonly KrotSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly ILocalizationService _localization;
    private readonly IServiceClient _serviceClient;
    private readonly RegistryAutoStartManager _autoStartManager;
    private readonly IReleaseUpdateService _releaseUpdateService;
    private readonly ILogService _log;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private int _settingsRevision;
    private AppState _appState = AppState.Off;
    private bool _isFakeRuntime;
    private bool _externalTunnelDetected;
    private bool _isHelpOpen;
    private bool _isLicensesOpen;
    private bool _autoStart;
    private bool _detailedLogs;
    private readonly Task _statusPollingTask;
    private Task? _updatePollingTask;
    private int _updateChecksStarted;
    private ReleaseUpdateInfo? _availableUpdate;
    private ServiceSnapshot _lastSnapshot = new();

    public MainWindowViewModel(
        KrotSettings settings,
        ISettingsStore settingsStore,
        ILocalizationService localization,
        IServiceClient serviceClient,
        RegistryAutoStartManager autoStartManager,
        IReleaseUpdateService releaseUpdateService,
        ILogService log)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _localization = localization;
        _serviceClient = serviceClient;
        _autoStartManager = autoStartManager;
        _releaseUpdateService = releaseUpdateService;
        _log = log;
        _autoStart = settings.AutoStart;
        _detailedLogs = settings.DetailedLogs;

        Services = CreateServices(settings);
        ToggleRuntimeCommand = new AsyncRelayCommand(
            ToggleRuntimeAsync,
            CanToggleRuntime,
            AsyncRelayCommandOptions.AllowConcurrentExecutions);
        ToggleHelpCommand = new RelayCommand(() => IsHelpOpen = !IsHelpOpen);
        ToggleLicensesCommand = new RelayCommand(() => IsLicensesOpen = !IsLicensesOpen);
        SetRussianCommand = new RelayCommand(() => SetLanguage("ru"));
        SetEnglishCommand = new RelayCommand(() => SetLanguage("en"));
        OpenProjectRepositoryCommand = new RelayCommand(() => OpenUrl(ProjectRepositoryUrl));
        OpenAuthorDiscordCommand = new RelayCommand(OpenAuthorDiscord);
        OpenZapretCommand = new RelayCommand(() => OpenUrl(ZapretOfficialUrl));
        OpenTelegramProxyRepositoryCommand = new RelayCommand(
            () => OpenUrl(TelegramProxyRepositoryUrl));
        OpenWinDivertCommand = new RelayCommand(() => OpenUrl(WinDivertOfficialUrl));
        OpenThirdPartyNoticesCommand = new RelayCommand(OpenThirdPartyNotices);
        OpenAvailableUpdateCommand = new RelayCommand(
            OpenAvailableUpdate,
            () => AvailableUpdate != null);

        _localization.LanguageChanged += OnLanguageChanged;
        _serviceClient.SnapshotChanged += OnSnapshotChanged;
        RefreshLocalization();
        UpdateChannelStates(new ServiceSnapshot { AppState = AppState.Off });
        _statusPollingTask = PollServiceStatusAsync(_lifetime.Token);
    }

    public event EventHandler? RequestOpenLogs;

    public event EventHandler? RequestUpdateNotification;

    public ObservableCollection<ServiceItemViewModel> Services { get; }

    public IAsyncRelayCommand ToggleRuntimeCommand { get; }

    public IRelayCommand ToggleHelpCommand { get; }

    public IRelayCommand ToggleLicensesCommand { get; }

    public IRelayCommand SetRussianCommand { get; }

    public IRelayCommand SetEnglishCommand { get; }

    public IRelayCommand OpenProjectRepositoryCommand { get; }

    public IRelayCommand OpenAuthorDiscordCommand { get; }

    public IRelayCommand OpenZapretCommand { get; }

    public IRelayCommand OpenTelegramProxyRepositoryCommand { get; }

    public IRelayCommand OpenWinDivertCommand { get; }

    public IRelayCommand OpenThirdPartyNoticesCommand { get; }

    public IRelayCommand OpenAvailableUpdateCommand { get; }

    public string Title => IsFakeRuntime
        ? _localization.Get("App.Title") + " — DEMO"
        : _localization.Get("App.Title");

    public string AutoStartText => _localization.Get("AutoStart");

    public string VpnWarningText =>
        _localization.Get("Warning.DisableVpnProxy");

    public string PrimaryActionText =>
        AppState switch
        {
            AppState.Off or AppState.FatalError => _localization.Get("Action.Enable"),
            AppState.Starting or
            AppState.TestingDirect or
            AppState.TestingSavedProfiles or
            AppState.SearchingProfiles => _localization.Get("Action.Cancel"),
            _ => _localization.Get("Action.Disable")
        };

    public string LanguageText => _localization.Get("Menu.Language");

    public string LogsText => _localization.Get("Menu.Logs");

    public string ViewLogsText => _localization.Get("Menu.ViewLogs");

    public string AboutText => _localization.Get("Menu.About");

    public string AboutDescriptionText => _localization.Get("Menu.AboutDescription");

    public string ProjectRepositoryText => _localization.Get("Menu.ProjectRepository");

    public string AuthorDiscordText => _localization.Get("Menu.AuthorDiscord");

    public string ZapretText => _localization.Get("Menu.Zapret");

    public string TelegramProxyRepositoryText =>
        _localization.Get("Menu.TelegramProxyRepository");

    public string WinDivertText => _localization.Get("Menu.WinDivert");

    public string LicensesText => _localization.Get("Menu.Licenses");

    public string AllLicensesText => _localization.Get("Menu.AllLicenses");

    public string VersionText =>
        $"{_localization.Get("Menu.Version")} {ApplicationVersionProvider.Current}";

    public ReleaseUpdateInfo? AvailableUpdate
    {
        get => _availableUpdate;
        private set
        {
            if (!SetProperty(ref _availableUpdate, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasAvailableUpdate));
            OnPropertyChanged(nameof(UpdateTooltipTitle));
            OnPropertyChanged(nameof(UpdateTooltipDescription));
            OnPropertyChanged(nameof(UpdateTooltipAction));
            OpenAvailableUpdateCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasAvailableUpdate => AvailableUpdate != null;

    public string UpdateTooltipTitle => AvailableUpdate == null
        ? string.Empty
        : string.Format(
            _localization.Get("Update.Available"),
            AvailableUpdate.VersionTag);

    public string UpdateTooltipDescription => AvailableUpdate == null
        ? string.Empty
        : string.IsNullOrWhiteSpace(AvailableUpdate.Description)
            ? _localization.Get("Update.NoDescription")
            : AvailableUpdate.Description;

    public string UpdateTooltipAction => _localization.Get("Update.OpenRelease");

    public string UpdateNotificationTitle =>
        _localization.Get("Update.Tray.Title");

    public string UpdateNotificationText => AvailableUpdate == null
        ? string.Empty
        : string.Format(
            _localization.Get("Update.Tray.Text"),
            AvailableUpdate.VersionTag);

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

    public bool IsFakeRuntime
    {
        get => _isFakeRuntime;
        private set
        {
            if (SetProperty(ref _isFakeRuntime, value))
            {
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public bool ExternalTunnelDetected
    {
        get => _externalTunnelDetected;
        private set => SetProperty(ref _externalTunnelDetected, value);
    }

    public bool IsBusy => AppState is not (AppState.Off or AppState.Running or AppState.PartialFailure or AppState.FatalError);

    public bool HasBusyChannels =>
        Services.SelectMany(service => service.Channels)
            .Any(channel => channel.IsBusy);

    public bool IsConfigurationEditable => AppState is AppState.Off or AppState.FatalError;

    public bool IsHelpOpen
    {
        get => _isHelpOpen;
        set
        {
            if (SetProperty(ref _isHelpOpen, value) && !value)
            {
                IsLicensesOpen = false;
            }
        }
    }

    public bool IsLicensesOpen
    {
        get => _isLicensesOpen;
        set => SetProperty(ref _isLicensesOpen, value);
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
                if (_log is IConfigurableLogService configurableLog)
                {
                    configurableLog.Detailed = value;
                }

                _ = SaveSettingsSafeAsync();
                if (AppState is not (AppState.Off or AppState.FatalError))
                {
                    _ = SetServiceDetailedLogsSafeAsync(value);
                }
            }
        }
    }

    public string CurrentLanguage => _localization.Language.ToUpperInvariant();

    public string TrayOpenText => _localization.Get("Action.Open");

    public string TrayExitText => _localization.Get("Action.Exit");

    public string TrayToggleText => _localization.Get("Action.Toggle");

    public void OpenLogs() => RequestOpenLogs?.Invoke(this, EventArgs.Empty);

    public void StartUpdateChecks()
    {
        if (Interlocked.CompareExchange(ref _updateChecksStarted, 1, 0) != 0)
        {
            return;
        }

        _updatePollingTask = PollForUpdatesAsync(_lifetime.Token);
    }

    public async Task RestoreRuntimeIfNeededAsync()
    {
        if (!_settings.AutoStart || !_settings.RestoreEnabledState)
        {
            return;
        }

        try
        {
            var snapshot = await _serviceClient
                .GetStatusAsync(_lifetime.Token);
            OnSnapshotChanged(this, snapshot);
            if (snapshot.AppState is AppState.Off or AppState.FatalError)
            {
                await StartRuntimeAsync();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log.Error(
                "runtime.autostart.restore.failed",
                "Failed to restore the remembered runtime state.",
                ex);
        }
    }

    public async Task StopForExitAsync()
    {
        await SaveSettingsSafeAsync(CancellationToken.None);
        _lifetime.Cancel();
        try
        {
            await _serviceClient.ShutdownAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.Error(
                "exit.shutdown.failed",
                "Failed to stop the runtime service during exit.",
                ex);
        }
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        _serviceClient.SnapshotChanged -= OnSnapshotChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
        GC.KeepAlive(_statusPollingTask);
        GC.KeepAlive(_updatePollingTask);
    }

    private void OpenAvailableUpdate()
    {
        if (AvailableUpdate != null)
        {
            OpenUrl(AvailableUpdate.ReleaseUrl);
        }
    }

    private async Task PollForUpdatesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var update = await _releaseUpdateService
                    .CheckForUpdateAsync(
                        ApplicationVersionProvider.Current,
                        cancellationToken)
                    .ConfigureAwait(false);
                ApplyAvailableUpdateOnUiThread(update);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.Detail(
                    "update.check.failed",
                    $"Update check failed: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                await Task.Delay(UpdateCheckInterval, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private void ApplyAvailableUpdateOnUiThread(ReleaseUpdateInfo? update)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => ApplyAvailableUpdate(update)));
            return;
        }

        ApplyAvailableUpdate(update);
    }

    private void ApplyAvailableUpdate(ReleaseUpdateInfo? update)
    {
        AvailableUpdate = update;
        if (update == null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var notificationIsDue =
            !string.Equals(
                _settings.LastUpdateNotificationVersion,
                update.VersionTag,
                StringComparison.OrdinalIgnoreCase)
            || !_settings.LastUpdateNotificationUtc.HasValue
            || now - _settings.LastUpdateNotificationUtc.Value.ToUniversalTime()
            >= UpdateNotificationInterval;
        if (!notificationIsDue)
        {
            return;
        }

        _settings.LastUpdateNotificationVersion = update.VersionTag;
        _settings.LastUpdateNotificationUtc = now;
        _ = SaveSettingsSafeAsync();
        RequestUpdateNotification?.Invoke(this, EventArgs.Empty);
    }

    private async Task ToggleRuntimeAsync()
    {
        try
        {
            if (AppState is not (AppState.Off or AppState.FatalError))
            {
                _settings.RestoreEnabledState = false;
                await SaveSettingsSafeAsync();
                AppState = AppState.Stopping;
                UpdateChannelStates(new ServiceSnapshot { AppState = AppState.Stopping });
                await _serviceClient.StopAsync(_lifetime.Token);
            }
            else
            {
                if (AppState == AppState.FatalError)
                {
                    await _serviceClient.StopAsync(_lifetime.Token);
                }

                _settings.RestoreEnabledState = true;
                await SaveSettingsSafeAsync();
                await StartRuntimeAsync();
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

    private bool CanToggleRuntime() =>
        AppState != AppState.Stopping
        && (AppState is not (AppState.Off or AppState.FatalError)
            || Services.Any(service => service.IsSelected));

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
        IsFakeRuntime = snapshot.IsFakeRuntime;
        ExternalTunnelDetected = snapshot.ExternalTunnelDetected;
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
            service.UpdateCanRefresh(IsRunning);
        }

        OnPropertyChanged(nameof(Services));
        OnPropertyChanged(nameof(HasBusyChannels));
    }

    private async Task PollServiceStatusAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
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
                string.Empty,
                IsSelected(ServiceId.Discord),
                OnServiceSelectionChanged,
                RefreshServiceAsync,
                "M19,8 A7,7 0 1 0 19,16 M19,4 V8 H15",
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
                string.Empty,
                IsSelected(ServiceId.YouTube),
                OnServiceSelectionChanged,
                RefreshServiceAsync,
                "M19,8 A7,7 0 1 0 19,16 M19,4 V8 H15",
                Channel(
                    "site",
                    "M6,2 H19 V21 H6 Z M3,5 H6 M3,5 V23 H17 V21 M9,7 H16 M9,11 H16 M9,15 H16 M9,19 H16",
                    "Channel.YouTube.Site"),
                Channel(
                    "video",
                    "M4,7 H8 L9.5,4 H14.5 L16,7 H20 A2,2 0 0 1 22,9 V19 A2,2 0 0 1 20,21 H4 A2,2 0 0 1 2,19 V9 A2,2 0 0 1 4,7 Z M12,10 A4,4 0 1 0 12,18 A4,4 0 1 0 12,10 M19,10 L19.01,10",
                    "Channel.YouTube.Video")),
            new(
                ServiceId.Telegram,
                string.Empty,
                "pack://application:,,,/KROT;component/assets/services/telegram-128px.png",
                string.Empty,
                IsSelected(ServiceId.Telegram),
                OnServiceSelectionChanged,
                RefreshServiceAsync,
                "M14,4 H20 V10 M20,4 L11,13 M18,13 V20 H4 V6 H11",
                Channel(
                    "proxy",
                    "M10,13 A5,5 0 0 0 17,13 L19,11 A5,5 0 0 0 12,4 L10,6 M14,11 A5,5 0 0 0 7,11 L5,13 A5,5 0 0 0 12,20 L14,18",
                    "Channel.Telegram.Proxy"))
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
        ToggleRuntimeCommand.NotifyCanExecuteChanged();
    }

    private async Task RefreshServiceAsync(ServiceItemViewModel item)
    {
        item.UpdateCanRefresh(runtimeConnected: false);
        try
        {
            await _serviceClient
                .RefreshServiceAsync(item.Id, _lifetime.Token);
            if (item.Id == ServiceId.Telegram)
            {
                OpenTelegramProxy();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log.Error(
                "runtime.service.refresh.failed",
                $"Failed to refresh {item.Id}.",
                ex);
        }
        finally
        {
            item.UpdateCanRefresh(IsRunning);
        }
    }

    private async Task StartRuntimeAsync()
    {
        AppState = AppState.Starting;
        UpdateChannelStates(new ServiceSnapshot { AppState = AppState.Starting });
        var options = new KrotStartOptions
        {
            Services = Services
                .Where(item => item.IsSelected)
                .Select(item => item.Id)
                .ToList(),
            DetailedLogs = DetailedLogs,
            TelegramProxySecret = _settings.TelegramProxySecret
        };
        await _serviceClient.StartAsync(options, _lifetime.Token);
        if (options.Services.Contains(ServiceId.Telegram)
            && !_settings.TelegramProxyConfigured
            && OpenTelegramProxy())
        {
            _settings.TelegramProxyConfigured = true;
            await SaveSettingsSafeAsync();
        }
    }

    private Task SaveSettingsSafeAsync() =>
        SaveSettingsSafeAsync(_lifetime.Token);

    private async Task SetServiceDetailedLogsSafeAsync(bool enabled)
    {
        try
        {
            await _serviceClient.SetDetailedLogsAsync(enabled, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log.Error(
                "logging.detail.update.failed",
                "Failed to update service detailed logging.",
                ex);
        }
    }

    private async Task SaveSettingsSafeAsync(CancellationToken cancellationToken)
    {
        var snapshot = CloneSettings(_settings);
        var revision = Interlocked.Increment(ref _settingsRevision);
        try
        {
            await _settingsSaveGate.WaitAsync(cancellationToken);
            try
            {
                if (revision == Volatile.Read(ref _settingsRevision))
                {
                    await _settingsStore.SaveAsync(snapshot, cancellationToken);
                }
            }
            finally
            {
                _settingsSaveGate.Release();
            }
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

    private static KrotSettings CloneSettings(KrotSettings settings) => new()
    {
        SchemaVersion = settings.SchemaVersion,
        Language = settings.Language,
        AutoStart = settings.AutoStart,
        RestoreEnabledState = settings.RestoreEnabledState,
        DetailedLogs = settings.DetailedLogs,
        LastUpdateNotificationVersion = settings.LastUpdateNotificationVersion,
        LastUpdateNotificationUtc = settings.LastUpdateNotificationUtc,
        TelegramProxySecret = settings.TelegramProxySecret,
        TelegramProxyConfigured = settings.TelegramProxyConfigured,
        Services = settings.Services
            .Select(service => new ServiceSelection
            {
                Id = service.Id,
                IsEnabled = service.IsEnabled
            })
            .ToList()
    };

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
            service.RefreshTooltip = service.Id == ServiceId.Telegram
                ? _localization.Get("Action.ConnectTelegram")
                : string.Format(
                    _localization.Get("Action.RecheckService"),
                    _localization.Get(service.Id == ServiceId.Discord
                        ? "Service.Discord"
                        : "Service.YouTube"));
            foreach (var channel in service.Channels)
            {
                RefreshChannelTooltip(channel);
            }
        }

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(AutoStartText));
        OnPropertyChanged(nameof(VpnWarningText));
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(LanguageText));
        OnPropertyChanged(nameof(LogsText));
        OnPropertyChanged(nameof(ViewLogsText));
        OnPropertyChanged(nameof(AboutText));
        OnPropertyChanged(nameof(AboutDescriptionText));
        OnPropertyChanged(nameof(ProjectRepositoryText));
        OnPropertyChanged(nameof(AuthorDiscordText));
        OnPropertyChanged(nameof(ZapretText));
        OnPropertyChanged(nameof(TelegramProxyRepositoryText));
        OnPropertyChanged(nameof(WinDivertText));
        OnPropertyChanged(nameof(LicensesText));
        OnPropertyChanged(nameof(AllLicensesText));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged(nameof(TrayOpenText));
        OnPropertyChanged(nameof(TrayExitText));
        OnPropertyChanged(nameof(TrayToggleText));
        OnPropertyChanged(nameof(UpdateTooltipTitle));
        OnPropertyChanged(nameof(UpdateTooltipDescription));
        OnPropertyChanged(nameof(UpdateTooltipAction));
        OnPropertyChanged(nameof(UpdateNotificationTitle));
        OnPropertyChanged(nameof(UpdateNotificationText));
    }

    private void RefreshChannelTooltip(ChannelIndicatorViewModel channel)
    {
        if (channel.State == ChannelState.Failed)
        {
            channel.Tooltip = _localization.Get("Channel.Error.Logs");
            return;
        }

        if (string.Equals(channel.Id, "voice", StringComparison.Ordinal))
        {
            if (channel.State == ChannelState.WaitingForActivity)
            {
                channel.Tooltip =
                    _localization.Get("Channel.Discord.Voice.Waiting");
                return;
            }

            if (channel.State is ChannelState.Testing or ChannelState.Searching)
            {
                channel.Tooltip =
                    _localization.Get("Channel.Discord.Voice.Checking");
                return;
            }
        }

        channel.Tooltip = _localization.Get(channel.TooltipKey);
    }

    private void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OpenThirdPartyNotices()
    {
        var path = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "THIRD_PARTY_NOTICES.txt");
        if (!File.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OpenAuthorDiscord()
    {
        try
        {
            OpenUrl(AuthorDiscordUrl);
        }
        catch (Exception ex)
        {
            _log.Error(
                "discord.profile.open.failed",
                "Could not open the Discord profile in the desktop client.",
                ex);
            OpenUrl(AuthorDiscordWebUrl);
        }
    }

    private bool OpenTelegramProxy()
    {
        if (!KrotSettings.IsValidTelegramProxySecret(
                _settings.TelegramProxySecret))
        {
            return false;
        }

        try
        {
            OpenUrl(
                "tg://proxy?server=127.0.0.1&port=1443&secret=dd"
                + _settings.TelegramProxySecret);
            return true;
        }
        catch (Exception ex)
        {
            _log.Error(
                "telegram.proxy.open.failed",
                "Could not open the Telegram proxy link.",
                ex);
            return false;
        }
    }

}
