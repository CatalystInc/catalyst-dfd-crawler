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

        }

		/// <summary>
		/// Processes a sitemap or sitemap index at the given URL.
		/// </summary>
		/// <param name="url">The URL of the sitemap to process.</param>
		/// <returns>A task representing the asynchronous operation.</returns>
		public async Task ProcessSitemapAsync(string url)
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

                await _queue.StartIndexSwapAsync();

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
						List<string> urls = ExtractUrlsFromSitemap(doc, ns);
						await ProcessSitemapUrlsAsync(url, urls);
						break;
					default:
						throw new FormatException("The XML does not appear to be a valid sitemap or sitemap index.");
				}

                await _queue.CompleteIndexSwapAsync();
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

			var sitemapTasks = doc.Root.Elements(ns + "sitemap")
				.Select(sitemapElement => sitemapElement.Element(ns + "loc"))
				.Where(locElement => locElement != null)
				.Select(locElement => ProcessSitemapAsync(locElement.Value.Trim()));

			await Task.WhenAll(sitemapTasks);
		}

		/// <summary>
		/// Extracts URLs from a sitemap document.
		/// </summary>
		/// <param name="doc">The XDocument representing the sitemap.</param>
		/// <param name="ns">The XML namespace of the document.</param>
		/// <returns>A list of URLs extracted from the sitemap.</returns>
		private List<string> ExtractUrlsFromSitemap(XDocument doc, XNamespace ns)
		{
			if (doc.Root == null)
			{
				_logger.LogError("Invalid sitemap: Root element is missing.");
				return new List<string>();
			}

			return doc.Root.Elements(ns + "url")
				.Select(urlElement => urlElement.Element(ns + "loc"))
				.Where(locElement => locElement != null)
				.Select(locElement => locElement.Value.Trim())
				.Where(url => !string.IsNullOrWhiteSpace(url))
				.ToList();
		}

		/// <summary>
		/// Processes URLs from a sitemap, sending them in batches to Azure Service Bus.
		/// </summary>
		/// <param name="sitemapSource">The source URL of the sitemap.</param>
		/// <param name="urls">The list of URLs to process.</param>
		/// <returns>A task representing the asynchronous operation.</returns>
		private async Task ProcessSitemapUrlsAsync(string sitemapSource, List<string> urls)
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

			var crawlRequest = new CrawlRequest() { Source = sitemapSource, Urls = urls };

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
