namespace AzureFunctions.Indexer
{
    /// <summary>
    /// Decides which sitemap URLs are crawled.
    /// </summary>
    public static class SitemapUrlFilter
    {
        /// <summary>
        /// Legacy locale path prefixes the site no longer routes (DFDMVP-2279): their pages
        /// are not found or redirect elsewhere, and the French ones would be indexed with
        /// French titles in the English results. Configurable via 'SitemapSkippedPathPrefixes'.
        /// </summary>
        public const string DefaultSkippedPathPrefixes = "fr,es,de,jp,cn,br";

        /// <summary>
        /// Parses a comma-separated list of path prefixes (case-insensitive, blanks ignored).
        /// </summary>
        public static HashSet<string> ParsePrefixes(string? value) =>
            new(
                (value ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(prefix => prefix.Trim('/')),
                StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whether the URL's first path segment is exactly one of the prefixes, e.g. 'fr'
        /// matches /fr/insights/... but not /fr-ca/... or /french-page.
        /// </summary>
        public static bool HasSkippedPrefix(string url, IReadOnlySet<string> prefixes)
        {
            if (prefixes.Count == 0 || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var firstSegment = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return firstSegment != null && prefixes.Contains(firstSegment);
        }
    }
}
