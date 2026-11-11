using AzureFunctions.Indexer;

namespace AzureFunctions.Tests;

public class SitemapUrlFilterTests
{
    private static readonly IReadOnlySet<string> Defaults =
        SitemapUrlFilter.ParsePrefixes(SitemapUrlFilter.DefaultSkippedPathPrefixes);

    [Theory]
    [InlineData("https://www.catalyst.org/fr/insights/2022/surmonter-les-obstacles-a-la-conversation")]
    [InlineData("https://www.catalyst.org/FR/insights/x")]
    [InlineData("https://www.catalyst.org/fr")]
    [InlineData("https://www.catalyst.org/es/insights/x")]
    [InlineData("https://www.catalyst.org/de/x")]
    [InlineData("https://www.catalyst.org/jp/insights/2024/%E3%81%82")]
    [InlineData("https://www.catalyst.org/cn/x")]
    [InlineData("https://www.catalyst.org/br/x")]
    public void Skips_legacy_locale_prefixes(string url) =>
        Assert.True(SitemapUrlFilter.HasSkippedPrefix(url, Defaults));

    [Theory]
    [InlineData("https://www.catalyst.org/fr-ca/pages/terms-of-use")]
    [InlineData("https://www.catalyst.org/en-us/pages/terms-of-use")]
    [InlineData("https://www.catalyst.org/pages/terms-of-use")]
    [InlineData("https://www.catalyst.org/french-page")]
    [InlineData("https://www.catalyst.org/insights/fr/x")]
    [InlineData("https://www.catalyst.org/")]
    [InlineData("https://www.catalyst.org/#")]
    [InlineData("not a url")]
    public void Keeps_everything_else(string url) =>
        Assert.False(SitemapUrlFilter.HasSkippedPrefix(url, Defaults));

    [Fact]
    public void An_empty_setting_skips_nothing() =>
        Assert.False(SitemapUrlFilter.HasSkippedPrefix("https://www.catalyst.org/fr/x", SitemapUrlFilter.ParsePrefixes("")));

    [Fact]
    public void Prefix_setting_is_trimmed_and_ignores_blanks() =>
        Assert.True(SitemapUrlFilter.ParsePrefixes(" fr , /es/ ,,").SetEquals(new[] { "fr", "es" }));

    [Theory]
    [InlineData("https://www.catalyst.org/#", "https://www.catalyst.org/")]
    [InlineData("https://www.catalyst.org/pages/x#section", "https://www.catalyst.org/pages/x")]
    [InlineData("https://www.catalyst.org/pages/x?a=1#s", "https://www.catalyst.org/pages/x?a=1")]
    [InlineData("https://www.catalyst.org/pages/x", "https://www.catalyst.org/pages/x")]
    public void Drops_the_fragment(string url, string expected) =>
        Assert.Equal(expected, SitemapUrlFilter.NormalizeUrl(url));

    [Fact]
    public void Dedupes_in_sitemap_order_after_dropping_fragments()
    {
        var urls = SitemapUrlFilter.NormalizeAndDedupe(new[]
        {
            "https://www.catalyst.org/b",
            "https://www.catalyst.org/#",
            "https://www.catalyst.org/a",
            "https://www.catalyst.org/#",
            "https://www.catalyst.org/b",
            "https://www.catalyst.org/",
        });

        Assert.Equal(new[] { "https://www.catalyst.org/b", "https://www.catalyst.org/", "https://www.catalyst.org/a" }, urls);
    }
}
