namespace AzureFunctions.Indexer
{
    /// <summary>
    /// Decides whether a freshly built index may replace the live one behind the alias.
    /// Without it, an empty or partial sitemap, a site outage during the crawl, or a
    /// failed schema clone would put an empty or near-empty index live (and with
    /// DeleteOldIndexOnSwap, delete the good one).
    /// </summary>
    public static class IndexSwapGuard
    {
        /// <summary>Default for 'SwapMinDocumentRatio': the new index needs 80% of the live index's documents.</summary>
        public const double DefaultMinDocumentRatio = 0.8;

        /// <summary>Default for 'SwapMinDocuments': an empty index never goes live.</summary>
        public const long DefaultMinDocuments = 1;

        /// <summary>
        /// Why the new index must not go live, or null when it may.
        /// </summary>
        /// <param name="newCount">Documents in the new index.</param>
        /// <param name="liveCount">Documents in the live index, or null when unknown (e.g. no live index yet).</param>
        /// <param name="minRatio">Minimum share of the live count (0 turns the comparison off, e.g. for an intended shrink).</param>
        /// <param name="minDocuments">Minimum number of documents, whatever the live count.</param>
        public static string? GetRejectionReason(long newCount, long? liveCount, double minRatio, long minDocuments)
        {
            if (newCount < minDocuments)
            {
                return $"the new index has {newCount} documents, under the minimum of {minDocuments}";
            }

            if (liveCount is > 0 && minRatio > 0 && newCount < liveCount.Value * minRatio)
            {
                return $"the new index has {newCount} documents, under {minRatio:P0} of the live index's {liveCount.Value}";
            }

            return null;
        }

        /// <summary>
        /// Reads a ratio setting such as "0.8" (invariant culture); the default when missing, invalid or outside 0-1.
        /// </summary>
        public static double ParseRatio(string? value) =>
            double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ratio)
            && ratio >= 0 && ratio <= 1
                ? ratio
                : DefaultMinDocumentRatio;

        /// <summary>
        /// Reads a minimum-documents setting; the default when missing, invalid or negative.
        /// </summary>
        public static long ParseMinDocuments(string? value) =>
            long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var minimum)
            && minimum >= 0
                ? minimum
                : DefaultMinDocuments;

        /// <summary>
        /// Default for 'OldIndexDeleteDelaySeconds'. Azure AI Search can take up to 10 seconds
        /// to apply an alias change everywhere; searches that still resolve the alias to the
        /// old index fail with 404 if it is already deleted.
        /// </summary>
        public const int DefaultOldIndexDeleteDelaySeconds = 15;

        /// <summary>Upper bound for 'OldIndexDeleteDelaySeconds', so a typo can't eat the run's time.</summary>
        public const int MaxOldIndexDeleteDelaySeconds = 120;

        /// <summary>
        /// Reads the wait between switching the alias and deleting the old index (whole seconds,
        /// 0 = no wait); the default when missing, invalid, negative or over the maximum.
        /// </summary>
        public static TimeSpan ParseOldIndexDeleteDelay(string? value) =>
            TimeSpan.FromSeconds(
                int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                && seconds >= 0 && seconds <= MaxOldIndexDeleteDelaySeconds
                    ? seconds
                    : DefaultOldIndexDeleteDelaySeconds);
    }

    /// <summary>
    /// The run stopped before switching the alias, so the live index stays in place.
    /// Rethrown by the timer function so the invocation is marked as failed.
    /// </summary>
    public class IndexSwapAbortedException : Exception
    {
        public IndexSwapAbortedException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
