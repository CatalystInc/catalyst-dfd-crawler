namespace AzureFunctions.Indexer
{
    /// <summary>
    /// Works out the language of a crawled page from its URL (DFDMVP-2279). The site
    /// serves French at /fr-ca/... and English unprefixed, so the URL is the reliable
    /// signal; the page's og:locale meta is always en_US.
    /// </summary>
    public static class PageLocale
    {
        /// <summary>Default for 'SupportedLocales'.</summary>
        public const string DefaultSupportedLocales = "en-us,fr-ca";

        /// <summary>Default for 'DefaultLocale': the language of unprefixed URLs and of Cvent events.</summary>
        public const string DefaultLocale = "en-us";

        /// <summary>
        /// Index field listing the other languages a page exists in (from the sitemap's
        /// hreflang alternates). French search uses it to show an English page only when
        /// it has no French version: <c>not alternate_locales/any(l: l eq 'fr-ca')</c>.
        /// </summary>
        public const string AlternateLocalesField = "alternate_locales";

        /// <summary>
        /// Parses a comma-separated list of locales into lowercase codes, e.g. "en-US, fr-CA" -> en-us, fr-ca.
        /// </summary>
        public static HashSet<string> ParseLocales(string? value) =>
            new(
                (value ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(Normalize),
                StringComparer.Ordinal);

        /// <summary>
        /// Lowercase with a hyphen, the format the site's search filter sends: 'fr_CA' -> 'fr-ca'.
        /// </summary>
        public static string Normalize(string locale) => locale.Trim().Replace('_', '-').ToLowerInvariant();

        /// <summary>
        /// The locale of the page at this URL: its first path segment when that is a supported
        /// locale (https://www.catalyst.org/fr-ca/pages/x -> fr-ca), otherwise the default.
        /// </summary>
        public static string FromUrl(string url, IReadOnlySet<string> supportedLocales, string defaultLocale)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var firstSegment = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (firstSegment != null)
                {
                    var candidate = Normalize(firstSegment);
                    if (supportedLocales.Contains(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return Normalize(defaultLocale);
        }

        /// <summary>
        /// The other supported languages the page exists in, from the hreflang values the
        /// sitemap lists for it (which include the page itself and x-default), sorted.
        /// </summary>
        public static List<string> Alternates(string ownLocale, IEnumerable<string>? hreflangs, IReadOnlySet<string> supportedLocales)
        {
            var own = Normalize(ownLocale);
            return (hreflangs ?? Enumerable.Empty<string>())
                .Select(Normalize)
                .Where(locale => locale != own && supportedLocales.Contains(locale))
                .Distinct()
                .OrderBy(locale => locale, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Like <see cref="Alternates(string, IEnumerable{string}?, IReadOnlySet{string})"/>, but only
        /// counting alternates whose URL was indexed in this crawl. A French version that is dead,
        /// failed or not in the sitemap must not hide the English page from French search.
        /// </summary>
        public static List<string> VerifiedAlternates(
            string ownLocale,
            IEnumerable<SitemapAlternate>? alternates,
            IReadOnlySet<string> indexedUrls,
            IReadOnlySet<string> supportedLocales) =>
            Alternates(
                ownLocale,
                (alternates ?? Enumerable.Empty<SitemapAlternate>())
                    .Where(alternate => indexedUrls.Contains(SitemapUrlFilter.NormalizeUrl(alternate.Href)))
                    .Select(alternate => alternate.Hreflang),
                supportedLocales);

        /// <summary>
        /// Whether a sitemap URL ended, after redirects, on a page in another language
        /// (e.g. a /fr-ca/ URL redirecting to an English page): that URL has no page in
        /// its own language.
        /// </summary>
        public static bool IsCrossLocaleRedirect(string url, Uri? finalUri, IReadOnlySet<string> supportedLocales, string defaultLocale) =>
            finalUri != null
            && FromUrl(url, supportedLocales, defaultLocale) != FromUrl(finalUri.ToString(), supportedLocales, defaultLocale);
    }
}
