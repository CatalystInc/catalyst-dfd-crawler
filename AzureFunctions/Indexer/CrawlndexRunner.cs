using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace AzureFunctions.Indexer
{
	/// <summary>
	/// Represents a runner for crawling and processing sitemaps, sending URLs to Azure Service Bus for indexing.
	/// </summary>
	public class CrawlIndexRunner : IAsyncDisposable
	{
		private readonly ILogger<CrawlIndexRunner> _logger;
		private readonly string _serviceBusConnectionString;
		private readonly string _queueName;
		private readonly string _schedule;
		private readonly HttpClient _httpClient;
		private readonly ServiceBusClient _serviceBusClient;
		private readonly ServiceBusSender _sender;
		private readonly string _sitemapsRootUrl;

		/// <summary>
		/// Initializes a new instance of the CrawlIndexRunner class.
		/// </summary>
		/// <param name="loggerFactory">The factory for creating loggers.</param>
		/// <param name="configuration">The configuration containing necessary settings.</param>
		/// <param name="httpClient">The HTTP client for making web requests.</param>
		/// <exception cref="ArgumentNullException">Thrown if any required parameter is null.</exception>
		public CrawlIndexRunner(ILoggerFactory loggerFactory, IConfiguration configuration, HttpClient httpClient)
		{
			_logger = loggerFactory?.CreateLogger<CrawlIndexRunner>() ?? throw new ArgumentNullException(nameof(loggerFactory));
			_serviceBusConnectionString = configuration?["ServiceBusConnection"] ?? throw new ArgumentNullException(nameof(configuration), "ServiceBusConnection configuration is missing");
			_queueName = configuration["ServiceBusQueueName"] ?? throw new ArgumentNullException(nameof(configuration), "ServiceBusQueueName configuration is missing");
			_schedule = configuration["CrawlIndexSchedule"] ?? throw new ArgumentNullException(nameof(configuration), "CrawlIndexSchedule configuration is missing");
			_sitemapsRootUrl = configuration["SitemapsRootUrl"] ?? throw new ArgumentNullException(nameof(configuration), "SitemapsRootUrl configuration is missing");
			_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

			_serviceBusClient = new ServiceBusClient(_serviceBusConnectionString);
			_sender = _serviceBusClient.CreateSender(_queueName);
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
				string xmlContent = await _httpClient.GetStringAsync(url);
				XDocument doc = XDocument.Parse(xmlContent);

				await SendSwapCommandMessage("begin", url);

				XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

				switch (doc.Root?.Name.LocalName)
				{
					case "sitemapindex":
						_logger.LogInformation("Processing sitemap index: {Url}", url);
						await ProcessSitemapIndexAsync(doc, ns);
						break;
					case "urlset":
						_logger.LogInformation("Processing sitemap: {Url}", url);
						List<string> urls = ExtractUrlsFromSitemap(doc, ns);
						await ProcessSitemapUrlsAsync(url, urls);
						break;
					default:
						throw new FormatException("The XML does not appear to be a valid sitemap or sitemap index.");
				}

				await SendSwapCommandMessage("end", url);
			}
			catch (HttpRequestException e)
			{
				_logger.LogError(e, "Error downloading sitemap: {Url}", url);
			}
			catch (System.Xml.XmlException e)
			{
				_logger.LogError(e, "Error parsing sitemap XML: {Url}", url);
			}
			catch (FormatException e)
			{
				_logger.LogError(e, "Invalid sitemap format: {Url}", url);
			}
			catch (Exception e)
			{
				_logger.LogError(e, "An unexpected error occurred while processing sitemap: {Url}", url);
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

			const int batchSize = 100;
			var batches = urls.Select((url, index) => new { url, index })
							  .GroupBy(x => x.index / batchSize)
							  .Select(g => g.Select(x => x.url).ToList());

			foreach (var batch in batches)
			{
				var message = new { source = sitemapSource, urls = batch };
				var messageBody = JsonSerializer.Serialize(message);
				var serviceBusMessage = new ServiceBusMessage(messageBody);

				try
				{
					await _sender.SendMessageAsync(serviceBusMessage);
					_logger.LogInformation("Sent batch of {BatchCount} URLs to Service Bus queue for sitemap: {SitemapSource}", batch.Count, sitemapSource);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error sending message to Service Bus for sitemap: {SitemapSource}. Error message: {message}", sitemapSource, ex.Message);
				}
			}
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

		/// <summary>
		/// Sends a swap command message to Azure Service Bus.
		/// </summary>
		/// <param name="swapCommand">The swap command ("begin" or "end").</param>
		/// <param name="sitemapSource">The source URL of the sitemap.</param>
		/// <returns>A task representing the asynchronous operation.</returns>
		private async Task SendSwapCommandMessage(string swapCommand, string sitemapSource)
		{
			if (string.IsNullOrWhiteSpace(swapCommand) || string.IsNullOrWhiteSpace(sitemapSource))
			{
				_logger.LogError("Invalid swap command or sitemap source provided.");
				return;
			}

			var message = new { source = sitemapSource, indexSwap = swapCommand };
			var messageBody = JsonSerializer.Serialize(message);
			var serviceBusMessage = new ServiceBusMessage(messageBody);

			try
			{
				await _sender.SendMessageAsync(serviceBusMessage);
				_logger.LogInformation("Sent IndexSwap: {SwapCommand} message for sitemap: {SitemapSource}", swapCommand, sitemapSource);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error sending IndexSwap: {SwapCommand} message for sitemap: {SitemapSource}. Error message: {message}", swapCommand, sitemapSource, ex.Message);
			}
		}

		/// <summary>
		/// Asynchronously releases the unmanaged resources used by the CrawlIndexRunner.
		/// </summary>
		public async ValueTask DisposeAsync()
		{
			if (_sender != null)
			{
				await _sender.DisposeAsync();
			}
			if (_serviceBusClient != null)
			{
				await _serviceBusClient.DisposeAsync();
			}
			GC.SuppressFinalize(this);
		}
	}
}