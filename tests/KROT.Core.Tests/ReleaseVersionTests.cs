using KROT.Core.Updates;
using Xunit;

namespace KROT.Core.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.1", "1.0")]
    [InlineData("2.0", "1.9")]
    [InlineData("1.0.1", "1.0")]
    public void CompareTo_OrdersNumericReleaseVersions(
        string newerValue,
        string olderValue)
    {
        Assert.True(ReleaseVersion.TryParse(newerValue, out var newer));
        Assert.True(ReleaseVersion.TryParse(olderValue, out var older));

        Assert.True(newer!.CompareTo(older) > 0);
        Assert.True(older!.CompareTo(newer) < 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1")]
    [InlineData("v1.0")]
    [InlineData("1.0-dev")]
    [InlineData("1.0-preview.1")]
    public void TryParse_RejectsNonNumericReleaseNames(string value)
    {
        Assert.False(ReleaseVersion.TryParse(value, out _));
    }
}
