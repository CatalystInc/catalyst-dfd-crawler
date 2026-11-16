using System.Net;
using AzureFunctions.Indexer;

namespace AzureFunctions.Tests;

public class CrawlResponseTests
{
    [Theory]
    [InlineData(HttpStatusCode.NotFound, "https://www.catalyst.org/not-found?intent=about--our-team--x")]
    [InlineData(HttpStatusCode.NotFound, "https://www.catalyst.org/pages/missing")]
    [InlineData(HttpStatusCode.Gone, "https://www.catalyst.org/pages/old")]
    [InlineData(HttpStatusCode.OK, "https://www.catalyst.org/not-found?intent=x")]
    [InlineData(HttpStatusCode.OK, "https://www.catalyst.org/NOT-FOUND")]
    public void Recognises_missing_pages(HttpStatusCode status, string finalUrl) =>
        Assert.True(CrawlResponse.IsDeadLink(status, new Uri(finalUrl)));

    [Theory]
    [InlineData(HttpStatusCode.OK, "https://www.catalyst.org/pages/terms-of-use")]
    [InlineData(HttpStatusCode.OK, "https://www.catalyst.org/not-found-stories/x")]
    [InlineData(HttpStatusCode.InternalServerError, "https://www.catalyst.org/pages/x")]
    [InlineData(HttpStatusCode.TooManyRequests, "https://www.catalyst.org/pages/x")]
    public void Leaves_other_responses_to_the_normal_retry_path(HttpStatusCode status, string finalUrl) =>
        Assert.False(CrawlResponse.IsDeadLink(status, new Uri(finalUrl)));

    [Fact]
    public void Handles_a_missing_final_url()
    {
        Assert.False(CrawlResponse.IsDeadLink(HttpStatusCode.OK, null));
        Assert.True(CrawlResponse.IsDeadLink(HttpStatusCode.NotFound, null));
    }
}
