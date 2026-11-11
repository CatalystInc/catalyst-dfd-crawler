using System.Xml.Linq;
using Azure.Search.Documents.Indexes.Models;
using AzureFunctions.Indexer;

namespace AzureFunctions.Tests;

public class SitemapParserTests
{
    // The shape the site's sitemap will have: one <url> per language version, each listing
    // every version (itself included) plus x-default.
    private const string LocalizedSitemap = """
        <?xml version="1.0" encoding="utf-8"?>
        <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">
          <url>
            <loc>https://www.catalyst.org/pages/terms-of-use</loc>
            <lastmod>2026-11-10T23:44:21.220Z</lastmod>
            <xhtml:link rel="alternate" hreflang="en-us" href="https://www.catalyst.org/pages/terms-of-use" />
            <xhtml:link rel="alternate" hreflang="fr-ca" href="https://www.catalyst.org/fr-ca/pages/terms-of-use" />
            <xhtml:link rel="alternate" hreflang="x-default" href="https://www.catalyst.org/pages/terms-of-use" />
          </url>
          <url>
            <loc>https://www.catalyst.org/fr-ca/pages/terms-of-use</loc>
            <xhtml:link rel="alternate" hreflang="en-us" href="https://www.catalyst.org/pages/terms-of-use" />
            <xhtml:link rel="alternate" hreflang="fr-ca" href="https://www.catalyst.org/fr-ca/pages/terms-of-use" />
            <xhtml:link rel="alternate" hreflang="x-default" href="https://www.catalyst.org/pages/terms-of-use" />
          </url>
          <url>
            <loc>https://www.catalyst.org/insights/2026/five-bs-gender-partnership</loc>
            <lastmod>2026-11-10T23:44:21.220Z</lastmod>
          </url>
          <url>
            <xhtml:link rel="alternate" hreflang="en-us" href="https://www.catalyst.org/no-loc" />
          </url>
        </urlset>
        """;

    private static readonly IReadOnlySet<string> Supported = PageLocale.ParseLocales(PageLocale.DefaultSupportedLocales);

    [Fact]
    public void Reads_locs_and_their_hreflangs()
    {
        var entries = SitemapParser.ParseUrlset(XDocument.Parse(LocalizedSitemap));

        Assert.Equal(3, entries.Count);
        Assert.Equal("https://www.catalyst.org/pages/terms-of-use", entries[0].Url);
        Assert.Equal(new[] { "en-us", "fr-ca", "x-default" }, entries[0].Hreflangs);
        Assert.Equal("https://www.catalyst.org/fr-ca/pages/terms-of-use", entries[1].Url);
        Assert.Empty(entries[2].Hreflangs);
    }

    [Fact]
    public void Reads_todays_sitemap_without_alternates()
    {
        var doc = XDocument.Parse("""
            <?xml version="1.0" encoding="utf-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><url><loc>https://www.catalyst.org/a</loc><lastmod>2026-11-10</lastmod></url><url><loc> https://www.catalyst.org/b </loc></url></urlset>
            """);

        var entries = SitemapParser.ParseUrlset(doc);

        Assert.Equal(new[] { "https://www.catalyst.org/a", "https://www.catalyst.org/b" }, entries.Select(e => e.Url));
        Assert.All(entries, entry => Assert.Empty(entry.Hreflangs));
    }

    [Fact]
    public void Ignores_links_that_are_not_alternates()
    {
        var doc = XDocument.Parse("""
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">
              <url>
                <loc>https://www.catalyst.org/a</loc>
                <xhtml:link rel="canonical" hreflang="fr-ca" href="https://www.catalyst.org/fr-ca/a" />
                <xhtml:link rel="ALTERNATE" hreflang=" fr-CA " href="https://www.catalyst.org/fr-ca/a" />
                <xhtml:link rel="alternate" href="https://www.catalyst.org/x" />
              </url>
            </urlset>
            """);

        Assert.Equal(new[] { "fr-CA" }, SitemapParser.ParseUrlset(doc)[0].Hreflangs);
    }

    [Fact]
    public void English_page_with_a_French_version_lists_fr_ca()
    {
        var entries = SitemapParser.ParseUrlset(XDocument.Parse(LocalizedSitemap));

        var english = entries[0];
        var french = entries[1];
        var englishOnly = entries[2];

        Assert.Equal(new[] { "fr-ca" }, PageLocale.Alternates(PageLocale.FromUrl(english.Url, Supported, "en-us"), english.Hreflangs, Supported));
        Assert.Equal(new[] { "en-us" }, PageLocale.Alternates(PageLocale.FromUrl(french.Url, Supported, "en-us"), french.Hreflangs, Supported));
        Assert.Empty(PageLocale.Alternates(PageLocale.FromUrl(englishOnly.Url, Supported, "en-us"), englishOnly.Hreflangs, Supported));
    }

    [Fact]
    public void Hreflangs_are_keyed_like_the_crawled_urls_and_merged()
    {
        var byUrl = SitemapUrlFilter.HreflangsByUrl(new[]
        {
            new SitemapEntry("https://www.catalyst.org/pages/x#top", new[] { "en-us", "fr-ca" }),
            new SitemapEntry("https://www.catalyst.org/pages/x", new[] { "FR-CA", "x-default" }),
            new SitemapEntry("https://www.catalyst.org/pages/y", Array.Empty<string>()),
        });

        Assert.Equal(new[] { "en-us", "fr-ca", "x-default" }, byUrl["https://www.catalyst.org/pages/x"]);
        Assert.Empty(byUrl["https://www.catalyst.org/pages/y"]);
        Assert.False(byUrl.ContainsKey("https://www.catalyst.org/pages/x#top"));
    }

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
