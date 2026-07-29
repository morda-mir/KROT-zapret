using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Core.Models;

namespace KROT.Zapret.Profiles;

public enum PresetSearchStage
{
    TestingDirect,
    TestingSaved,
    Searching
}

public interface IPresetReachabilityProbe
{
    Task<bool> CheckAsync(ServiceId serviceId, CancellationToken cancellationToken);
}

public interface IPresetSelectionCache
{
    Task<PresetSelection?> LoadAsync(
        string networkFingerprint,
        CancellationToken cancellationToken);

    Task SaveAsync(
        string networkFingerprint,
        PresetSelection selection,
        CancellationToken cancellationToken);
}

public sealed class PresetSearchOutcome
{
    public PresetSelection Selection { get; set; } = new();

    public ZapretRuntimePlan Plan { get; set; } = new();

    public bool AllTcpReachable { get; set; } = true;

    public IReadOnlyDictionary<ServiceId, bool?> TcpReachability { get; set; } =
        new Dictionary<ServiceId, bool?>();
}

public sealed class AdaptivePresetSearchEngine
{
    private readonly IZapretProcessManager _processManager;
    private readonly BuiltInPresetCatalog _presetCatalog;
    private readonly IPresetReachabilityProbe _probe;
    private readonly IPresetSelectionCache _cache;
    private readonly ILogService _log;

    public AdaptivePresetSearchEngine(
        IZapretProcessManager processManager,
        BuiltInPresetCatalog presetCatalog,
        IPresetReachabilityProbe probe,
        IPresetSelectionCache cache,
        ILogService log)
    {
        _processManager = processManager;
        _presetCatalog = presetCatalog;
        _probe = probe;
        _cache = cache;
        _log = log;
    }

