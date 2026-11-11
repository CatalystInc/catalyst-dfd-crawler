using System.Net;

namespace AzureFunctions.Indexer
{
    /// <summary>
    /// Classifies page responses.
    /// </summary>
    public static class CrawlResponse
    {
        /// <summary>
        /// Whether the sitemap URL points at a page that doesn't exist: a 404 or 410, or a
        /// redirect that ended on the site's /not-found page. Retrying these never helps.
        /// </summary>
        /// <param name="statusCode">The status of the final response, after redirects.</param>
        /// <param name="finalUri">The URL of the final response (HttpResponseMessage.RequestMessage.RequestUri).</param>
        public static bool IsDeadLink(HttpStatusCode statusCode, Uri? finalUri)
        {
            if (statusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                return true;
            }

            if (finalUri == null || !finalUri.IsAbsoluteUri)
            {
                return false;
            }

            var path = finalUri.AbsolutePath;
            return path.Equals("/not-found", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/not-found/", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The sitemap URL leads to a missing page; it is skipped without retries.
    /// </summary>
    public class DeadLinkException : Exception
    {
        public DeadLinkException(string url, Uri? finalUri, HttpStatusCode statusCode)
            : base($"{url} leads to a missing page ({(int)statusCode}{(finalUri != null ? $" at {finalUri.GetLeftPart(UriPartial.Path)}" : string.Empty)})")
        {
        }
    }
}
