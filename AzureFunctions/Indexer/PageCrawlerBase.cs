using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using AzureSearchCrawler;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AzureFunctions.Indexer
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
		internal readonly SearchIndexClient _indexClient;
		private readonly TextExtractor _textExtractor;
		private readonly List<MetaTagConfig> _metaFieldMappings;
		private readonly List<JsonLdConfig> _jsonLdMappings;

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

			_indexClient = new SearchIndexClient(new Uri(searchServiceEndpoint), new AzureKeyCredential(searchApiKey));

			_maxConcurrency = int.Parse(configuration["CrawlerMaxConcurrency"] ?? "3");
			_maxRetries = int.Parse(configuration["CrawlerMaxRetries"] ?? "3");

			_textExtractor = new TextExtractor(loggerFactory);

			_metaFieldMappings = configuration.GetSection("MetaFieldMappings").Get<List<MetaTagConfig>>();
			_jsonLdMappings = configuration.GetSection("JsonLdMappings").Get<List<JsonLdConfig>>();

			_logger.LogInformation("Page Crawler initialized with User-Agent: {UserAgent}, MaxConcurrency: {MaxConcurrency}, MaxRetries: {MaxRetries}",
				userAgent, _maxConcurrency, _maxRetries);
		}

		/// <summary>
		/// Crawls a single page asynchronously.
		/// </summary>
		/// <param name="url">The URL to crawl.</param>
		/// <param name="source">The source of the URL.</param>
		/// <returns>A task that represents the asynchronous operation. The task result contains the search document.</returns>
		/// <exception cref="ArgumentNullException">Thrown when url or source is null.</exception>
		public async Task<SearchDocument> CrawlPageAsync(string url, string source)
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
				var metaTags = ExtractMetaTags(doc);

				string uniqueId = GenerateUrlUniqueId(url);
				//var jsonLd = ExtractJsonLd(doc);
				var jsonLd = ExtractJsonLd(html);

				var searchDocument = new SearchDocument
				{
					["id"] = uniqueId,
					["url"] = url,
					["title"] = pageContent.Title,
					["metaTags"] = JsonSerializer.Serialize(metaTags),
					["htmlContent"] = pageContent.HtmlContent,
					["textContent"] = pageContent.TextContent,
					["source"] = source
				};

				// Process configured meta tags
				foreach (var config in _metaFieldMappings)
				{
					if (metaTags.TryGetValue(config.SourceMetaTag, out string metaValue))
					{
						if (TryConvertValue(metaValue, config.TargetType, out object convertedValue))
						{
							searchDocument[config.TargetField] = convertedValue;
						}
					}
				}


				if (jsonLd != null)
				{
					foreach (var config in _jsonLdMappings)
					{
						var token = jsonLd.SelectToken(config.SourceElementPath);
						if (token != null && !string.IsNullOrEmpty(config.TargetType) && TryConvertValue(token, config.TargetType, out object convertedValue))
						{
							searchDocument[config.TargetField] = convertedValue;
						}
					}
				}

				return searchDocument;
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

			var results = new ConcurrentBag<SearchDocument>();

			await Parallel.ForEachAsync(crawlRequest.Urls,
				new ParallelOptions { MaxDegreeOfParallelism = _maxConcurrency },
				async (url, ct) =>
				{
					var searchDocument = await ProcessUrlWithRetryAsync(url, crawlRequest.Source, ct);
					await Task.Delay(100, ct); // Slow down to prevent rate limiting
					results.Add(searchDocument);
				});

			_logger.LogInformation("Crawled {Count} pages from source {Source}", results.Count, crawlRequest.Source);
		}

		public static JObject ExtractJsonLd(string html)
		{
			var regex = new Regex(@"<script[^>]*type\s*=\s*[""']application/ld\+json[""'][^>]*>(.*?)</script>",
							  RegexOptions.Singleline | RegexOptions.IgnoreCase);
			var match = regex.Match(html);
			if (match.Success)
			{
				var jsonLdContent = match.Groups[1].Value.Trim();
				try
				{
					return JObject.Parse(jsonLdContent);
				}
				catch (JsonException ex)
				{
					Console.WriteLine($"Error parsing JSON-LD: {ex.Message}");
				}
			}
			else
			{
				Console.WriteLine("No JSON-LD script tag found in the HTML.");
			}

			return null;
		}
		public static JsonDocument ExtractJsonLd(HtmlDocument htmlDocument)
		{
			var scriptNodes = htmlDocument.DocumentNode.SelectNodes("//script[@type='application/ld+json']");

			if (scriptNodes != null)
			{
				foreach (var scriptNode in scriptNodes)
				{
					var jsonLdContent = scriptNode.InnerHtml;

					try
					{
						return JsonDocument.Parse(jsonLdContent);
					}
					catch (JsonException ex)
					{
						Console.WriteLine($"Error parsing JSON-LD: {ex.Message}");
					}
				}
			}

			return null;
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

		private async Task IndexSearchDocumentAsync(SearchDocument document, CancellationToken cancellationToken)
		{
			try
			{
				await _searchClient.MergeOrUploadDocumentsAsync(new[] { document }, cancellationToken: cancellationToken);
				_logger.LogInformation("Indexed document for {Url} with ID: {UniqueId} from source: {Source}",
					document["url"], document["id"], document["source"]);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error indexing document for {Url}", document["url"]);
				throw;
			}
		}

		private async Task<SearchDocument> ProcessUrlWithRetryAsync(string url, string source, CancellationToken cancellationToken)
		{
			for (int attempt = 1; attempt <= _maxRetries; attempt++)
			{
				try
				{
					var searchDocument = await CrawlPageAsync(url, source);
					await IndexSearchDocumentAsync(searchDocument, cancellationToken);
					return searchDocument;
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					_logger.LogWarning(ex, "Error processing {Url} (Attempt {Attempt}/{MaxRetries})", url, attempt, _maxRetries);
					if (attempt == _maxRetries)
					{
						_logger.LogError(ex, "Failed to process {Url} after {MaxRetries} attempts", url, _maxRetries);
						return new SearchDocument
						{
							["id"] = GenerateUrlUniqueId(url),
							["url"] = url,
							["error"] = ex.Message,
							["source"] = source
						};
					}
					await Task.Delay(1000 * attempt, cancellationToken); // Exponential backoff
				}
			}

			return new SearchDocument
			{
				["id"] = GenerateUrlUniqueId(url),
				["url"] = url,
				["error"] = "Unexpected error",
				["source"] = source
			};
		}

		public bool TryConvertValue(JToken token, string targetType, out object result)
		{
			result = null;
			try
			{
				switch (targetType.ToLower())
				{
					case "string":
						result = token.ToString();
						return true;

					case "int32":
					case "int":
						if (token.Type == JTokenType.Integer && token.Value<int>() is int intResult)
						{
							result = intResult;
							return true;
						}
						break;

					case "int64":
						if (token.Type == JTokenType.Integer && token.Value<long>() is long longResult)
						{
							result = longResult;
							return true;
						}
						break;

					case "double":
						if (token.Type == JTokenType.Float && token.Value<double>() is double doubleResult)
						{
							result = doubleResult;
							return true;
						}
						else if (double.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out doubleResult))
						{
							result = doubleResult;
							return true;
						}
						break;

					case "boolean":
						if (token.Type == JTokenType.Boolean && token.Value<bool>() is bool boolResult)
						{
							result = boolResult;
							return true;
						}
						else if (bool.TryParse(token.ToString(), out boolResult))
						{
							result = boolResult;
							return true;
						}
						break;

					case "datetimeoffset":
						if (token.Type == JTokenType.Date && token.Value<DateTimeOffset>() is DateTimeOffset dateTimeOffsetResult)
						{
							result = dateTimeOffsetResult;
							return true;
						}
						else if (DateTimeOffset.TryParse(token.ToString(), out dateTimeOffsetResult))
						{
							result = dateTimeOffsetResult;
							return true;
						}
						break;

					case "datetime":
						if (token.Type == JTokenType.Date && token.Value<DateTime>() is DateTime dateTimeResult)
						{
							result = new DateTimeOffset(dateTimeResult.ToUniversalTime(), TimeSpan.Zero);
							return true;
						}
						else if (DateTime.TryParse(token.ToString(), out dateTimeResult))
						{
							result = new DateTimeOffset(dateTimeResult.ToUniversalTime(), TimeSpan.Zero);
							return true;
						}
						break;

					case "geography":
						var coordinates = token.ToString().Split(',');
						if (coordinates.Length == 2 &&
							double.TryParse(coordinates[0], out double lat) &&
							double.TryParse(coordinates[1], out double lon))
						{
							result = $"POINT({lon} {lat})";
							return true;
						}
						break;
						// Add more cases as needed for other types
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Error converting JToken value '{Token}' to type {TargetType}", token, targetType);
			}

			return false;
		}
		private bool TryConvertValue(string value, string targetType, out object result)
		{
			result = null;
			try
			{
				switch (targetType.ToLower())
				{
					case "string":
						result = value;
						return true;
					case "int32":
					case "int":
						if (int.TryParse(value, out int intResult))
						{
							result = intResult;
							return true;
						}
						break;
					case "int64":
						if (long.TryParse(value, out long longResult))
						{
							result = longResult;
							return true;
						}
						break;
					case "double":
						if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double doubleResult))
						{
							result = doubleResult;
							return true;
						}
						break;
					case "boolean":
						if (bool.TryParse(value, out bool boolResult))
						{
							result = boolResult;
							return true;
						}
						break;
					case "datetimeoffset":
						if (DateTimeOffset.TryParse(value, out DateTimeOffset dateTimeOffsetResult))
						{
							result = dateTimeOffsetResult;
							return true;
						}
						break;
					case "datetime":
						if (DateTime.TryParse(value, out DateTime dateTimeResult))
						{
							result = result = new DateTimeOffset(dateTimeResult.ToUniversalTime(), TimeSpan.Zero);
							return true;
						}
						break;
					case "geography":
						// For simplicity, we'll assume the geography is in the format "lat,lon"
						var coordinates = value.Split(',');
						if (coordinates.Length == 2 &&
							double.TryParse(coordinates[0], out double lat) &&
							double.TryParse(coordinates[1], out double lon))
						{
							result = $"POINT({lon} {lat})";
							return true;
						}
						break;
						// Add more cases as needed for other EDM types
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Error converting meta value '{Value}' to type {TargetType}", value, targetType);
			}
			return false;
		}


		public async Task<bool> CloneIndexSchemaAsync(string sourceIndexName, string targetIndexName)
		{
			Console.WriteLine($"Cloning schema from '{sourceIndexName}' to '{targetIndexName}'");

			try
			{
				// Get the source index
				var sourceIndex = await _indexClient.GetIndexAsync(sourceIndexName);

				// Create a new index with the same schema
				var newIndex = new SearchIndex(targetIndexName)
				{
					Fields = sourceIndex.Value.Fields,
					DefaultScoringProfile = sourceIndex.Value.DefaultScoringProfile,
					CorsOptions = sourceIndex.Value.CorsOptions,
					VectorSearch = sourceIndex.Value.VectorSearch
				};

				// Add analyzers
				if (sourceIndex.Value.Analyzers != null)
				{
					foreach (var analyzer in sourceIndex.Value.Analyzers)
					{
						newIndex.Analyzers.Add(analyzer);
					}
				}

				// Add char filters
				if (sourceIndex.Value.CharFilters != null)
				{
					foreach (var charFilter in sourceIndex.Value.CharFilters)
					{
						newIndex.CharFilters.Add(charFilter);
					}
				}

				// Add tokenizers
				if (sourceIndex.Value.Tokenizers != null)
				{
					foreach (var tokenizer in sourceIndex.Value.Tokenizers)
					{
						newIndex.Tokenizers.Add(tokenizer);
					}
				}

				// Add token filters
				if (sourceIndex.Value.TokenFilters != null)
				{
					foreach (var tokenFilter in sourceIndex.Value.TokenFilters)
					{
						newIndex.TokenFilters.Add(tokenFilter);
					}
				}

				// Add scoring profiles
				if (sourceIndex.Value.ScoringProfiles != null)
				{
					foreach (var scoringProfile in sourceIndex.Value.ScoringProfiles)
					{
						newIndex.ScoringProfiles.Add(scoringProfile);
					}
				}

				// Add suggesters
				if (sourceIndex.Value.Suggesters != null)
				{
					foreach (var suggester in sourceIndex.Value.Suggesters)
					{
						newIndex.Suggesters.Add(suggester);
					}
				}


				// Set encryption key if present in the source index
				//if (sourceIndex.Value.EncryptionKey != null)
				//{
				//	newIndex.EncryptionKey = new SearchResourceEncryptionKey
				//	{
				//		KeyName = sourceIndex.Value.EncryptionKey.KeyName,
				//		KeyVersion = sourceIndex.Value.EncryptionKey.KeyVersion,
				//		VaultUrl = sourceIndex.Value.EncryptionKey.VaultUrl
				//	};
				//}

				// Create the new index
				var createIndexResponse = await _indexClient.CreateOrUpdateIndexAsync(newIndex);

				if (createIndexResponse.Value != null)
				{
					Console.WriteLine($"Schema cloned successfully. New index '{targetIndexName}' created.");
					return true;
				}
				else
				{
					Console.WriteLine($"Failed to create index '{targetIndexName}'. Please check the Azure portal for details.");
				}
			}
			catch (RequestFailedException ex)
			{
				Console.WriteLine($"Error cloning index schema: {ex.Message}");
				Console.WriteLine($"Status: {ex.Status}, Error Code: {ex.ErrorCode}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Unexpected error occurred: {ex.Message}");
			}

			return false;
		}
	}
	public class MetaTagConfig
	{
		public string SourceMetaTag { get; set; }
		public string TargetField { get; set; }
		public string TargetType { get; set; }
	}

	public class JsonLdConfig
	{
		public string SourceElementPath { get; set; }
		public string TargetField { get; set; }
		public string TargetType { get; set; }
	}
}