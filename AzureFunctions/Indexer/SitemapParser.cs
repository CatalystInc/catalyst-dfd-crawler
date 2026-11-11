using System.Xml.Linq;

namespace AzureFunctions.Indexer
{
    /// <summary>
    /// One &lt;xhtml:link rel="alternate" hreflang="..." href="..."&gt; of a sitemap entry:
    /// a language version of the page (Google lists every version, including the page itself).
    /// </summary>
    public record SitemapAlternate(string Hreflang, string Href);

    /// <summary>
    /// One &lt;url&gt; of a sitemap: its &lt;loc&gt; and its language alternates.
    /// </summary>
    public record SitemapEntry(string Url, IReadOnlyList<SitemapAlternate> Alternates);

    /// <summary>
    /// Reads &lt;urlset&gt; sitemaps.
    /// </summary>
    public static class SitemapParser
    {
        /// <summary>Namespace of the &lt;xhtml:link&gt; alternates (any prefix may be used for it).</summary>
        public static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";

        /// <summary>
        /// The entries of a &lt;urlset&gt;, in order; entries without a &lt;loc&gt; and alternates
        /// without an hreflang or href are dropped. Other elements are ignored, so a sitemap
        /// without alternates still works.
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
                    Alternates = url.Elements(Xhtml + "link")
                        .Where(link => string.Equals(((string?)link.Attribute("rel"))?.Trim(), "alternate", StringComparison.OrdinalIgnoreCase))
                        .Select(link => new
                        {
                            Hreflang = ((string?)link.Attribute("hreflang"))?.Trim(),
                            Href = ((string?)link.Attribute("href"))?.Trim(),
                        })
                        .Where(link => !string.IsNullOrEmpty(link.Hreflang) && !string.IsNullOrEmpty(link.Href))
                        .Select(link => new SitemapAlternate(link.Hreflang!, link.Href!))
                        .ToList(),
                })
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Loc))
                .Select(entry => new SitemapEntry(entry.Loc!, entry.Alternates))
                .ToList();
        }
    }
}
