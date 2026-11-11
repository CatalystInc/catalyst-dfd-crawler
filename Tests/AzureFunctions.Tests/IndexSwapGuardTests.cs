using AzureFunctions.Indexer;

namespace AzureFunctions.Tests;

public class IndexSwapGuardTests
{
    private static bool Allowed(long newCount, long? liveCount, double ratio = 0.8, long minimum = 1) =>
        IndexSwapGuard.GetRejectionReason(newCount, liveCount, ratio, minimum) == null;

    [Fact] public void Rejects_an_empty_index() => Assert.False(Allowed(0, 1000));
    [Fact] public void Rejects_an_empty_index_on_a_first_run() => Assert.False(Allowed(0, null));
    [Fact] public void Allows_a_first_run_with_documents() => Assert.True(Allowed(1, null));
    [Fact] public void Rejects_below_the_ratio() => Assert.False(Allowed(799, 1000));
    [Fact] public void Allows_at_the_ratio() => Assert.True(Allowed(800, 1000));
    [Fact] public void Allows_growth() => Assert.True(Allowed(1200, 1000));
    [Fact] public void Ratio_zero_allows_an_intended_shrink() => Assert.True(Allowed(500, 1000, 0));
    [Fact] public void Ratio_zero_still_rejects_an_empty_index() => Assert.False(Allowed(0, 1000, 0));
    [Fact] public void A_live_count_of_zero_skips_the_ratio() => Assert.True(Allowed(5, 0));
    [Fact] public void Applies_the_minimum_documents() => Assert.False(Allowed(5, null, 0.8, 10));

    [Fact]
    public void The_reason_names_both_counts()
    {
        var reason = IndexSwapGuard.GetRejectionReason(10, 1000, 0.8, 1);
        Assert.NotNull(reason);
        Assert.Contains("10", reason);
        Assert.Contains("1000", reason);
    }

    [Theory]
    [InlineData(null, 0.8)]
    [InlineData("0.5", 0.5)]
    [InlineData("0", 0.0)]
    [InlineData("1", 1.0)]
    [InlineData("1.5", 0.8)]
    [InlineData("-1", 0.8)]
    [InlineData("abc", 0.8)]
    [InlineData("0,5", 0.8)]
    public void Parses_the_ratio_setting(string? value, double expected) =>
        Assert.Equal(expected, IndexSwapGuard.ParseRatio(value));

    [Theory]
    [InlineData(null, 1L)]
    [InlineData("50", 50L)]
    [InlineData("0", 0L)]
    [InlineData("-3", 1L)]
    [InlineData("x", 1L)]
    public void Parses_the_minimum_documents_setting(string? value, long expected) =>
        Assert.Equal(expected, IndexSwapGuard.ParseMinDocuments(value));
}
