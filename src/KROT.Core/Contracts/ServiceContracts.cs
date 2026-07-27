using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Models;
using KROT.Core.States;

namespace KROT.Core.Contracts;

public interface IServiceDefinition
{
    ServiceId Id { get; }

    IReadOnlyList<string> ChannelIds { get; }
}

public interface IServiceDiagnostic
{
    Task<ServiceState> CheckAsync(CancellationToken cancellationToken);
}

public interface IChannelDiagnostic
{
    Task<ChannelState> CheckAsync(string channelId, CancellationToken cancellationToken);
}

public interface IPresetProvider
{
    IReadOnlyList<string> GetPresetIds(ServiceId serviceId, string channelId, bool deep);
}

public interface IPresetTester
{
    Task<bool> TestAsync(string presetId, CancellationToken cancellationToken);
}

