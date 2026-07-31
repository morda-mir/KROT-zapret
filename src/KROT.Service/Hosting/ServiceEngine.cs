using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Core.Models;
using KROT.Core.States;
using KROT.Diagnostics.FieldTesting;
using KROT.Zapret.Profiles;

namespace KROT.Service.Hosting;

public sealed class ServiceEngine
{
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan NetworkChangeSettleDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan NetworkRecoveryDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ExternalTunnelCheckInterval =
        TimeSpan.FromSeconds(3);
    private static readonly TimeSpan TcpCheckIndicatorDuration =
        TimeSpan.FromSeconds(4);
    private static readonly TimeSpan DefaultUdpConfirmationTimeout =
        TimeSpan.FromSeconds(15);
    private readonly IZapretProcessManager _processManager;
    private readonly BuiltInPresetCatalog _presetCatalog;
    private readonly AdaptivePresetSearchEngine _presetSearch;
    private readonly NetworkEnvironmentInspector _networkInspector;
    private readonly IInternetAvailabilityProbe _internetProbe;
    private readonly ILogService _log;
    private readonly bool _isFakeRuntime;
    private readonly TimeSpan _udpConfirmationTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _tcpProbeGate = new(1, 1);
    private readonly SemaphoreSlim _healthSignal = new(0, 1);
    private readonly AppStateMachine _stateMachine = new();
    private readonly object _healthSync = new();
    private readonly object _sessionSync = new();
    private readonly object _startSync = new();
    private CancellationTokenSource? _activeStartCancellation;
    private CancellationTokenSource? _healthCancellation;
    private Task? _healthTask;
    private bool _networkChangeSubscribed;
    private KrotStartOptions? _activeOptions;
    private string? _activeNetworkFingerprint;
    private PresetSelection? _activeSelection;
    private readonly Dictionary<ServiceId, bool?> _tcpReachability = new();
    private readonly Dictionary<ServiceId, DateTime> _tcpCheckingUntilUtc = new();
    private readonly HashSet<ServiceId> _refreshingTcpServices = new();
    private readonly HashSet<string> _confirmedUdpChannels =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeUdpChannels =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedUdpChannels =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _udpAttemptGenerations =
        new(StringComparer.Ordinal);
    private long _nextUdpAttemptGeneration;
    private bool _internetUnavailable;
    private bool _externalTunnelDetected;

    public ServiceEngine(
        IZapretProcessManager processManager,
        BuiltInPresetCatalog presetCatalog,
        AdaptivePresetSearchEngine presetSearch,
        NetworkEnvironmentInspector networkInspector,
        IInternetAvailabilityProbe internetProbe,
        ILogService log,
        bool isFakeRuntime,
        TimeSpan? udpConfirmationTimeout = null)
    {
        _processManager = processManager;
        _presetCatalog = presetCatalog;
        _presetSearch = presetSearch;
        _networkInspector = networkInspector;
        _internetProbe = internetProbe;
        _log = log;
        _isFakeRuntime = isFakeRuntime;
        _udpConfirmationTimeout =
            udpConfirmationTimeout ?? DefaultUdpConfirmationTimeout;
        if (_udpConfirmationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(udpConfirmationTimeout),
                "UDP confirmation timeout must be positive.");
        }