    public async Task<PresetSearchOutcome> StartMainAsync(
        KrotStartOptions options,
        string networkFingerprint,
        bool allowSearch,
        Action<PresetSearchStage>? stageChanged,
        CancellationToken cancellationToken)
    {
        var selected = new HashSet<ServiceId>(
            options.Services.Where(IsTcpService));
        var cached = await _cache
            .LoadAsync(networkFingerprint, cancellationToken)
            .ConfigureAwait(false);
        var chosen = cached?.Clone() ?? new PresetSelection();
        Normalize(chosen, directAllowed: allowSearch);

        if (selected.Count == 0)
        {
            var noTcpPlan = _presetCatalog.Build(options, chosen);
            return new PresetSearchOutcome
            {
                Selection = chosen,
                Plan = noTcpPlan,
                AllTcpReachable = true,
                TcpReachability = Reachability(selected, unresolved: null, assumed: true)
            };
        }

        if (!allowSearch)
        {
            stageChanged?.Invoke(PresetSearchStage.TestingSaved);
            var vpnPlan = _presetCatalog.Build(options, chosen);
            await StartOrReplaceMainAsync(vpnPlan, cancellationToken).ConfigureAwait(false);
            _log.Info(
                "preset.search.skipped",
                "VPN or proxy detected; adaptive probing skipped and cached/default presets used.");
            return new PresetSearchOutcome
            {
                Selection = chosen,
                Plan = vpnPlan,
                AllTcpReachable = true,
                TcpReachability = Reachability(selected, unresolved: null, assumed: null)
            };
        }

        var unresolved = new HashSet<ServiceId>(selected);
        PresetSelection? runningSelection = null;

        if (cached != null)
        {
            stageChanged?.Invoke(PresetSearchStage.TestingSaved);
            var savedTrial = chosen.Clone();
            var savedPlan = _presetCatalog.Build(options, savedTrial);
            await StartOrReplaceMainAsync(savedPlan, cancellationToken).ConfigureAwait(false);
            runningSelection = savedTrial;
            await AcceptReachableAsync(
                    chosen,
                    unresolved,
                    savedTrial,
                    unresolved.ToArray(),
                    cancellationToken)
                .ConfigureAwait(false);

            if (unresolved.Count == 0)
            {
                await _cache
                    .SaveAsync(networkFingerprint, chosen, cancellationToken)
                    .ConfigureAwait(false);
                return new PresetSearchOutcome
                {
                    Selection = chosen,
                    Plan = savedPlan,
                    AllTcpReachable = true,
                    TcpReachability = Reachability(selected, unresolved, assumed: null)
                };
            }
        }

        var directServices = unresolved
            .Where(serviceId =>
                cached == null
                || !string.Equals(
                    GetTcp(cached, serviceId),
                    PresetSelection.Direct,
                    StringComparison.Ordinal))
            .ToArray();
        if (directServices.Length > 0)
        {
            stageChanged?.Invoke(PresetSearchStage.TestingDirect);
            var directTrial = chosen.Clone();
            foreach (var serviceId in directServices)
            {
                SetTcp(directTrial, serviceId, PresetSelection.Direct);
            }

            var directPlan = _presetCatalog.Build(options, directTrial);
            await StartOrReplaceMainAsync(directPlan, cancellationToken).ConfigureAwait(false);
            runningSelection = directTrial;
            await AcceptReachableAsync(
                    chosen,
                    unresolved,
                    directTrial,
                    directServices,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (unresolved.Count == 0)
        {
            var reachablePlan = _presetCatalog.Build(options, chosen);
            if (!SameMainSelection(runningSelection, chosen, selected))
            {
                await StartOrReplaceMainAsync(reachablePlan, cancellationToken)
                .ConfigureAwait(false);
            }

            await _cache
                .SaveAsync(networkFingerprint, chosen, cancellationToken)
                .ConfigureAwait(false);
            return new PresetSearchOutcome
            {
                Selection = chosen,
                Plan = reachablePlan,
                AllTcpReachable = true,
                TcpReachability = Reachability(selected, unresolved, assumed: null)
            };
        }

        if (unresolved.Count > 0)
        {
            stageChanged?.Invoke(PresetSearchStage.Searching);
            var candidates = unresolved.ToDictionary(
                serviceId => serviceId,
                serviceId => new Queue<StrategyDescriptor>(
                    BuiltInStrategyCatalog.Tcp.Where(strategy =>
                        cached == null
                        || !string.Equals(
                            strategy.Id,
                            GetTcp(cached, serviceId),
                            StringComparison.Ordinal))));
            while (unresolved.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var trial = chosen.Clone();
                var attempted = new List<ServiceId>();
                foreach (var serviceId in unresolved.ToArray())
                {
                    if (candidates[serviceId].Count == 0)
                    {
                        continue;
                    }

                    var strategy = candidates[serviceId].Dequeue();
                    SetTcp(trial, serviceId, strategy.Id);
                    attempted.Add(serviceId);
                }

                if (attempted.Count == 0)
                {
                    break;
                }

                var plan = _presetCatalog.Build(options, trial);
                await StartOrReplaceMainAsync(plan, cancellationToken).ConfigureAwait(false);
                runningSelection = trial;
                _log.Detail(
                    "preset.search.try",
                    $"Testing TCP strategies for {string.Join(",", attempted.Select(serviceId => $"{serviceId}={GetTcp(trial, serviceId)}"))}.");

                await AcceptReachableAsync(
                        chosen,
                        unresolved,
                        trial,
                        attempted,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        foreach (var serviceId in unresolved)
        {
            SetTcp(chosen, serviceId, BuiltInStrategyCatalog.DefaultTcpId);
            if (serviceId == ServiceId.YouTube)
            {
                chosen.YouTubeQuic = BuiltInStrategyCatalog.DefaultQuicId;
            }
        }

        var finalPlan = _presetCatalog.Build(options, chosen);
        if (!SameMainSelection(runningSelection, chosen, selected))
        {
            await StartOrReplaceMainAsync(finalPlan, cancellationToken).ConfigureAwait(false);
        }

        await _cache.SaveAsync(networkFingerprint, chosen, cancellationToken).ConfigureAwait(false);
        return new PresetSearchOutcome
        {
            Selection = chosen,
            Plan = finalPlan,
            AllTcpReachable = unresolved.Count == 0,
            TcpReachability = Reachability(selected, unresolved, assumed: null)
        };
    }

    private async Task AcceptReachableAsync(
        PresetSelection chosen,
        ISet<ServiceId> unresolved,
        PresetSelection trial,
        IEnumerable<ServiceId> servicesToProbe,
        CancellationToken cancellationToken)
    {
        var results = await ProbeAsync(servicesToProbe, cancellationToken).ConfigureAwait(false);
        foreach (var result in results.Where(item => item.Value))
        {
            SetTcp(chosen, result.Key, GetTcp(trial, result.Key));
            unresolved.Remove(result.Key);
            _log.Info(
                "preset.search.winner",
                $"Selected {GetTcp(chosen, result.Key)} for {result.Key}.");
        }
    }

    private async Task<IReadOnlyDictionary<ServiceId, bool>> ProbeAsync(
        IEnumerable<ServiceId> services,
        CancellationToken cancellationToken)
    {
        var tasks = services.ToDictionary(
            serviceId => serviceId,
            serviceId => _probe.CheckAsync(serviceId, cancellationToken));
        await Task.WhenAll(tasks.Values).ConfigureAwait(false);
        return tasks.ToDictionary(item => item.Key, item => item.Value.Result);
    }

    public Task<IReadOnlyDictionary<ServiceId, bool>> ProbeTcpAsync(
        IEnumerable<ServiceId> services,
        CancellationToken cancellationToken) =>
        ProbeAsync(services.Where(IsTcpService).Distinct(), cancellationToken);

    public async Task SaveSelectionAsync(
        string networkFingerprint,
        PresetSelection selection,
        CancellationToken cancellationToken)
    {
        var normalized = selection.Clone();
        Normalize(normalized, directAllowed: true);
        await _cache
            .SaveAsync(networkFingerprint, normalized, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task StartOrReplaceMainAsync(
        ZapretRuntimePlan plan,
        CancellationToken cancellationToken)
    {
        var hasMain = _processManager.OwnedProcesses.Any(
            item => string.Equals(item.Role, "main", StringComparison.OrdinalIgnoreCase));
        if (!plan.HasMain)
        {
            if (hasMain)
            {
                await _processManager
                    .StopMainAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        if (hasMain)
        {
            await _processManager
                .RestartMainAsync(plan.MainArguments, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await _processManager
                .StartMainAsync(plan.MainArguments, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static void Normalize(PresetSelection selection, bool directAllowed)
    {
        if (!BuiltInStrategyCatalog.IsTcp(selection.DiscordTcp)
            || (!directAllowed && selection.DiscordTcp == PresetSelection.Direct))
        {
            selection.DiscordTcp = BuiltInStrategyCatalog.DefaultTcpId;
        }

        if (!BuiltInStrategyCatalog.IsTcp(selection.YouTubeTcp)
            || (!directAllowed && selection.YouTubeTcp == PresetSelection.Direct))
        {
            selection.YouTubeTcp = BuiltInStrategyCatalog.DefaultTcpId;
        }

        if (!BuiltInStrategyCatalog.IsQuic(selection.YouTubeQuic)
            || selection.YouTubeQuic == PresetSelection.Direct)
        {
            selection.YouTubeQuic = BuiltInStrategyCatalog.DefaultQuicId;
        }

        if (!BuiltInStrategyCatalog.IsVoice(selection.DiscordVoice)
            || selection.DiscordVoice == PresetSelection.Direct)
        {
            selection.DiscordVoice = BuiltInStrategyCatalog.DefaultVoiceId;
        }
    }

    private static bool SameMainSelection(
        PresetSelection? left,
        PresetSelection right,
        IEnumerable<ServiceId> selected)
    {
        if (left == null)
        {
            return !selected.Any();
        }

        return (!selected.Contains(ServiceId.Discord)
                || left.DiscordTcp == right.DiscordTcp)
               && (!selected.Contains(ServiceId.YouTube)
                   || left.YouTubeTcp == right.YouTubeTcp);
    }

    private static bool IsTcpService(ServiceId serviceId) =>
        serviceId is ServiceId.Discord or ServiceId.YouTube;

    private static IReadOnlyDictionary<ServiceId, bool?> Reachability(
        IEnumerable<ServiceId> selected,
        ISet<ServiceId>? unresolved,
        bool? assumed) =>
        selected.ToDictionary(
            serviceId => serviceId,
            serviceId => unresolved == null
                ? assumed
                : !unresolved.Contains(serviceId));

    private static string GetTcp(PresetSelection selection, ServiceId serviceId) =>
        serviceId switch
        {
            ServiceId.Discord => selection.DiscordTcp,
            ServiceId.YouTube => selection.YouTubeTcp,
            _ => PresetSelection.Direct
        };

    private static void SetTcp(
        PresetSelection selection,
        ServiceId serviceId,
        string presetId)
    {
        switch (serviceId)
        {
            case ServiceId.Discord:
                selection.DiscordTcp = presetId;
                break;
            case ServiceId.YouTube:
                selection.YouTubeTcp = presetId;
                break;
        }
    }
}
