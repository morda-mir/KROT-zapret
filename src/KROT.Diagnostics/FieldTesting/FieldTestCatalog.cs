using System.Collections.Generic;

namespace KROT.Diagnostics.FieldTesting;

public static class FieldTestCatalog
{
    public static IReadOnlyList<FieldTestEndpoint> Default { get; } = new[]
    {
        new FieldTestEndpoint("discord", "discord.com", "/api/v10/gateway"),
        new FieldTestEndpoint("youtube", "www.youtube.com", "/generate_204"),
        new FieldTestEndpoint("ai-services", "chatgpt.com", "/")
    };
}

public sealed class FieldTestEndpoint
{
    public FieldTestEndpoint(string serviceId, string host, string path)
    {
        ServiceId = serviceId;
        Host = host;
        Path = path;
    }

    public string ServiceId { get; }

    public string Host { get; }

    public string Path { get; }
}
