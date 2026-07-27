using System.Collections.Generic;
using KROT.Core.Contracts;
using KROT.Core.Models;

namespace KROT.Diagnostics.Definitions;

public sealed class BuiltInServiceDefinition : IServiceDefinition
{
    public BuiltInServiceDefinition(ServiceId id, params string[] channelIds)
    {
        Id = id;
        ChannelIds = channelIds;
    }

    public ServiceId Id { get; }

    public IReadOnlyList<string> ChannelIds { get; }
}

