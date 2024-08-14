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

public class PageCrawlerBase
{
	internal readonly HttpClient _httpClient;
	internal readonly ILogger<PageCrawlerBase> _logger;
	private readonly int _maxConcurrency;
	private readonly int _maxRetries;
	internal readonly SearchClient _searchClient;
	private readonly TextExtractor _textExtractor = new TextExtractor();

	public PageCrawlerBase(IConfiguration configuration, ILoggerFactory loggerFactory)
	{
		_logger = loggerFactory.CreateLogger<PageCrawlerBase>();
		_httpClient = new HttpClient();

		string userAgent = configuration["UserAgent"] ?? "DefaultCrawlerBot/1.0";
		_httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);

		string searchServiceEndpoint = configuration["SearchServiceEndpoint"] ?? throw new ArgumentNullException(nameof(configuration), "SearchServiceEndpoint is missing");
		string searchIndexName = configuration["SearchIndexName"] ?? throw new ArgumentNullException(nameof(configuration), "SearchIndexName is missing");
		string searchApiKey = configuration["SearchApiKey"] ?? throw new ArgumentNullException(nameof(configuration), "SearchApiKey is missing");

		_searchClient = new SearchClient(
			new Uri(searchServiceEndpoint),
			searchIndexName,
			new AzureKeyCredential(searchApiKey));

		_maxConcurrency = int.Parse(configuration["CrawlerMaxConcurrency"] ?? "3");
		_maxRetries = int.Parse(configuration["CrawlerMaxRetries"] ?? "3");

		_logger.LogInformation("Page Crawler initialized with User-Agent: {UserAgent}, MaxConcurrency: {MaxConcurrency}, MaxRetries: {MaxRetries}",
			userAgent, _maxConcurrency, _maxRetries);
	}

	public async Task<PageInfo> CrawlPageAsync(string url, string source)
	{
		_logger.LogInformation("Crawling {Url} from source {Source}", url, source);

		try
		{
			var response = await _httpClient.GetAsync(url);

			response.EnsureSuccessStatusCode();

			var html = await response.Content.ReadAsStringAsync();
			var doc = new HtmlDocument();
			doc.LoadHtml(html);

			//var mainElement = doc.DocumentNode.SelectSingleNode("//main");
			//string mainContentHtml = mainElement?.InnerHtml ?? string.Empty;
			//string mainContentText = mainElement?.InnerText?.Trim() ?? string.Empty;

			var pageContent = _textExtractor.ExtractPageContent(doc);

			var pageInfo = new PageInfo
			{
				Url = url,
				Title = pageContent.Title,
				MetaTags = JsonSerializer.Serialize(ExtractMetaTags(doc)),
				HtmlContent = pageContent.HtmlContent,
				TextContent = pageContent.TextContent,

				Source = source
			};

			return pageInfo;
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

	public async Task CrawlPages(CrawlRequest crawlRequest)
	{
		if (crawlRequest == null || crawlRequest.urls == null || !crawlRequest.urls.Any())
		{
			_logger.LogWarning("Invalid crawl request received");
			return;
		}

		var results = new ConcurrentBag<PageInfo>();

		await Parallel.ForEachAsync(crawlRequest.urls, new ParallelOptions { MaxDegreeOfParallelism = _maxConcurrency },
			async (url, ct) =>
			{
				var pageInfo = await ProcessUrlWithRetryAsync(url, crawlRequest.source);
				await Task.Delay(100); // Slow down to prevent rate limiting
				results.Add(pageInfo);
			});

		_logger.LogInformation("Crawled {Count} pages from source {Source}", results.Count, crawlRequest.source);
	}

	private Dictionary<string, string> ExtractMetaTags(HtmlDocument doc)
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
		var key = Convert.ToBase64String(hashBytes)
			.Replace("_", "-") // Remove the padding
			.Replace("/", "-")
			.Replace("+", "-")
			.TrimEnd('=');  // Remove any trailing '='

		return key;
	}

	private async Task IndexPageInfoAsync(PageInfo pageInfo)
	{
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
			await _searchClient.MergeOrUploadDocumentsAsync(new[] { document });
			_logger.LogInformation("Indexed document for {Url} with ID: {UniqueId} from source: {Source}",
				pageInfo.Url, uniqueId, pageInfo.Source);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error indexing document for {Url}", pageInfo.Url);
			throw;
		}
	}

	private async Task<PageInfo> ProcessUrlWithRetryAsync(string url, string source)
	{
		for (int attempt = 1; attempt <= _maxRetries; attempt++)
		{
			try
			{
				var pageInfo = await CrawlPageAsync(url, source);
				await IndexPageInfoAsync(pageInfo);
				return pageInfo;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Error processing {Url} (Attempt {Attempt}/{MaxRetries})", url, attempt, _maxRetries);
				if (attempt == _maxRetries)
				{
					_logger.LogError(ex, "Failed to process {Url} after {MaxRetries} attempts", url, _maxRetries);
					return new PageInfo { Url = url, Error = ex.Message, Source = source };
				}
				await Task.Delay(1000 * attempt); // Exponential backoff
			}
		}

		return new PageInfo { Url = url, Error = "Unexpected error", Source = source };
	}
}

public class CrawlRequest
{
	public List<string> urls { get; set; }
	public string source { get; set; }
}

public class PageInfo
{
	public string Url { get; set; }
	public string Title { get; set; }
	public string MetaTags { get; set; }
	//public string HtmlContent { get; set; }
	public string HtmlContent { get; set; }
	public string TextContent { get; set; }
	public string Source { get; set; }
	public string Error { get; set; }
}