using System.Xml.Linq;

namespace AzureFunctions.Indexer
{
    /// <summary>
    /// One &lt;url&gt; of a sitemap: its &lt;loc&gt; and the hreflang values of its
    /// &lt;xhtml:link rel="alternate"&gt; elements (Google's way of listing a page's language versions).
    /// </summary>
    public record SitemapEntry(string Url, IReadOnlyList<string> Hreflangs);

    /// <summary>
    /// Reads &lt;urlset&gt; sitemaps.
    /// </summary>
    public static class SitemapParser
    {
        /// <summary>Namespace of the &lt;xhtml:link&gt; alternates.</summary>
        public static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";

        /// <summary>
        /// The entries of a &lt;urlset&gt;, in order; entries without a &lt;loc&gt; are dropped.
        /// Other elements are ignored, so a sitemap without alternates still works.
        /// </summary>
        public static List<SitemapEntry> ParseUrlset(XDocument doc)
        {
            if (doc.Root == null)
            {
                return new List<SitemapEntry>();
            }

            var ns = doc.Root.GetDefaultNamespace();
            return doc.Root.Elements(ns + "url")
                .Select(url => new
                {
                    Loc = url.Element(ns + "loc")?.Value.Trim(),
                    Hreflangs = url.Elements(Xhtml + "link")
                        .Where(link => string.Equals((string?)link.Attribute("rel"), "alternate", StringComparison.OrdinalIgnoreCase))
                        .Select(link => ((string?)link.Attribute("hreflang"))?.Trim())
                        .Where(hreflang => !string.IsNullOrEmpty(hreflang))
                        .Select(hreflang => hreflang!)
                        .ToList(),
                })
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Loc))
                .Select(entry => new SitemapEntry(entry.Loc!, entry.Hreflangs))
                .ToList();
        }
    }
}
