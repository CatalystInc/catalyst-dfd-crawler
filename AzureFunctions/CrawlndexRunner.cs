using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Xml.Linq;

namespace AzureSearchCrawler
{
	public class CrawlIndexRunner
	{
		private readonly ILogger<CrawlIndexRunner> _logger;
		private readonly string _serviceBusConnectionString;
		private readonly string _queueName;
		private readonly string _schedule;
		private readonly HttpClient _httpClient;
		private readonly ServiceBusClient _serviceBusClient;
		private readonly ServiceBusSender _sender;
		private readonly string _sitemapsRootUrl;

		public CrawlIndexRunner(ILoggerFactory loggerFactory, IConfiguration configuration)
		{
			_logger = loggerFactory.CreateLogger<CrawlIndexRunner>();
			_serviceBusConnectionString = configuration["ServiceBusConnection"] ?? throw new ArgumentNullException(nameof(_serviceBusConnectionString), "ServiceBusConnection configuration is missing");
			_queueName = configuration["ServiceBusQueueName"] ?? throw new ArgumentNullException(nameof(_queueName), "ServiceBusQueueName configuration is missing");
			_schedule = configuration["CrawlIndexSchedule"] ?? throw new ArgumentNullException(nameof(_schedule), "CrawlIndexSchedule configuration is missing");
			_sitemapsRootUrl = configuration["SitemapsRootUrl"] ?? throw new ArgumentNullException(nameof(_schedule), "SitemapsRootUrl configuration is missing");
			_httpClient = new HttpClient();

			_serviceBusClient = new ServiceBusClient(_serviceBusConnectionString);
			_sender = _serviceBusClient.CreateSender(_queueName);
		}

		public async Task ProcessSitemapAsync(string url)
		{
			try
			{
				_logger.LogInformation("Processing sitemap: {Url}", url);
				string xmlContent = await _httpClient.GetStringAsync(url);
				XDocument doc = XDocument.Parse(xmlContent);

				XNamespace ns = doc.Root.GetDefaultNamespace();

				switch (doc.Root.Name.LocalName)
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
			}
			catch (HttpRequestException e)
			{
				_logger.LogError(e, "Error downloading sitemap: {Url}", url);
			}
			catch (System.Xml.XmlException e)
			{
				_logger.LogError(e, "Error parsing sitemap XML: {Url}", url);
			}
			catch (Exception e)
			{
				_logger.LogError(e, "An unexpected error occurred while processing sitemap: {Url}", url);
			}
		}

		private async Task ProcessSitemapIndexAsync(XDocument doc, XNamespace ns)
		{
			var sitemapTasks = doc.Root.Elements(ns + "sitemap")
				.Select(sitemapElement => sitemapElement.Element(ns + "loc"))
				.Where(locElement => locElement != null)
				.Select(locElement => ProcessSitemapAsync(locElement.Value.Trim()));

			await Task.WhenAll(sitemapTasks);
		}

		private List<string> ExtractUrlsFromSitemap(XDocument doc, XNamespace ns)
		{
			return doc.Root.Elements(ns + "url")
				.Select(urlElement => urlElement.Element(ns + "loc"))
				.Where(locElement => locElement != null)
				.Select(locElement => locElement.Value.Trim())
				.ToList();
		}

		private async Task ProcessSitemapUrlsAsync(string sitemapSource, List<string> urls)
		{
			_logger.LogInformation("Processing {UrlCount} URLs from sitemap: {SitemapSource}", urls.Count, sitemapSource);

			if (string.IsNullOrEmpty(sitemapSource) || urls == null || urls.Count == 0)
			{
				_logger.LogWarning("No valid sitemap source or URLs to process.");
				return;
			}

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
					_logger.LogInformation("Sent batch of {BatchCount} URLs to Service Bus queue.", batch.Count);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error sending message to Service Bus for sitemap: {SitemapSource}", sitemapSource);
				}
			}
		}

		[Function("CrawlIndexRunner")]
		public async Task RunAsync([TimerTrigger("%CrawlIndexSchedule%")] TimerInfo myTimer)
		{
			_logger.LogInformation("CrawlIndexRunner function executed at: {CurrentTime}", DateTime.Now);
			await ProcessSitemapAsync(_sitemapsRootUrl);
		}
	}
}