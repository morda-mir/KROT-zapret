using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KROT.App.Services;
using KROT.Core.Contracts;
using KROT.Core.Models;
using KROT.Core.States;

namespace KROT.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    public const string DiscordSupportUrl = "DISCORD_SUPPORT_URL";
    public const string ZapretOfficialUrl = "https://github.com/bol-van/zapret";

    private readonly KrotSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly ILocalizationService _localization;
    private readonly IServiceClient _serviceClient;
    private readonly RegistryAutoStartManager _autoStartManager;
    private readonly ILogService _log;
    private readonly CancellationTokenSource _lifetime = new();
    private AppState _appState = AppState.Off;
    private string _statusText = string.Empty;
    private bool _isHelpOpen;
    private bool _autoStart;
    private bool _detailedLogs;

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
        Journal = new ObservableCollection<string>();
        ToggleRuntimeCommand = new AsyncRelayCommand(ToggleRuntimeAsync, () => !IsBusy);
        ToggleHelpCommand = new RelayCommand(() => IsHelpOpen = !IsHelpOpen);
        SetRussianCommand = new RelayCommand(() => SetLanguage("ru"));
        SetEnglishCommand = new RelayCommand(() => SetLanguage("en"));
        OpenZapretCommand = new RelayCommand(() => OpenUrl(ZapretOfficialUrl));
        OpenSupportCommand = new RelayCommand(
            () => OpenUrl(DiscordSupportUrl),
            () => Uri.TryCreate(DiscordSupportUrl, UriKind.Absolute, out _));

        _localization.LanguageChanged += OnLanguageChanged;
        _serviceClient.SnapshotChanged += OnSnapshotChanged;
        RefreshLocalization();
        AddJournal("Journal.Ready");
    }

    public event EventHandler? RequestOpenLogs;

    public ObservableCollection<ServiceItemViewModel> Services { get; }

    public ObservableCollection<string> Journal { get; }

    public IAsyncRelayCommand ToggleRuntimeCommand { get; }

    public IRelayCommand ToggleHelpCommand { get; }

    public IRelayCommand SetRussianCommand { get; }

    public IRelayCommand SetEnglishCommand { get; }

    public IRelayCommand OpenZapretCommand { get; }

    public IRelayCommand OpenSupportCommand { get; }

    public string Title => _localization.Get("App.Title");

    public string SubtitleText => _localization.Get("App.Subtitle");

    public string FooterText => _localization.Get("Footer.Runtime");

    public string AutoStartText => _localization.Get("AutoStart");

    public string PrimaryActionText =>
        IsRunning ? _localization.Get("Action.Disable") : _localization.Get("Action.Enable");

    public string LanguageText => _localization.Get("Menu.Language");

    public string LogsText => _localization.Get("Menu.Logs");

    public string ViewLogsText => _localization.Get("Menu.ViewLogs");

    public string SupportText => _localization.Get("Menu.Support");

    public string ZapretText => _localization.Get("Menu.Zapret");

    public string LicensesText => _localization.Get("Menu.Licenses");

    public string VersionText => _localization.Get("Menu.Version");

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

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
    }

    private async Task ToggleRuntimeAsync()
    {
        try
        {
            if (IsRunning || AppState == AppState.FatalError)
            {
                await _serviceClient.StopAsync(_lifetime.Token);
                AddJournal("Journal.Disabled");
            }
            else
            {
                var options = new KrotStartOptions
                {
                    Services = Services.Where(x => x.IsSelected).Select(x => x.Id).ToList(),
                    DetailedLogs = DetailedLogs
                };
                await _serviceClient.StartAsync(options, _lifetime.Token);
                AddJournal("Journal.Enabled");
            }
        }
        catch (OperationCanceledException)
        {
            // Application is exiting.
        }
        catch (Exception ex)
        {
            AppState = AppState.FatalError;
            StatusText = _localization.Get("Status.Error");
            _log.Error("runtime.command.failed", "Runtime command failed.", ex);
        }
    }

    private void OnSnapshotChanged(object? sender, ServiceSnapshot snapshot)
    {
        AppState = snapshot.AppState;
        StatusText = _localization.Get(snapshot.MessageKey);
        UpdateChannelStates(snapshot.AppState);

        if (snapshot.AppState == AppState.TestingDirect)
        {
            AddJournal("Journal.Direct");
        }
        else if (snapshot.AppState == AppState.TestingSavedProfiles)
        {
            AddJournal("Journal.Profiles");
        }
    }

    private void UpdateChannelStates(AppState state)
    {
        var channelState = state switch
        {
            AppState.Starting or AppState.TestingDirect => ChannelState.Testing,
            AppState.TestingSavedProfiles or AppState.SearchingProfiles => ChannelState.Searching,
            AppState.Running => ChannelState.WorkingDirect,
            AppState.FatalError or AppState.PartialFailure => ChannelState.Failed,
            _ => ChannelState.Unknown
        };

        foreach (var service in Services)
        {
            foreach (var channel in service.Channels)
            {
                channel.State = service.IsSelected ? channelState : ChannelState.Disabled;
            }

            service.RefreshChannels();
        }

        OnPropertyChanged(nameof(Services));
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
                IsSelected(ServiceId.Discord),
                OnServiceSelectionChanged,
                Channel("✉", "Channel.Discord.Text"),
                Channel("▧", "Channel.Discord.Media"),
                Channel("●", "Channel.Discord.Voice")),
            new(
                ServiceId.YouTube,
                "▶",
                IsSelected(ServiceId.YouTube),
                OnServiceSelectionChanged,
                Channel("⌂", "Channel.YouTube.Site"),
                Channel("▶", "Channel.YouTube.Video"),
                Channel("Q", "Channel.YouTube.Quic")),
            new(
                ServiceId.AiServices,
                "AI",
                IsSelected(ServiceId.AiServices),
                OnServiceSelectionChanged,
                Channel("⌂", "Channel.Ai.Site"),
                Channel("≈", "Channel.Ai.Stream"))
        };
    }

    private static ChannelIndicatorViewModel Channel(string symbol, string tooltipKey) =>
        new() { Symbol = symbol, TooltipKey = tooltipKey };

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
        StatusText = _localization.Get(MessageKeyFor(AppState));
        foreach (var service in Services)
        {
            service.Tooltip = _localization.Get($"Service.{service.Id}");
            foreach (var channel in service.Channels)
            {
                channel.Tooltip = _localization.Get(channel.TooltipKey);
            }
        }

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(FooterText));
        OnPropertyChanged(nameof(AutoStartText));
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(LanguageText));
        OnPropertyChanged(nameof(LogsText));
        OnPropertyChanged(nameof(ViewLogsText));
        OnPropertyChanged(nameof(SupportText));
        OnPropertyChanged(nameof(ZapretText));
        OnPropertyChanged(nameof(LicensesText));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged(nameof(TrayOpenText));
        OnPropertyChanged(nameof(TrayExitText));
        OnPropertyChanged(nameof(TrayToggleText));
    }

    private void AddJournal(string key)
    {
        var message = _localization.Get(key);
        if (Journal.Count > 0 && Journal[0] == message)
        {
            return;
        }

        Journal.Insert(0, message);
        while (Journal.Count > 8)
        {
            Journal.RemoveAt(Journal.Count - 1);
        }
    }

    private void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static string MessageKeyFor(AppState state) =>
        state switch
        {
            AppState.Off => "Status.Off",
            AppState.Starting => "Status.Starting",
            AppState.TestingDirect => "Status.TestingDirect",
            AppState.TestingSavedProfiles or AppState.SearchingProfiles => "Status.Searching",
            AppState.Running => "Status.Running",
            AppState.Stopping => "Status.Stopping",
            _ => "Status.Error"
        };
}
