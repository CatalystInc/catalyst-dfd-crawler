using System.Xml.Linq;
using Azure.Search.Documents.Indexes.Models;
using AzureFunctions.Indexer;

namespace AzureFunctions.Tests;

public class SitemapParserTests
{
    private const string En = "https://www.catalyst.org/pages/terms-of-use";
    private const string Fr = "https://www.catalyst.org/fr-ca/pages/terms-of-use";
    private const string EnOnly = "https://www.catalyst.org/insights/2026/five-bs-gender-partnership";

    // The shape the site's sitemap will have: one <url> per language version, each listing
    // every version (itself included) plus x-default.
    private const string LocalizedSitemap = $"""
        <?xml version="1.0" encoding="utf-8"?>
        <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">
          <url>
            <loc>{En}</loc>
            <lastmod>2026-11-10T23:44:21.220Z</lastmod>
            <xhtml:link rel="alternate" hreflang="en-us" href="{En}" />
            <xhtml:link rel="alternate" hreflang="fr-ca" href="{Fr}" />
            <xhtml:link rel="alternate" hreflang="x-default" href="{En}" />
          </url>
          <url>
            <loc>{Fr}</loc>
            <xhtml:link rel="alternate" hreflang="en-us" href="{En}" />
            <xhtml:link rel="alternate" hreflang="fr-ca" href="{Fr}" />
            <xhtml:link rel="alternate" hreflang="x-default" href="{En}" />
          </url>
          <url>
            <loc>{EnOnly}</loc>
            <lastmod>2026-11-10T23:44:21.220Z</lastmod>
          </url>
          <url>
            <xhtml:link rel="alternate" hreflang="en-us" href="https://www.catalyst.org/no-loc" />
          </url>
        </urlset>
        """;

    private static readonly IReadOnlySet<string> Supported = PageLocale.ParseLocales(PageLocale.DefaultSupportedLocales);

    private static List<SitemapEntry> Parse(string xml) => SitemapParser.ParseUrlset(XDocument.Parse(xml));

    [Fact]
    public void Reads_locs_and_their_alternates()
    {
        var entries = Parse(LocalizedSitemap);

        Assert.Equal(3, entries.Count);
        Assert.Equal(En, entries[0].Url);
        Assert.Equal(
            new[] { new SitemapAlternate("en-us", En), new SitemapAlternate("fr-ca", Fr), new SitemapAlternate("x-default", En) },
            entries[0].Alternates);
        Assert.Equal(Fr, entries[1].Url);
        Assert.Empty(entries[2].Alternates);
    }

    [Fact]
    public void Reads_todays_sitemap_without_alternates()
    {
        var entries = Parse("""
            <?xml version="1.0" encoding="utf-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><url><loc>https://www.catalyst.org/a</loc><lastmod>2026-11-10</lastmod></url><url><loc> https://www.catalyst.org/b </loc></url></urlset>
            """);

        Assert.Equal(new[] { "https://www.catalyst.org/a", "https://www.catalyst.org/b" }, entries.Select(e => e.Url));
        Assert.All(entries, entry => Assert.Empty(entry.Alternates));
    }

    [Fact]
    public void Reads_alternates_under_any_namespace_prefix()
    {
        var entries = Parse($"""
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:x="http://www.w3.org/1999/xhtml">
              <url><loc>{En}</loc><x:link rel="alternate" hreflang="fr-ca" href="{Fr}" /></url>
            </urlset>
            """);

        Assert.Equal(new[] { new SitemapAlternate("fr-ca", Fr) }, entries[0].Alternates);
    }

    [Fact]
    public void Ignores_links_that_are_not_usable_alternates()
    {
        var entries = Parse($"""
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">
              <url>
                <loc>{En}</loc>
                <xhtml:link rel="canonical" hreflang="fr-ca" href="{Fr}" />
                <xhtml:link rel="ALTERNATE" hreflang=" fr-CA " href=" {Fr} " />
                <xhtml:link rel="alternate" href="https://www.catalyst.org/x" />
                <xhtml:link rel="alternate" hreflang="en-us" />
                <link rel="alternate" hreflang="en-us" href="{En}" />
              </url>
            </urlset>
            """);

        Assert.Equal(new[] { new SitemapAlternate("fr-CA", Fr) }, entries[0].Alternates);
    }

