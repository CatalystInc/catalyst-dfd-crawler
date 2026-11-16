using System.Runtime.CompilerServices;
using AzureFunctions.Indexer;

public class CrawlRequest
{
	public List<string> Urls { get; set; }
	public string Source { get; set; }
	public string IndexSwap { get; set; }

	/// <summary>
	/// Optional: the language alternates the sitemap lists for each URL (keyed by the URL
	/// as crawled). Used to fill the alternate_locales field; without it the field isn't
	/// written, so a document keeps the value it has.
	/// </summary>
	public Dictionary<string, List<SitemapAlternate>>? Alternates { get; set; }
}
