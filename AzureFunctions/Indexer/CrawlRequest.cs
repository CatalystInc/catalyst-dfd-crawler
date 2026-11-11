using System.Runtime.CompilerServices;

public class CrawlRequest
{
	public List<string> Urls { get; set; }
	public string Source { get; set; }
	public string IndexSwap { get; set; }

	/// <summary>
	/// Optional: the hreflang values the sitemap lists for each URL (keyed by the URL as
	/// crawled). Used to fill the alternate_locales field.
	/// </summary>
	public Dictionary<string, List<string>>? Hreflangs { get; set; }
}
