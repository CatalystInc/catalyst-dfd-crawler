using System.Text.Json;
using AzureFunctions.Indexer;

namespace AzureFunctions.Tests;

public class CrawlRequestTests
{
    // The options PageCrawlerHttp reads its request body with.
    private static readonly JsonSerializerOptions HttpTriggerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void Reads_the_documented_payload_with_alternates()
    {
        const string json = """
            {
              "source": "https://www.catalyst.org/sitemap.xml",
              "urls": ["https://www.catalyst.org/pages/terms-of-use"],
              "alternates": {
                "https://www.catalyst.org/pages/terms-of-use": [
                  { "hreflang": "en-us", "href": "https://www.catalyst.org/pages/terms-of-use" },
                  { "hreflang": "fr-ca", "href": "https://www.catalyst.org/fr-ca/pages/terms-of-use" }
                ]
              }
            }
            """;

        var request = JsonSerializer.Deserialize<CrawlRequest>(json, HttpTriggerOptions)!;

        Assert.Equal(new[] { "https://www.catalyst.org/pages/terms-of-use" }, request.Urls);
        Assert.Equal(
            new[]
            {
                new SitemapAlternate("en-us", "https://www.catalyst.org/pages/terms-of-use"),
                new SitemapAlternate("fr-ca", "https://www.catalyst.org/fr-ca/pages/terms-of-use"),
            },
            request.Alternates!["https://www.catalyst.org/pages/terms-of-use"]);
    }

    [Fact]
    public void Alternates_are_optional()
    {
        var request = JsonSerializer.Deserialize<CrawlRequest>(
            """{ "source": "s", "urls": ["https://www.catalyst.org/a"] }""", HttpTriggerOptions)!;

        Assert.Null(request.Alternates);
    }
}
