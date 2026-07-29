using System;
using System.Linq;

namespace KROT.Core.Updates;

public sealed class ReleaseVersion : IComparable<ReleaseVersion>
{
    private readonly Version _value;

    private ReleaseVersion(Version value)
    {
        _value = value;
    }

    public static bool TryParse(string? value, out ReleaseVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value!.Trim();
        var parts = normalized.Split('.');
        if (parts.Length < 2
            || parts.Length > 4
            || parts.Any(part =>
                part.Length == 0
                || !part.All(char.IsDigit)
                || !int.TryParse(part, out _))
            || !Version.TryParse(normalized, out var parsed))
        {
            return false;
        }

        version = new ReleaseVersion(parsed);
        return true;
    }

    public int CompareTo(ReleaseVersion? other) =>
        other == null ? 1 : _value.CompareTo(other._value);
}
