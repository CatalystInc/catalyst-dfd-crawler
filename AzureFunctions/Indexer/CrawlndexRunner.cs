using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Xml.Linq;

namespace AzureFunctions.Indexer
{
    /// <summary>
    /// Represents a runner for crawling and processing sitemaps, sending URLs to Azure Service Bus for indexing.
    /// </summary>
    public class CrawlIndexRunner
	{
		private readonly ILogger<CrawlIndexRunner> _logger;
		private readonly string _schedule;
		private readonly HttpClient _httpClient;
		private readonly string _sitemapsRootUrl;
		private readonly PageCrawlerQueue _queue;
		private readonly HashSet<string> _skippedPathPrefixes;
		private readonly HashSet<string> _supportedLocales;

        /// <summary>
        /// Initializes a new instance of the CrawlIndexRunner class.
        /// </summary>
        /// <param name="loggerFactory">The factory for creating loggers.</param>
        /// <param name="configuration">The configuration containing necessary settings.</param>
        /// <param name="httpClient">The HTTP client for making web requests.</param>
        /// <exception cref="ArgumentNullException">Thrown if any required parameter is null.</exception>
        public CrawlIndexRunner(ILoggerFactory loggerFactory, IConfiguration configuration, HttpClient httpClient, PageCrawlerQueue queue)
		{
			_logger = loggerFactory?.CreateLogger<CrawlIndexRunner>() ?? throw new ArgumentNullException(nameof(loggerFactory));
			_schedule = configuration["CrawlIndexSchedule"] ?? throw new ArgumentNullException(nameof(configuration), "CrawlIndexSchedule configuration is missing");
			_sitemapsRootUrl = configuration["SitemapsRootUrl"] ?? throw new ArgumentNullException(nameof(configuration), "SitemapsRootUrl configuration is missing");
			_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
			_queue = queue;
			_skippedPathPrefixes = SitemapUrlFilter.ParsePrefixes(
				configuration["SitemapSkippedPathPrefixes"] ?? SitemapUrlFilter.DefaultSkippedPathPrefixes);
			_supportedLocales = PageLocale.ParseLocales(configuration["SupportedLocales"] ?? PageLocale.DefaultSupportedLocales);

        }

		/// <summary>
		/// Processes a sitemap or sitemap index at the given URL.
		/// </summary>
		/// <param name="url">The URL of the sitemap to process.</param>
		/// <param name="isRoot">
		/// True for the configured root sitemap. Only the root builds a new index and swaps it in,
		/// once, after every child sitemap of a sitemap index has been crawled into it.
		/// </param>
		/// <returns>A task representing the asynchronous operation.</returns>
		public async Task ProcessSitemapAsync(string url, bool isRoot = true)
		{
			if (string.IsNullOrWhiteSpace(url))
			{
				_logger.LogError("Invalid sitemap URL provided.");
				return;
			}

			try
			{
				_logger.LogInformation("Processing sitemap: {Url}", url);
				var xmlContent = await _httpClient.GetStringAsync(url);
				var doc = XDocument.Parse(xmlContent);

                if (isRoot)
                {
                    await _queue.StartIndexSwapAsync();
                }

                XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

				switch (doc.Root?.Name.LocalName)
				{
					// this is for if there was a sitempaindex, in which case it will recursively processed for each sitemap
					case "sitemapindex":
						_logger.LogInformation("Processing sitemap index: {Url}", url);
						await ProcessSitemapIndexAsync(doc, ns);
						break;
					case "urlset":
						_logger.LogInformation("Processing urlset: {Url}", url);
						var entries = SitemapParser.ParseUrlset(doc);
						List<string> urls = ExtractUrlsFromSitemap(entries);
						LogAlternatesSummary(entries);
						await ProcessSitemapUrlsAsync(url, urls, SitemapUrlFilter.AlternatesByUrl(entries));
						break;
					default:
						throw new FormatException("The XML does not appear to be a valid sitemap or sitemap index.");
				}

                if (isRoot)
                {
                    await _queue.CompleteIndexSwapAsync();
                }
            }
			catch (HttpRequestException e)
			{
				_logger.LogError(e, "Error downloading sitemap: {Url}. Error: {message}", url, e.Message);
			}
			catch (System.Xml.XmlException e)
			{
				_logger.LogError(e, "Error parsing sitemap XML: {Url}. Error: {message}", url, e.Message);
			}
			catch (FormatException e)
			{
				_logger.LogError(e, "Invalid sitemap format: {Url}. Error: {message}", url, e.Message);
			}
			catch (IndexSwapAbortedException e)
			{
				// The live index was kept. Rethrow so the invocation shows as failed and can alert.
				_logger.LogError(e, "Index swap aborted for sitemap {Url}: {message}", url, e.Message);
				throw;
			}
			catch (Exception e)
			{
				_logger.LogError(e, "An unexpected error occurred while processing sitemap: {Url}. Error: {message}", url, e.Message);
			}
		}