    [Fact]
    public void English_page_with_a_French_version_lists_fr_ca_and_the_reverse()
    {
        var entries = Parse(LocalizedSitemap);
        List<string> AlternatesOf(SitemapEntry entry) =>
            PageLocale.Alternates(PageLocale.FromUrl(entry.Url, Supported, "en-us"), entry.Alternates.Select(a => a.Hreflang), Supported);

        Assert.Equal(new[] { "fr-ca" }, AlternatesOf(entries[0]));
        Assert.Equal(new[] { "en-us" }, AlternatesOf(entries[1]));
        Assert.Empty(AlternatesOf(entries[2]));
    }

    [Fact]
    public void Alternates_are_keyed_like_the_crawled_urls_and_merged()
    {
        var byUrl = SitemapUrlFilter.AlternatesByUrl(new[]
        {
            new SitemapEntry("https://www.catalyst.org/pages/x#top", new[] { new SitemapAlternate("en-us", "a"), new SitemapAlternate("fr-ca", "b") }),
            new SitemapEntry("https://www.catalyst.org/pages/x", new[] { new SitemapAlternate("FR-CA", "c"), new SitemapAlternate("x-default", "a") }),
            new SitemapEntry("https://www.catalyst.org/pages/y", Array.Empty<SitemapAlternate>()),
        });

        Assert.Equal(
            new[] { new SitemapAlternate("en-us", "a"), new SitemapAlternate("fr-ca", "b"), new SitemapAlternate("x-default", "a") },
            byUrl["https://www.catalyst.org/pages/x"]);
        Assert.Empty(byUrl["https://www.catalyst.org/pages/y"]);
        Assert.False(byUrl.ContainsKey("https://www.catalyst.org/pages/x#top"));
    }

    [Fact]
    public void Verified_alternates_need_the_other_version_to_be_indexed()
    {
        var alternates = new[] { new SitemapAlternate("en-us", En), new SitemapAlternate("fr-ca", Fr + "#main"), new SitemapAlternate("x-default", En) };

        // The French version was indexed (fragment ignored): the English page has a French version.
        Assert.Equal(new[] { "fr-ca" }, PageLocale.VerifiedAlternates("en-us", alternates, new HashSet<string> { En, Fr }, Supported));

        // The French version is dead, failed or missing from the sitemap: French search keeps showing the English page.
        Assert.Empty(PageLocale.VerifiedAlternates("en-us", alternates, new HashSet<string> { En }, Supported));
        Assert.Empty(PageLocale.VerifiedAlternates("en-us", null, new HashSet<string> { En, Fr }, Supported));
    }

    [Theory]
    [InlineData(Fr, "https://www.catalyst.org/pages/terms-of-use", true)]
    [InlineData(En, "https://www.catalyst.org/fr-ca/pages/terms-of-use", true)]
    [InlineData(Fr, "https://www.catalyst.org/fr-ca/pages/terms-of-use?t=1", false)]
    [InlineData(En, "https://www.catalyst.org/en-us/pages/terms-of-use", false)]
    [InlineData("https://www.catalyst.org/bio/x", "https://www.catalyst.org/about/our-team/x", false)]
    public void Recognises_redirects_into_another_language(string url, string finalUrl, bool expected) =>
        Assert.Equal(expected, PageLocale.IsCrossLocaleRedirect(url, new Uri(finalUrl), Supported, "en-us"));

    [Fact]
    public void Without_a_final_url_there_is_no_cross_language_redirect() =>
        Assert.False(PageLocale.IsCrossLocaleRedirect(Fr, null, Supported, "en-us"));

    [Fact]
    public void Adds_the_alternate_locales_field_once_as_a_filterable_string_collection()
    {
        var fields = new List<SearchField> { new SearchField("id", SearchFieldDataType.String) { IsKey = true } };

        PageCrawlerBase.EnsureAlternateLocalesField(fields);
        PageCrawlerBase.EnsureAlternateLocalesField(fields);

        var field = Assert.Single(fields, f => f.Name == "alternate_locales");
        Assert.Equal(SearchFieldDataType.Collection(SearchFieldDataType.String), field.Type);
        Assert.True(field.IsFilterable);
        Assert.False(field.IsSearchable);
        Assert.False(field.IsSortable);
        Assert.False(field.IsHidden);
        Assert.Equal(2, fields.Count);
    }

    [Fact]
    public void Keeps_an_existing_alternate_locales_field()
    {
        var existing = new SearchField("alternate_locales", SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true, IsFacetable = true };
        var fields = new List<SearchField> { existing };

        PageCrawlerBase.EnsureAlternateLocalesField(fields);

        Assert.Same(existing, Assert.Single(fields));
    }
}
