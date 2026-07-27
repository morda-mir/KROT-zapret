using System;
using System.Collections.Generic;
using System.Linq;

namespace KROT.Zapret.Arguments;

public sealed class ZapretArgumentBuilder
{
    public IReadOnlyList<string> Build(
        IEnumerable<ZapretProfileArguments> profiles,
        IEnumerable<string> rawWinDivertParts)
    {
        var result = new List<string>();
        foreach (var rawPart in rawWinDivertParts.Where(IsSafeArgument))
        {
            result.Add("--wf-raw-part");
            result.Add(rawPart);
        }

        var first = true;
        foreach (var profile in profiles)
        {
            if (!first)
            {
                result.Add("--new");
            }

            first = false;
            result.AddRange(profile.Arguments.Where(IsSafeArgument));
        }

        return result;
    }

    private static bool IsSafeArgument(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.IndexOfAny(new[] { '\r', '\n', '\0' }) < 0;
}

public sealed class ZapretProfileArguments
{
    public string Id { get; set; } = string.Empty;

    public IReadOnlyList<string> Arguments { get; set; } = Array.Empty<string>();
}

