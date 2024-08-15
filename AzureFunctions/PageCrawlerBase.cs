using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using AzureSearchCrawler;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AzureSearchCrawler
{
	/// <summary>
	/// Base class for crawling web pages and indexing their content in Azure Search.
	/// </summary>
	public class PageCrawlerBase
	{
		internal readonly HttpClient _httpClient;
		internal readonly ILogger<PageCrawlerBase> _logger;
		private readonly int _maxConcurrency;
		private readonly int _maxRetries;
		internal readonly SearchClient _searchClient;
		private readonly TextExtractor _textExtractor;

		/// <summary>
		/// Initializes a new instance of the <see cref="PageCrawlerBase"/> class.
		/// </summary>
		/// <param name="configuration">The configuration.</param>
		/// <param name="loggerFactory">The logger factory.</param>
		/// <exception cref="ArgumentNullException">Thrown when configuration or loggerFactory is null.</exception>
		public PageCrawlerBase(IConfiguration configuration, ILoggerFactory loggerFactory)
		{
			ArgumentNullException.ThrowIfNull(configuration);
			ArgumentNullException.ThrowIfNull(loggerFactory);

			_logger = loggerFactory.CreateLogger<PageCrawlerBase>();
			_httpClient = new HttpClient();

			var userAgent = configuration["UserAgent"] ?? "DefaultCrawlerBot/1.0";
			_httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);

			var searchServiceEndpoint = configuration["SearchServiceEndpoint"] ?? throw new ArgumentNullException(nameof(configuration), "SearchServiceEndpoint is missing");
			var searchIndexName = configuration["SearchIndexName"] ?? throw new ArgumentNullException(nameof(configuration), "SearchIndexName is missing");
			var searchApiKey = configuration["SearchApiKey"] ?? throw new ArgumentNullException(nameof(configuration), "SearchApiKey is missing");

			_searchClient = new SearchClient(
				new Uri(searchServiceEndpoint),
				searchIndexName,
				new AzureKeyCredential(searchApiKey));

			_maxConcurrency = int.Parse(configuration["CrawlerMaxConcurrency"] ?? "3");
			_maxRetries = int.Parse(configuration["CrawlerMaxRetries"] ?? "3");

			_textExtractor = new TextExtractor(loggerFactory);

			_logger.LogInformation("Page Crawler initialized with User-Agent: {UserAgent}, MaxConcurrency: {MaxConcurrency}, MaxRetries: {MaxRetries}",
				userAgent, _maxConcurrency, _maxRetries);

		}

		/// <summary>
		/// Crawls a single page asynchronously.
		/// </summary>
		/// <param name="url">The URL to crawl.</param>
		/// <param name="source">The source of the URL.</param>
		/// <returns>A task that represents the asynchronous operation. The task result contains the page information.</returns>
		/// <exception cref="ArgumentNullException">Thrown when url or source is null.</exception>
		public async Task<PageInfo> CrawlPageAsync(string url, string source)
		{
			ArgumentNullException.ThrowIfNull(url);
			ArgumentNullException.ThrowIfNull(source);

			_logger.LogInformation("Crawling {Url} from source {Source}", url, source);

			try
			{
				using var response = await _httpClient.GetAsync(url);
				response.EnsureSuccessStatusCode();

				var html = await response.Content.ReadAsStringAsync();
				var doc = new HtmlDocument();
				doc.LoadHtml(html);

				var pageContent = _textExtractor.ExtractPageContent(doc);

				return new PageInfo
				{
					Url = url,
					Title = pageContent.Title,
					MetaTags = JsonSerializer.Serialize(ExtractMetaTags(doc)),
					HtmlContent = pageContent.HtmlContent,
					TextContent = pageContent.TextContent,
					Source = source
				};
			}
			catch (HttpRequestException ex)
			{
				_logger.LogError(ex, "HTTP request error while crawling {Url}", url);
				throw;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Unexpected error while crawling {Url}", url);
				throw;
			}
		}

		/// <summary>
		/// Crawls multiple pages based on the provided crawl request.
		/// </summary>
		/// <param name="crawlRequest">The crawl request containing URLs to crawl.</param>
		/// <returns>A task that represents the asynchronous operation.</returns>
		/// <exception cref="ArgumentNullException">Thrown when crawlRequest is null.</exception>
		public async Task CrawlPagesAsync(CrawlRequest crawlRequest)
		{
			ArgumentNullException.ThrowIfNull(crawlRequest);

			if (crawlRequest.Urls is not { Count: > 0 })
			{
				_logger.LogWarning("Invalid crawl request received");
				return;
			}

			var results = new ConcurrentBag<PageInfo>();

			await Parallel.ForEachAsync(crawlRequest.Urls,
				new ParallelOptions { MaxDegreeOfParallelism = _maxConcurrency },
				async (url, ct) =>
				{
					var pageInfo = await ProcessUrlWithRetryAsync(url, crawlRequest.Source, ct);
					await Task.Delay(100, ct); // Slow down to prevent rate limiting
					results.Add(pageInfo);
				});

			_logger.LogInformation("Crawled {Count} pages from source {Source}", results.Count, crawlRequest.Source);
		}

		private static IReadOnlyDictionary<string, string> ExtractMetaTags(HtmlDocument doc)
		{
			var metaTags = new Dictionary<string, string>();
			var nodes = doc.DocumentNode.SelectNodes("//meta");

			if (nodes != null)
			{
				foreach (var node in nodes)
				{
					var name = node.GetAttributeValue("name", null) ??
							   node.GetAttributeValue("property", null);
					var content = node.GetAttributeValue("content", null);

					if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(content))
					{
						metaTags[name] = content;
					}
				}
			}

			return metaTags;
		}

		private static string GenerateUrlUniqueId(string url)
		{
			using var sha256 = SHA256.Create();
			byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(url));
			return Convert.ToBase64String(hashBytes)
				.Replace("_", "-")
				.Replace("/", "-")
				.Replace("+", "-")
				.TrimEnd('=');
		}

		private async Task IndexPageInfoAsync(PageInfo pageInfo, CancellationToken cancellationToken)
		{
			ArgumentNullException.ThrowIfNull(pageInfo);

			string uniqueId = GenerateUrlUniqueId(pageInfo.Url);

			var document = new SearchDocument
			{
				["id"] = uniqueId,
				["url"] = pageInfo.Url,
				["title"] = pageInfo.Title,
				["metaTags"] = pageInfo.MetaTags,
				["htmlContent"] = pageInfo.HtmlContent,
				["textContent"] = pageInfo.TextContent,
				["source"] = pageInfo.Source
			};

			try
			{
				await _searchClient.MergeOrUploadDocumentsAsync(new[] { document }, cancellationToken: cancellationToken);
				_logger.LogInformation("Indexed document for {Url} with ID: {UniqueId} from source: {Source}",
					pageInfo.Url, uniqueId, pageInfo.Source);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error indexing document for {Url}", pageInfo.Url);
				throw;
			}
		}

		private async Task<PageInfo> ProcessUrlWithRetryAsync(string url, string source, CancellationToken cancellationToken)
		{
			for (int attempt = 1; attempt <= _maxRetries; attempt++)
			{
				try
				{
					var pageInfo = await CrawlPageAsync(url, source);
					await IndexPageInfoAsync(pageInfo, cancellationToken);
					return pageInfo;
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					_logger.LogWarning(ex, "Error processing {Url} (Attempt {Attempt}/{MaxRetries})", url, attempt, _maxRetries);
					if (attempt == _maxRetries)
					{
						_logger.LogError(ex, "Failed to process {Url} after {MaxRetries} attempts", url, _maxRetries);
						return new PageInfo { Url = url, Error = ex.Message, Source = source };
					}
					await Task.Delay(1000 * attempt, cancellationToken); // Exponential backoff
				}
			}

			return new PageInfo { Url = url, Error = "Unexpected error", Source = source };
		}
	}
}