		/// <summary>
		/// Processes a sitemap index, recursively processing each sitemap it contains.
		/// </summary>
		/// <param name="doc">The XDocument representing the sitemap index.</param>
		/// <param name="ns">The XML namespace of the document.</param>
		/// <returns>A task representing the asynchronous operation.</returns>
		private async Task ProcessSitemapIndexAsync(XDocument doc, XNamespace ns)
		{
			if (doc.Root == null)
			{
				_logger.LogError("Invalid sitemap index: Root element is missing.");
				return;
			}

			var childSitemapUrls = doc.Root.Elements(ns + "sitemap")
				.Select(sitemapElement => sitemapElement.Element(ns + "loc"))
				.Where(locElement => locElement != null)
				.Select(locElement => locElement!.Value.Trim());

			// One at a time, into the index the root is building: the root swaps once at the
			// end, and the site sees the usual crawl concurrency rather than a multiple of it.
			foreach (var childSitemapUrl in childSitemapUrls)
			{
				await ProcessSitemapAsync(childSitemapUrl, isRoot: false);
			}
		}

		/// <summary>
		/// Logs how many entries list language alternates, and hreflang codes that are ignored
		/// because they aren't supported locales (e.g. language-only 'fr'), so a sitemap whose
		/// alternates can't be used doesn't go unnoticed.
		/// </summary>
		private void LogAlternatesSummary(IReadOnlyList<SitemapEntry> entries)
		{
			var withAlternates = entries.Count(entry => entry.Alternates.Count > 0);
			_logger.LogInformation("Sitemap lists language alternates for {WithAlternates} of {Total} entries", withAlternates, entries.Count);

			var ignored = entries
				.SelectMany(entry => entry.Alternates)
				.Select(alternate => PageLocale.Normalize(alternate.Hreflang))
				.Where(hreflang => hreflang != "x-default" && !_supportedLocales.Contains(hreflang))
				.Distinct()
				.ToList();
			if (ignored.Count > 0)
			{
				_logger.LogWarning("Ignoring hreflang values that aren't supported locales: {Hreflangs}", string.Join(",", ignored));
			}
		}

		/// <summary>
		/// The URLs to crawl from a sitemap's entries: without #fragments, duplicates or
		/// skipped legacy prefixes.
		/// </summary>
		/// <param name="entries">The sitemap's &lt;url&gt; entries.</param>
		/// <returns>A list of URLs extracted from the sitemap.</returns>
		private List<string> ExtractUrlsFromSitemap(IReadOnlyList<SitemapEntry> entries)
		{
			var locs = entries.Select(entry => entry.Url).ToList();

			var urls = SitemapUrlFilter.NormalizeAndDedupe(locs);
			if (urls.Count < locs.Count)
			{
				_logger.LogInformation("Removed {DuplicateCount} duplicate sitemap URLs (after dropping #fragments)", locs.Count - urls.Count);
			}

			var skipped = urls.RemoveAll(url => SitemapUrlFilter.HasSkippedPrefix(url, _skippedPathPrefixes));
			if (skipped > 0)
			{
				_logger.LogInformation("Skipped {SkippedCount} sitemap URLs under the path prefixes {Prefixes}", skipped, string.Join(",", _skippedPathPrefixes));
			}

			return urls;
		}

		/// <summary>
		/// Processes URLs from a sitemap, sending them in batches to Azure Service Bus.
		/// </summary>
		/// <param name="sitemapSource">The source URL of the sitemap.</param>
		/// <param name="urls">The list of URLs to process.</param>
		/// <param name="alternates">The language alternates the sitemap lists per URL.</param>
		/// <returns>A task representing the asynchronous operation.</returns>
		private async Task ProcessSitemapUrlsAsync(string sitemapSource, List<string> urls, Dictionary<string, List<SitemapAlternate>> alternates)
		{
			if (string.IsNullOrWhiteSpace(sitemapSource))
			{
				_logger.LogError("Invalid sitemap source provided.");
				return;
			}

			if (urls == null || urls.Count == 0)
			{
				_logger.LogWarning("No URLs to process for sitemap: {SitemapSource}", sitemapSource);
				return;
			}

			_logger.LogInformation("Processing {UrlCount} URLs from sitemap: {SitemapSource}", urls.Count, sitemapSource);

			var crawlRequest = new CrawlRequest() { Source = sitemapSource, Urls = urls, Alternates = alternates };

			await _queue.CrawlPagesAsync(crawlRequest);
		}

		/// <summary>
		/// Azure Function entry point, triggered on a schedule to start the crawl process.
		/// </summary>
		/// <param name="myTimer">Timer information for the function trigger.</param>
		/// <returns>A task representing the asynchronous operation.</returns>
		[Function("CrawlIndexRunner")]
		public async Task RunAsync([TimerTrigger("%CrawlIndexSchedule%")] TimerInfo myTimer)
		{
			_logger.LogInformation("CrawlIndexRunner function executed at: {CurrentTime}", DateTime.UtcNow);
			await ProcessSitemapAsync(_sitemapsRootUrl);
		}
	}
}
