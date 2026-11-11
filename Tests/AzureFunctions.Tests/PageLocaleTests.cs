using AzureFunctions.Indexer;

namespace AzureFunctions.Tests;

public class PageLocaleTests
{
    private static readonly IReadOnlySet<string> Supported = PageLocale.ParseLocales(PageLocale.DefaultSupportedLocales);

    [Theory]
    [InlineData("https://www.catalyst.org/fr-ca/pages/terms-of-use", "fr-ca")]
    [InlineData("https://www.catalyst.org/FR-CA/pages/terms-of-use", "fr-ca")]
    [InlineData("https://www.catalyst.org/fr-ca", "fr-ca")]
    [InlineData("https://www.catalyst.org/fr-ca/pages/terms-of-use?t=1700000000", "fr-ca")]
    [InlineData("https://www.catalyst.org/en-us/pages/terms-of-use", "en-us")]
    [InlineData("https://www.catalyst.org/pages/terms-of-use", "en-us")]
    [InlineData("https://www.catalyst.org/", "en-us")]
    [InlineData("https://www.catalyst.org/fr/insights/x", "en-us")]
    [InlineData("https://www.catalyst.org/insights/fr-ca/x", "en-us")]
    [InlineData("not a url", "en-us")]
    public void Takes_the_locale_from_the_first_path_segment(string url, string expected) =>
        Assert.Equal(expected, PageLocale.FromUrl(url, Supported, PageLocale.DefaultLocale));

    [Fact]
    public void Uses_the_configured_default_for_unprefixed_urls() =>
        Assert.Equal("fr-ca", PageLocale.FromUrl("https://www.catalyst.org/pages/x", Supported, "fr_CA"));

    [Theory]
    [InlineData("fr_CA", "fr-ca")]
    [InlineData("en-US", "en-us")]
    [InlineData(" EN_us ", "en-us")]
    public void Normalizes_to_the_search_filter_format(string value, string expected) =>
        Assert.Equal(expected, PageLocale.Normalize(value));

    [Fact]
    public void Parses_the_supported_locales_setting() =>
        Assert.True(PageLocale.ParseLocales("en-US, fr_CA,,").SetEquals(new[] { "en-us", "fr-ca" }));
}