        if (_processManager is IRuntimeOutputSource outputSource)
        {
            outputSource.RuntimeOutput += OnRuntimeOutput;
        }
    }

    public ServiceSnapshot Snapshot => BuildSnapshot();

    public async Task StartAsync(
        KrotStartOptions options,
        CancellationToken cancellationToken)
    {
        var startCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_startSync)
        {
            if (_activeStartCancellation != null)
            {
                startCancellation.Dispose();
                throw new InvalidOperationException(
                    "A runtime start operation is already in progress.");
            }

            _activeStartCancellation = startCancellation;
        }

        var gateHeld = false;
        try
        {
            await _gate.WaitAsync(startCancellation.Token).ConfigureAwait(false);
            gateHeld = true;
            if (_stateMachine.State != AppState.Off)
            {
                return;
            }

            _stateMachine.TransitionTo(AppState.Starting);
            SetActiveSession(options, null, null, null);
            if (_log is IConfigurableLogService configurableLog)
            {
                configurableLog.Detailed = options.DetailedLogs;
            }

            _log.Info(
                "runtime.starting",
                "Starting KROT runtime for services="
                + string.Join(",", options.Services.Distinct())
                + ".");

            _stateMachine.TransitionTo(AppState.TestingDirect);
            ZapretRuntimePlan plan;
            var allTcpReachable = true;
            string? networkFingerprint = null;
            PresetSelection? selection = null;
            IReadOnlyDictionary<ServiceId, bool?>? tcpReachability = null;
            var externalTunnelDetected = false;
            if (_isFakeRuntime)
            {
                plan = _presetCatalog.Build(options);
                selection = new PresetSelection();
                tcpReachability = options.Services
                    .Where(serviceId =>
                        serviceId is ServiceId.Discord or ServiceId.YouTube)
                    .Distinct()
                    .ToDictionary(serviceId => serviceId, _ => (bool?)true);
                _stateMachine.TransitionTo(AppState.TestingSavedProfiles);
                if (plan.HasMain)
                {
                    await _processManager
                        .StartMainAsync(plan.MainArguments, startCancellation.Token)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                var network = _networkInspector.Inspect();
                networkFingerprint = network.FingerprintSha256;
                externalTunnelDetected = network.VpnLikely;
                if (network.VpnLikely)
                {
                    _log.Info(
                        "network.external-tunnel.detected",
                        "VPN or system proxy detected: "
                        + network.DetectionReason
                        + ".");
                }

                var searchTimer = Stopwatch.StartNew();
                var outcome = await _presetSearch
                    .StartMainAsync(
                        options,
                        network.FingerprintSha256,
                        allowSearch: true,
                        OnPresetSearchStageChanged,
                        startCancellation.Token)
                    .ConfigureAwait(false);
                searchTimer.Stop();
                _log.Info(
                    "preset.selection.completed",
                    $"TCP preset selection completed in {searchTimer.ElapsedMilliseconds} ms; "
                    + $"all-reachable={outcome.AllTcpReachable}.");
                plan = outcome.Plan;
                allTcpReachable = outcome.AllTcpReachable;
                selection = outcome.Selection;
                tcpReachability = outcome.TcpReachability;
            }

            SetActiveSession(
                options,
                networkFingerprint,
                selection,
                tcpReachability,
                externalTunnelDetected);
            if (plan.HasVoice)
            {
                await _processManager
                    .StartVoiceAsync(plan.VoiceArguments, startCancellation.Token)
                    .ConfigureAwait(false);
            }

            startCancellation.Token.ThrowIfCancellationRequested();
            _stateMachine.TransitionTo(
                allTcpReachable ? AppState.Running : AppState.PartialFailure);
            _log.Info(
                "runtime.running",
                $"KROT runtime active. presets={string.Join(",", plan.PresetIds)}.");
            if (!_isFakeRuntime)
            {
                await StartHealthMonitorAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            await CancelHealthMonitorAsync().ConfigureAwait(false);
            if (gateHeld)
            {
                await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
        catch (Exception ex)
        {
            await CancelHealthMonitorAsync().ConfigureAwait(false);
            ClearActiveSession();
            _log.Error("runtime.start.failed", "Failed to start KROT runtime.", ex);
            try
            {
                await _processManager
                    .StopAllOwnedAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                _log.Error(
                    "runtime.start.cleanup.failed",
                    "Failed to clean up KROT-owned processes after a start error.",
                    cleanupException);
            }

            if (_stateMachine.CanTransitionTo(AppState.FatalError))
            {
                _stateMachine.TransitionTo(AppState.FatalError);
            }

            throw;
        }
        finally
        {
            if (gateHeld)
            {
                _gate.Release();
            }

            lock (_startSync)
            {
                if (ReferenceEquals(_activeStartCancellation, startCancellation))
                {
                    _activeStartCancellation = null;
                }
            }

            startCancellation.Dispose();
        }
    }

    private void OnPresetSearchStageChanged(PresetSearchStage stage)
    {
        if (stage == PresetSearchStage.TestingDirect)
        {
            return;
        }

        if (_stateMachine.State == AppState.TestingDirect)
        {
            _stateMachine.TransitionTo(AppState.TestingSavedProfiles);
        }

        if (stage == PresetSearchStage.Searching
            && _stateMachine.State == AppState.TestingSavedProfiles)
        {
            _stateMachine.TransitionTo(AppState.SearchingProfiles);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_startSync)
        {
            _activeStartCancellation?.Cancel();
        }

        await CancelHealthMonitorAsync().ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CancelHealthMonitorAsync().ConfigureAwait(false);
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RefreshServiceAsync(
        ServiceId serviceId,
        CancellationToken cancellationToken)
    {
        using var refreshCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_startSync)
        {
            if (_activeStartCancellation != null)
            {
                throw new InvalidOperationException(
                    "Another runtime operation is already in progress.");
            }

            _activeStartCancellation = refreshCancellation;
        }

        var gateHeld = false;
        try
        {
            await _gate
                .WaitAsync(refreshCancellation.Token)
                .ConfigureAwait(false);
            gateHeld = true;
            KrotStartOptions options;
            PresetSelection selection;
            bool refreshConfirmedVoice;
            lock (_sessionSync)
            {
                if (_activeOptions == null
                    || _activeSelection == null
                    || !_activeOptions.Services.Contains(serviceId)
                    || _stateMachine.State is not (
                        AppState.Running or AppState.PartialFailure))
                {
                    throw new InvalidOperationException(
                        "The requested service is not active.");
                }

                options = CloneOptions(_activeOptions)!;
                selection = _activeSelection.Clone();
                refreshConfirmedVoice =
                    serviceId == ServiceId.Discord
                    && _confirmedUdpChannels.Contains("discord_voice");
                _refreshingTcpServices.Add(serviceId);
            }

            var network = _networkInspector.Inspect();
            SetExternalTunnelDetected(network.VpnLikely, network.DetectionReason);
            PresetSearchOutcome outcome;
            await _tcpProbeGate
                .WaitAsync(refreshCancellation.Token)
                .ConfigureAwait(false);
            try
            {
                outcome = await _presetSearch
                    .RefreshServiceAsync(
                        options,
                        network.FingerprintSha256,
                        selection,
                        serviceId,
                        allowSearch: true,
                        refreshCancellation.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                _tcpProbeGate.Release();
            }

            lock (_sessionSync)
            {
                _activeSelection = outcome.Selection.Clone();
                _activeNetworkFingerprint = network.FingerprintSha256;
                _tcpReachability[serviceId] = outcome.AllTcpReachable;
                if (refreshConfirmedVoice)
                {
                    _confirmedUdpChannels.Remove("discord_voice");
                    _failedUdpChannels.Remove("discord_voice");
                    _activeUdpChannels.Remove("discord_voice");
                    _udpAttemptGenerations.Remove("discord_voice");
                }
            }

            if (refreshConfirmedVoice && outcome.Plan.HasVoice)
            {
                await _processManager
                    .RestartVoiceAsync(
                        outcome.Plan.VoiceArguments,
                        refreshCancellation.Token)
                    .ConfigureAwait(false);
            }

            var targetState = ActiveTcpChannelsReachable()
                ? AppState.Running
                : AppState.PartialFailure;
            if (_stateMachine.State != targetState
                && _stateMachine.CanTransitionTo(targetState))
            {
                _stateMachine.TransitionTo(targetState);
            }
        }
        finally
        {
            lock (_sessionSync)
            {
                _refreshingTcpServices.Remove(serviceId);
            }

            if (gateHeld)
            {
                _gate.Release();
            }

            lock (_startSync)
            {
                if (ReferenceEquals(
                        _activeStartCancellation,
                        refreshCancellation))
                {
                    _activeStartCancellation = null;
                }
            }
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        if (_stateMachine.State == AppState.Off)
        {
            return;
        }

        if (_stateMachine.CanTransitionTo(AppState.Stopping))
        {
            _stateMachine.TransitionTo(AppState.Stopping);
        }

        await _processManager.StopAllOwnedAsync(cancellationToken).ConfigureAwait(false);
        ClearActiveSession();
        _stateMachine.TransitionTo(AppState.Off);
        _log.Info("runtime.stopped", "All KROT-owned processes stopped.");
    }

    private void OnRuntimeOutput(object? sender, RuntimeOutputEvent eventArgs)
    {
        if (eventArgs.IsError
            || !string.Equals(
                eventArgs.Role,
                "voice",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (UdpActivityMarkerParser.TryParse(
                eventArgs.Line,
                out var activityMarker))
        {
            RememberUdpActivity(activityMarker);
            return;
        }

        if (UdpWinnerMarkerParser.TryParse(eventArgs.Line, out var winnerMarker))
        {
            _ = RememberUdpWinnerAsync(winnerMarker);
        }
    }

    private void RememberUdpActivity(UdpActivityMarker marker)
    {
        if (!string.Equals(
                marker.Channel,
                "discord_voice",
                StringComparison.Ordinal))
        {
            return;
        }

        long generation;
        lock (_sessionSync)
        {
            if (_activeOptions == null
                || !_activeOptions.Services.Contains(ServiceId.Discord)
                || _activeOptions.SkipVoice
                || _confirmedUdpChannels.Contains(marker.Channel))
            {
                return;
            }

            _activeUdpChannels.Add(marker.Channel);
            _failedUdpChannels.Remove(marker.Channel);
            generation = ++_nextUdpAttemptGeneration;
            _udpAttemptGenerations[marker.Channel] = generation;
        }

        _log.Detail(
            "preset.udp.activity",
            "Discord voice traffic detected; checking adaptive strategies.");
        _ = MarkUdpFailureAfterTimeoutAsync(marker.Channel, generation);
    }

    private async Task MarkUdpFailureAfterTimeoutAsync(
        string channel,
        long generation)
    {
        await Task.Delay(_udpConfirmationTimeout).ConfigureAwait(false);

        lock (_sessionSync)
        {
            if (_activeOptions == null
                || _confirmedUdpChannels.Contains(channel)
                || !_activeUdpChannels.Contains(channel)
                || !_udpAttemptGenerations.TryGetValue(
                    channel,
                    out var currentGeneration)
                || currentGeneration != generation)
            {
                return;
            }

            _activeUdpChannels.Remove(channel);
            _failedUdpChannels.Add(channel);
            _udpAttemptGenerations.Remove(channel);
        }

        _log.Info(
            "preset.udp.failed",
            "Discord voice traffic was detected, but no strategy was confirmed. See detailed logs.");
    }

    private async Task RememberUdpWinnerAsync(UdpWinnerMarker marker)
    {
        try
        {
            string? fingerprint;
            PresetSelection? selection;
            lock (_sessionSync)
            {
                if (_activeOptions == null)
                {
                    return;
                }

                _confirmedUdpChannels.Add(marker.Channel);
                _activeUdpChannels.Remove(marker.Channel);
                _failedUdpChannels.Remove(marker.Channel);
                _udpAttemptGenerations.Remove(marker.Channel);
                if (_activeSelection == null
                    || string.IsNullOrWhiteSpace(_activeNetworkFingerprint))
                {
                    return;
                }

                var current = marker.Channel == "youtube_quic"
                    ? _activeSelection.YouTubeQuic
                    : _activeSelection.DiscordVoice;
                if (string.Equals(current, marker.StrategyId, StringComparison.Ordinal))
                {
                    return;
                }

                if (marker.Channel == "youtube_quic")
                {
                    _activeSelection.YouTubeQuic = marker.StrategyId;
                }
                else
                {
                    _activeSelection.DiscordVoice = marker.StrategyId;
                }

                fingerprint = _activeNetworkFingerprint;
                selection = _activeSelection.Clone();
            }

            await _presetSearch
                .SaveSelectionAsync(fingerprint!, selection!, CancellationToken.None)
                .ConfigureAwait(false);
            _log.Info(
                "preset.udp.winner",
                $"Saved {marker.StrategyId} for {marker.Channel}.");
        }
        catch (Exception ex)
        {
            _log.Error(
                "preset.udp.save.failed",
                "Failed to save an adaptive UDP winner.",
                ex);
        }
    }

    private async Task StartHealthMonitorAsync()
    {
        await CancelHealthMonitorAsync().ConfigureAwait(false);
        while (_healthSignal.Wait(0))
        {
        }

        var cancellation = new CancellationTokenSource();
        lock (_healthSync)
        {
            _healthCancellation = cancellation;
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            _networkChangeSubscribed = true;
            _healthTask = Task.WhenAll(
                MonitorHealthAsync(cancellation.Token),
                MonitorExternalTunnelAsync(cancellation.Token));
        }
    }

    private async Task CancelHealthMonitorAsync()
    {
        CancellationTokenSource? cancellation;
        Task? task;
        lock (_healthSync)
        {
            if (_networkChangeSubscribed)
            {
                NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
                _networkChangeSubscribed = false;
            }

            cancellation = _healthCancellation;
            task = _healthTask;
            _healthCancellation = null;
            _healthTask = null;
        }

        if (cancellation == null)
        {
            return;
        }

        cancellation.Cancel();
        try
        {
            if (task != null)
            {
                await task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Monitor cancellation is the expected shutdown path.
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs eventArgs)
    {
        try
        {
            _healthSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Several adapter notifications can describe the same network change.
        }
    }

    private async Task MonitorHealthAsync(CancellationToken cancellationToken)
    {
        var failures = new Dictionary<ServiceId, int>();
        var wasOffline = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var networkChanged = await _healthSignal
                    .WaitAsync(HealthCheckInterval, cancellationToken)
                    .ConfigureAwait(false);
                if (networkChanged)
                {
                    await Task
                        .Delay(NetworkChangeSettleDelay, cancellationToken)
                        .ConfigureAwait(false);
                }

                KrotStartOptions? options;
                string? fingerprint;
                lock (_sessionSync)
                {
                    options = CloneOptions(_activeOptions);
                    fingerprint = _activeNetworkFingerprint;
                }

                if (options == null)
                {
                    return;
                }

                var network = _networkInspector.Inspect();
                SetExternalTunnelDetected(network.VpnLikely, network.DetectionReason);

                var internetAvailable = await _internetProbe
                    .CheckAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!internetAvailable)
                {
                    failures.Clear();
                    wasOffline = true;
                    SetInternetUnavailable(true);
                    continue;
                }

                SetInternetUnavailable(false);
                if (wasOffline)
                {
                    await Task
                        .Delay(NetworkRecoveryDelay, cancellationToken)
                        .ConfigureAwait(false);
                    network = _networkInspector.Inspect();
                    SetExternalTunnelDetected(network.VpnLikely, network.DetectionReason);

                    if (!await _internetProbe
                            .CheckAsync(cancellationToken)
                            .ConfigureAwait(false))
                    {
                        SetInternetUnavailable(true);
                        continue;
                    }

                    wasOffline = false;
                    _log.Info(
                        "runtime.internet.restored",
                        "Internet connectivity restored; validating the active preset.");
                }

                var services = options.Services
                    .Where(serviceId =>
                        serviceId is ServiceId.Discord or ServiceId.YouTube)
                    .Distinct()
                    .ToArray();
                BeginTcpReachabilityCheck(services);
                IReadOnlyDictionary<ServiceId, bool> results;
                await _tcpProbeGate
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                try
                {
                    results = await _presetSearch
                        .ProbeTcpAsync(services, cancellationToken)
                        .ConfigureAwait(false);
                    UpdateTcpReachability(results);
                }
                finally
                {
                    _tcpProbeGate.Release();
                }
                foreach (var result in results)
                {
                    failures[result.Key] = result.Value
                        ? 0
                        : failures.TryGetValue(result.Key, out var count)
                            ? count + 1
                            : 1;
                }

                var fingerprintChanged = !string.Equals(
                    network.FingerprintSha256,
                    fingerprint,
                    StringComparison.Ordinal);
                if (fingerprintChanged && results.All(result => result.Value))
                {
                    await AdoptNetworkAsync(
                            network.FingerprintSha256,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (results.Any(result => !result.Value && failures[result.Key] >= 2))
                {
                    await RepairRuntimeAsync(
                            options,
                            network,
                            results
                                .Where(result =>
                                    !result.Value && failures[result.Key] >= 2)
                                .Select(result => result.Key)
                                .ToArray(),
                            cancellationToken)
                        .ConfigureAwait(false);
                    failures.Clear();
                }
                else if (results.All(result => result.Value)
                         && _stateMachine.State == AppState.PartialFailure
                         && _stateMachine.CanTransitionTo(AppState.Running))
                {
                    _stateMachine.TransitionTo(AppState.Running);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log.Error(
                "runtime.health.failed",
                "Background channel health monitoring failed.",
                ex);
        }
    }

    private async Task MonitorExternalTunnelAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task
                    .Delay(ExternalTunnelCheckInterval, cancellationToken)
                    .ConfigureAwait(false);

                lock (_sessionSync)
                {
                    if (_activeOptions == null)
                    {
                        return;
                    }
                }

                var network = _networkInspector.Inspect();
                var changed = SetExternalTunnelDetected(
                    network.VpnLikely,
                    network.DetectionReason);
                if (changed && !network.VpnLikely)
                {
                    OnNetworkAddressChanged(this, EventArgs.Empty);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log.Error(
                "network.external-tunnel.monitor.failed",
                "VPN and system proxy monitoring failed.",
                ex);
        }
    }

    private void SetInternetUnavailable(bool unavailable)
    {
        lock (_sessionSync)
        {
            _internetUnavailable = unavailable;
        }
    }

    private bool SetExternalTunnelDetected(bool detected, string? reason = null)
    {
        var changed = false;
        lock (_sessionSync)
        {
            if (_externalTunnelDetected != detected)
            {
                _externalTunnelDetected = detected;
                changed = true;
            }
        }

        if (changed)
        {
            _log.Info(
                detected
                    ? "network.external-tunnel.detected"
                    : "network.external-tunnel.cleared",
                detected
                    ? "VPN or system proxy detected"
                      + (string.IsNullOrWhiteSpace(reason) ? "." : $": {reason}.")
                    : "VPN or system proxy is no longer detected.");
        }

        return changed;
    }

    private void BeginTcpReachabilityCheck(
        IEnumerable<ServiceId> services)
    {
        var visibleUntilUtc =
            DateTime.UtcNow + TcpCheckIndicatorDuration;
        lock (_sessionSync)
        {
            foreach (var serviceId in services)
            {
                _tcpCheckingUntilUtc[serviceId] = visibleUntilUtc;
            }
        }
    }

    private void UpdateTcpReachability(
        IReadOnlyDictionary<ServiceId, bool> results)
    {
        lock (_sessionSync)
        {
            foreach (var result in results)
            {
                _tcpReachability[result.Key] = result.Value;
            }
        }
    }

    private async Task AdoptNetworkAsync(
        string networkFingerprint,
        CancellationToken cancellationToken)
    {
        PresetSelection? selection;
        lock (_sessionSync)
        {
            if (_activeSelection == null)
            {
                return;
            }

            _activeNetworkFingerprint = networkFingerprint;
            selection = _activeSelection.Clone();
        }

        await _presetSearch
            .SaveSelectionAsync(
                networkFingerprint,
                selection,
                cancellationToken)
            .ConfigureAwait(false);
        _log.Info(
            "runtime.network.adopted",
            "The active preset works on the new network and was saved without a restart.");
    }

    private async Task RepairRuntimeAsync(
        KrotStartOptions options,
        NetworkEnvironmentSummary network,
        IReadOnlyCollection<ServiceId> failedServices,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stateMachine.State is not (AppState.Running or AppState.PartialFailure))
            {
                return;
            }

            _log.Info(
                "runtime.repair.starting",
                "A service channel failed twice while the internet remained available; selecting a working preset.");
            foreach (var serviceId in failedServices)
            {
                PresetSelection selection;
                lock (_sessionSync)
                {
                    selection = _activeSelection?.Clone()
                        ?? new PresetSelection();
                    _refreshingTcpServices.Add(serviceId);
                }

                PresetSearchOutcome outcome;
                await _tcpProbeGate
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                try
                {
                    outcome = await _presetSearch
                        .RefreshServiceAsync(
                            options,
                            network.FingerprintSha256,
                            selection,
                            serviceId,
                            allowSearch: true,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    _tcpProbeGate.Release();
                }
                lock (_sessionSync)
                {
                    _activeSelection = outcome.Selection.Clone();
                    _activeNetworkFingerprint = network.FingerprintSha256;
                    _tcpReachability[serviceId] = outcome.AllTcpReachable;
                    _refreshingTcpServices.Remove(serviceId);
                }
            }

            var targetState = ActiveTcpChannelsReachable()
                ? AppState.Running
                : AppState.PartialFailure;
            if (_stateMachine.CanTransitionTo(targetState))
            {
                _stateMachine.TransitionTo(targetState);
            }

            _log.Info(
                "runtime.repair.complete",
                "Problematic service channels were reselected independently.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (_stateMachine.CanTransitionTo(AppState.PartialFailure))
            {
                _stateMachine.TransitionTo(AppState.PartialFailure);
            }

            _log.Error(
                "runtime.repair.failed",
                "Failed to repair the active runtime.",
                ex);
        }
        finally
        {
            lock (_sessionSync)
            {
                foreach (var serviceId in failedServices)
                {
                    _refreshingTcpServices.Remove(serviceId);
                }
            }

            _gate.Release();
        }
    }

    private void SetActiveSession(
        KrotStartOptions options,
        string? networkFingerprint,
        PresetSelection? selection,
        IReadOnlyDictionary<ServiceId, bool?>? tcpReachability,
        bool externalTunnelDetected = false)
    {
        lock (_sessionSync)
        {
            _activeOptions = CloneOptions(options);
            _activeNetworkFingerprint = networkFingerprint;
            _activeSelection = selection?.Clone();
            _tcpReachability.Clear();
            _tcpCheckingUntilUtc.Clear();
            _refreshingTcpServices.Clear();
            if (tcpReachability != null)
            {
                foreach (var item in tcpReachability)
                {
                    _tcpReachability[item.Key] = item.Value;
                }
            }

            _confirmedUdpChannels.Clear();
            _activeUdpChannels.Clear();
            _failedUdpChannels.Clear();
            _udpAttemptGenerations.Clear();
            _internetUnavailable = false;
            _externalTunnelDetected = externalTunnelDetected;
        }
    }

    private void ClearActiveSession()
    {
        lock (_sessionSync)
        {
            _activeOptions = null;
            _activeNetworkFingerprint = null;
            _activeSelection = null;
            _tcpReachability.Clear();
            _tcpCheckingUntilUtc.Clear();
            _refreshingTcpServices.Clear();
            _confirmedUdpChannels.Clear();
            _activeUdpChannels.Clear();
            _failedUdpChannels.Clear();
            _udpAttemptGenerations.Clear();
            _internetUnavailable = false;
            _externalTunnelDetected = false;
        }
    }

    private ServiceSnapshot BuildSnapshot()
    {
        lock (_sessionSync)
        {
            var snapshot = new ServiceSnapshot
            {
                AppState = _stateMachine.State,
                MessageKey = MessageKeyFor(_stateMachine.State),
                IsFakeRuntime = _isFakeRuntime,
                ExternalTunnelDetected = _externalTunnelDetected
            };
            if (_activeOptions == null)
            {
                return snapshot;
            }

            AddChannel(snapshot, ServiceId.Discord, "text");
            AddChannel(snapshot, ServiceId.Discord, "media");
            AddChannel(snapshot, ServiceId.Discord, "voice");
            AddChannel(snapshot, ServiceId.YouTube, "site");
            AddChannel(snapshot, ServiceId.YouTube, "video");
            return snapshot;
        }
    }

    private void AddChannel(
        ServiceSnapshot snapshot,
        ServiceId serviceId,
        string channelId)
    {
        var selected = _activeOptions?.Services.Contains(serviceId) == true;
        var state = selected
            ? ActiveChannelState(serviceId, channelId)
            : ChannelState.Disabled;
        snapshot.Channels.Add(new ChannelSnapshot
        {
            ServiceId = serviceId,
            ChannelId = channelId,
            State = state,
            StrategyId = ChannelStrategy(serviceId, channelId)
        });
    }

    private ChannelState ActiveChannelState(
        ServiceId serviceId,
        string channelId)
    {
        if (_stateMachine.State is AppState.Starting or AppState.TestingDirect)
        {
            return ChannelState.Testing;
        }

        if (_stateMachine.State is AppState.TestingSavedProfiles
            or AppState.SearchingProfiles)
        {
            return ChannelState.Searching;
        }

        if (_stateMachine.State == AppState.Stopping)
        {
            return ChannelState.Testing;
        }

        if (_stateMachine.State == AppState.FatalError)
        {
            return ChannelState.Failed;
        }

        if (_stateMachine.State == AppState.Off || _internetUnavailable)
        {
            return ChannelState.Unknown;
        }

        if (serviceId == ServiceId.Discord && channelId == "voice")
        {
            if (_activeOptions?.SkipVoice == true)
            {
                return ChannelState.Disabled;
            }

            if (_confirmedUdpChannels.Contains("discord_voice"))
            {
                return ChannelState.WorkingPreset;
            }

            if (_failedUdpChannels.Contains("discord_voice"))
            {
                return ChannelState.Failed;
            }

            return _activeUdpChannels.Contains("discord_voice")
                ? ChannelState.Testing
                : ChannelState.WaitingForActivity;
        }

        if (_refreshingTcpServices.Contains(serviceId))
        {
            return ChannelState.Searching;
        }

        if (_tcpCheckingUntilUtc.TryGetValue(serviceId, out var checkingUntilUtc))
        {
            if (checkingUntilUtc > DateTime.UtcNow)
            {
                return ChannelState.Testing;
            }

            _tcpCheckingUntilUtc.Remove(serviceId);
        }

        if (serviceId == ServiceId.YouTube
            && channelId == "video"
            && _confirmedUdpChannels.Contains("youtube_quic"))
        {
            var tcp = TcpChannelState(ServiceId.YouTube);
            return tcp == ChannelState.WorkingDirect
                ? ChannelState.WorkingDirect
                : ChannelState.WorkingPreset;
        }

        return TcpChannelState(serviceId);
    }

    private ChannelState TcpChannelState(ServiceId serviceId)
    {
        if (!_tcpReachability.TryGetValue(serviceId, out var reachable)
            || !reachable.HasValue)
        {
            return ChannelState.WaitingForActivity;
        }

        if (!reachable.Value)
        {
            return ChannelState.Failed;
        }

        var strategy = serviceId switch
        {
            ServiceId.Discord => _activeSelection?.DiscordTcp,
            ServiceId.YouTube => _activeSelection?.YouTubeTcp,
            _ => null
        };
        return string.Equals(
            strategy,
            PresetSelection.Direct,
            StringComparison.Ordinal)
            ? ChannelState.WorkingDirect
            : ChannelState.WorkingPreset;
    }

    private bool ActiveTcpChannelsReachable()
    {
        lock (_sessionSync)
        {
            if (_activeOptions == null)
            {
                return false;
            }

            return _activeOptions.Services
                .Where(serviceId =>
                    serviceId is ServiceId.Discord or ServiceId.YouTube)
                .Distinct()
                .All(serviceId =>
                    _tcpReachability.TryGetValue(serviceId, out var reachable)
                    && reachable == true);
        }
    }

    private string ChannelStrategy(ServiceId serviceId, string channelId)
    {
        if (_activeSelection == null)
        {
            return string.Empty;
        }

        if (serviceId == ServiceId.Discord)
        {
            return channelId == "voice"
                ? _activeSelection.DiscordVoice
                : _activeSelection.DiscordTcp;
        }

        if (serviceId == ServiceId.YouTube)
        {
            return channelId == "video"
                   && _confirmedUdpChannels.Contains("youtube_quic")
                ? _activeSelection.YouTubeQuic
                : _activeSelection.YouTubeTcp;
        }

        return string.Empty;
    }

    private static KrotStartOptions? CloneOptions(KrotStartOptions? options)
    {
        if (options == null)
        {
            return null;
        }

        return new KrotStartOptions
        {
            Services = options.Services.ToList(),
            DetailedLogs = options.DetailedLogs,
            SkipVoice = options.SkipVoice
        };
    }

    private static string MessageKeyFor(AppState state)
    {
        return state switch
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
}
