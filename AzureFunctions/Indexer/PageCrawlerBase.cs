using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using AzureFunctions.Cvent;
using AzureFunctions.Models;
using AzureSearchCrawler;
using Google.Protobuf.WellKnownTypes;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
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
		internal SearchClient _searchClient;
		internal readonly SearchIndexClient _searchIndexClient;
		private readonly IConfiguration _configuration;
		private readonly ILoggerFactory _loggerFactory;
        private readonly TextExtractor _textExtractor;
		private readonly List<MetaTagConfig> _metaFieldMappings;
		private readonly List<JsonLdConfig> _jsonLdMappings;
		private readonly string _searchIndexBaseName;
		private readonly string _searchServiceEndpoint;
		private readonly string _searchIndexAlias;
		private readonly string _searchApiKey;
		private readonly bool _deleteOldIndexOnSwap;
		private string _currentIndexName;
		private string _newIndexName;
		private string _eventFacetValue;
		private string _contentTypeFacetName;
		private string _tagFacetName;
		private string _dateFacetName;
		private string _eventTypeFacetName;
		private readonly string _eventActiveStatus = "Active";

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

            _configuration = configuration;
            _loggerFactory = loggerFactory;

            _logger = loggerFactory.CreateLogger<PageCrawlerBase>();
			_httpClient = new HttpClient();

			var userAgent = configuration["UserAgent"] ?? "DefaultCrawlerBot/1.0";
			_httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
			_httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
			{
				NoCache = true
			};

            _searchServiceEndpoint = configuration["SearchServiceEndpoint"] ?? throw new ArgumentNullException(nameof(configuration), "SearchServiceEndpoint is missing");
			_searchApiKey = configuration["SearchApiKey"] ?? throw new ArgumentNullException(nameof(configuration), "SearchApiKey is missing");
			_searchIndexAlias = configuration["SearchIndexAlias"] ?? throw new ArgumentNullException(nameof(configuration), "SearchIndexAlias is missing");
			_searchIndexBaseName = configuration["SearchIndexBaseName"] ?? throw new ArgumentNullException(nameof(configuration), "SearchIndexBaseName is missing");
			_deleteOldIndexOnSwap = bool.Parse(Environment.GetEnvironmentVariable("DeleteOldIndexOnSwap") ?? "false");

            _contentTypeFacetName = configuration["ContentTypeFacetName"] ?? throw new ArgumentNullException(nameof(configuration), "ContentTypeFacetName is missing");
            _eventFacetValue = configuration["EventFacetValue"] ?? throw new ArgumentNullException(nameof(configuration), "EventFacetValue is missing");
            _tagFacetName = configuration["TagFacetName"] ?? throw new ArgumentNullException(nameof(configuration), "TagFacetName is missing");
            _dateFacetName = configuration["DateFacetName"] ?? throw new ArgumentNullException(nameof(configuration), "DateFacetName is missing");
			_eventTypeFacetName = configuration["EventTypeFacetName"] ?? throw new ArgumentNullException(nameof(configuration), "EventTypeFacetName is missing");

            _searchIndexClient = new SearchIndexClient(new Uri(_searchServiceEndpoint), new AzureKeyCredential(_searchApiKey));
			InitializeIndexNames().Wait();

			_maxConcurrency = int.Parse(configuration["CrawlerMaxConcurrency"] ?? "3");
			_maxRetries = int.Parse(configuration["CrawlerMaxRetries"] ?? "3");

			_textExtractor = new TextExtractor(loggerFactory);

			_metaFieldMappings = configuration.GetSection("MetaFieldMappings").Get<List<MetaTagConfig>>();
			_jsonLdMappings = configuration.GetSection("JsonLdMappings").Get<List<JsonLdConfig>>();

			_logger.LogInformation("Page Crawler initialized with User-Agent: {UserAgent}, MaxConcurrency: {MaxConcurrency}, MaxRetries: {MaxRetries}",
				userAgent, _maxConcurrency, _maxRetries);
        }

		private async Task CreateAliasAsync(string indexName)
		{
			try
			{
				var newAlias = new SearchAlias(_searchIndexAlias, indexName);

				await _searchIndexClient.CreateAliasAsync(newAlias);
				_logger.LogInformation($"Created new alias {_searchIndexAlias} pointing to {indexName}");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $"Error creating alias {_searchIndexAlias}");
				// Depending on your error handling strategy, you might want to throw this exception
				// throw;
			}
		}

		private async Task InitializeIndexNames()
		{
			var indexes = _searchIndexClient.GetIndexNames().ToList();
			var index0 = $"{_searchIndexBaseName}_0";
			var index1 = $"{_searchIndexBaseName}_1";


			Response<SearchAlias> aliasResponse = null;
			try
			{
				aliasResponse = _searchIndexClient.GetAlias(_searchIndexAlias);
			}
			catch (RequestFailedException ex) when (ex.Status == 404)
			{
				// Alias doesn't exist, create it pointing to index0
				_currentIndexName = index0;
				await CreateAliasAsync(_currentIndexName);
			}

			// Determine current index
			if (indexes.Contains(index0) || indexes.Contains(index1))
			{
				// Both indexes exist, determine which one is current based on the alias
				try
				{
					aliasResponse ??= _searchIndexClient.GetAlias(_searchIndexAlias);

					var firstIndex = aliasResponse.Value.Indexes.FirstOrDefault();
					if (aliasResponse.Value != null && firstIndex != null)
					{
						_currentIndexName = firstIndex;
					}
					else
					{
						_currentIndexName = index0; // Default to index0 if alias exists but doesn't point to any index
					}
				}
				catch (RequestFailedException ex) when (ex.Status == 404)
				{
					// Alias doesn't exist, create it pointing to index0
					_currentIndexName = index0;
					await CreateAliasAsync(_currentIndexName);
				}
			}
			else if (indexes.Contains(index0))
			{
				_currentIndexName = index0;
				await CreateAliasAsync(_currentIndexName);
			}
			else if (indexes.Contains(index1))
			{
				_currentIndexName = index1;
				await CreateAliasAsync(_currentIndexName);
			}
			else
			{
				// Neither index exists, start with index0
				_currentIndexName = index0;
				// Note: We don't create the alias here because the index doesn't exist yet
			}

			_newIndexName = GetOtherIndexName(_currentIndexName);
			_logger.LogInformation($"Initialized with current index: {_currentIndexName}, new index: {_newIndexName}");
		}

		private static string GetOtherIndexName(string indexName)
		{
			return indexName.EndsWith("_0")
				? indexName.Substring(0, indexName.Length - 1) + "1"
				: indexName.Substring(0, indexName.Length - 1) + "0";
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
                var fetchUrl = url;

                // ensure fetching url using a timestamp within query string to avoid caching
                if (url.IndexOf("?") >= 0)
                {
                    fetchUrl = $"{url}&t={DateTime.UtcNow.ToTimestamp().Seconds}";
                }
                else
                {
                    fetchUrl = $"{url}?t={DateTime.UtcNow.ToTimestamp().Seconds}";
                }

                using var response = await _httpClient.GetAsync(fetchUrl);
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

					var facetsToken = jsonLd.SelectToken("facets");
					if (facetsToken != null)
					{
						var facetList = facetsToken.ToObject<List<FacetModel>>();
						if (facetList != null)
						{
							foreach (var item in facetList)
							{
								if (item.Indexable && !string.IsNullOrEmpty(item.FacetName) && item.FacetValues != null && item.FacetValues.Count > 0)
								{
									var fieldName = $"facet_{item.FacetName}";
									searchDocument[fieldName] = item.FacetValues;
								}
							}
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

			var results = new ConcurrentBag<SearchDocument>();

            if (crawlRequest.IndexSwap == "start")
			{
				await StartIndexSwapAsync();
			}

			var indexName = !string.IsNullOrEmpty(_currentIndexName) ? _currentIndexName : _newIndexName;
			_logger.LogInformation("Selected index name: {indexName}.", indexName);

			_searchClient = new SearchClient(
				new Uri(_searchServiceEndpoint),
				indexName,
				new AzureKeyCredential(_searchApiKey));

			// Loads Cvent data and index
			await ProcessCventInformation(crawlRequest.Source);

			// Crawls pages, extract data and upload to index
			if (crawlRequest.Urls != null && crawlRequest.Urls.Count > 0)
			{
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
			else
			{
				_logger.LogInformation("No pages to crawl in request");
			}

			if (crawlRequest.IndexSwap == "end")
			{
				await CompleteIndexSwapAsync();
			}
		}

		private async Task StartIndexSwapAsync()
		{
			_logger.LogInformation("Starting index swap process");

			try
			{
				// Attempt to get the index. If it doesn't exist, this will throw an exception
				await _searchIndexClient.GetIndexAsync(_newIndexName);

				// If we reach here, the index exists, so we delete it
				_logger.LogInformation($"Index {_newIndexName} already exists. Deleting it.");
				await _searchIndexClient.DeleteIndexAsync(_newIndexName);
			}
			catch (RequestFailedException ex) when (ex.Status == 404)
			{
				// Index doesn't exist, which is fine for our purposes
				_logger.LogInformation($"Index {_newIndexName} does not exist. Proceeding with creation.");
			}

			// Clone the existing index schema
			await CloneIndexSchemaAsync(_currentIndexName, _newIndexName);

			_logger.LogInformation($"New index {_newIndexName} created");
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
		static IReadOnlyDictionary<string, string> ExtractMetaTags(HtmlDocument doc)
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

		static string GenerateUrlUniqueId(string url)
		{
			using var sha256 = SHA256.Create();
			byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(url));
			return Convert.ToBase64String(hashBytes)
				.Replace("_", "-")
				.Replace("/", "-")
				.Replace("+", "-")
				.TrimEnd('=');
		}

		async Task IndexSearchDocumentAsync(SearchDocument document, CancellationToken cancellationToken)
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

		async Task<SearchDocument> ProcessUrlWithRetryAsync(string url, string source, CancellationToken cancellationToken)
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
					_logger.LogError(ex, "Error processing {Url} (Attempt {Attempt}/{MaxRetries}). Error message: {error}", url, attempt, _maxRetries, ex.Message);
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

		async Task ProcessCventInformation(string source)
		{
			var cventAPI = new CventAPIService(_configuration, _loggerFactory);
			var authorization = await cventAPI.GetAccessToken();

			if (authorization != null)
			{
				var eventDocuments = new List<SearchDocument>();
                var results = new List<SearchDocument>();
				for (int attempt = 1; attempt <= _maxRetries; attempt++)
				{
					var eventsData = await cventAPI.GetEvents(accessToken: authorization.AccessToken);

					if (eventsData?.Data?.Count > 0)
					{
						var dataMapper = new CventDataMapper(_configuration, _loggerFactory);
                        foreach (var eventEntry in eventsData?.Data.Where(data =>
                            !string.IsNullOrEmpty(data.Status) &&
							data.Status.Equals(_eventActiveStatus, StringComparison.CurrentCultureIgnoreCase)))
						{
							var eventDetails = dataMapper.ToEventDetailsModel(eventEntry);

							var searchDocument = new SearchDocument
							{
								["id"] = eventDetails.Id,
								["type"] = eventDetails.Type,
								["url"] = eventDetails.Url,
								["title"] = eventDetails.Title,
								["source"] = source,
								["htmlContent"] = eventDetails.HtmlDescription,
								["textContent"] = eventDetails.TextDescription,
								[_contentTypeFacetName] = new List<string> { _eventFacetValue },
								[_tagFacetName] = eventDetails.Tags,
								[_dateFacetName] = eventDetails.StartDate
                            };

							if (!string.IsNullOrEmpty(eventDetails.EventTypeFacet))
							{
								searchDocument[_eventTypeFacetName] = new List<string> { eventDetails.EventTypeFacet };
                            }

							eventDocuments.Add(searchDocument);
						}
						break;
					}
					else
					{
						_logger.LogWarning($"Cvent processing: NO event data found, attempt: {attempt}");
                        await Task.Delay(1000 * attempt); // Exponential backoff
                    }
				}

                if (eventDocuments.Count > 0)
                {
                    await Parallel.ForEachAsync(eventDocuments,
                        new ParallelOptions { MaxDegreeOfParallelism = _maxConcurrency },
                        async (eventDocument, ct) =>
                        {
                            var searchDocument = await ProcessEventAsync(eventDocument["title"].ToString(), eventDocument["url"].ToString(), eventDocument, ct);
                            await Task.Delay(100, ct); // Slow down to prevent rate limiting
                            results.Add(searchDocument);
                        });

                    _logger.LogInformation("Indexed {Count} events from Cvent", results.Count);
                }
                else
                {
                    _logger.LogWarning("No events to index in request");
                }
            }
			else
			{
				_logger.LogWarning("Cvent processing: could not get an access token.");
			}
        }

        async Task<SearchDocument> ProcessEventAsync(string eventTitle, string eventUrl, SearchDocument document, CancellationToken cancellationToken)
        {
            try
            {
                await IndexSearchDocumentAsync(document, cancellationToken);
                return document;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Error processing {eventUrl} for event: {eventTitle})", eventUrl, eventTitle);
				_logger.LogError(ex, "Failed to process event {eventUrl}", eventUrl);
				document["error"] = ex.Message;
				return document;
            }
        }

        bool TryConvertValue(JToken token, string targetType, out object result)
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
		bool TryConvertValue(string value, string targetType, out object result)
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

		private async Task DeleteOldIndexAsync(string indexName)
		{
			if(!_deleteOldIndexOnSwap)
			{
				_logger.LogInformation("DeleteOldIndexOnSwap is set to false. Skipping deletion of old index.");
				return;
			}
			try
			{
				await _searchIndexClient.DeleteIndexAsync(indexName);
				_logger.LogInformation($"Successfully deleted old index: {indexName}");
			}
			catch (RequestFailedException ex) when (ex.Status == 404)
			{
				_logger.LogWarning($"Old index {indexName} not found, it may have been already deleted.");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $"Error deleting old index {indexName}");
				// Depending on your error handling strategy, you might want to throw this exception
				// throw;
			}
		}
		private async Task CompleteIndexSwapAsync()
		{
			_logger.LogInformation("Completing index swap process");
			if (string.IsNullOrEmpty(_newIndexName))
			{
				_logger.LogWarning("No new index to swap to. Swap process was not started.");
				return;
			}

			string oldIndexName = _currentIndexName;

			try
			{
				// Check if the alias exists
				var aliasResponse = await _searchIndexClient.GetAliasAsync(_searchIndexAlias);
				if (aliasResponse != null && aliasResponse.Value != null)
				{
					// Alias exists, update it
					var aliasUpdate = new SearchAlias(_searchIndexAlias, _newIndexName);
					await _searchIndexClient.CreateOrUpdateAliasAsync(_searchIndexAlias, aliasUpdate);
					_logger.LogInformation($"Updated existing alias {_searchIndexAlias} to point to {_newIndexName}");
				}
				else
				{
					// Alias doesn't exist, create it
					var newAlias = new SearchAlias(_searchIndexAlias, _newIndexName);
					await _searchIndexClient.CreateAliasAsync(newAlias);
					_logger.LogInformation($"Created new alias {_searchIndexAlias} pointing to {_newIndexName}");
				}

				// Swap completed successfully, now delete the old index
				await DeleteOldIndexAsync(oldIndexName);

				_logger.LogInformation($"Index swap completed. Alias {_searchIndexAlias} now points to {_newIndexName}");

				// Update the current index name
				_currentIndexName = _newIndexName;
				_newIndexName = GetOtherIndexName(_currentIndexName);
			}
			catch (RequestFailedException ex) when (ex.Status == 404)
			{
				// Alias doesn't exist, create it
				var newAlias = new SearchAlias(_searchIndexAlias, _newIndexName);
				await _searchIndexClient.CreateAliasAsync(newAlias);
				_logger.LogInformation($"Created new alias {_searchIndexAlias} pointing to {_newIndexName}");

				// Swap completed successfully, now delete the old index
				await DeleteOldIndexAsync(oldIndexName);

				// Update the current index name
				_currentIndexName = _newIndexName;
				_newIndexName = GetOtherIndexName(_currentIndexName);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $"Error updating alias {_searchIndexAlias}");
				throw;
			}
		}

		public async Task<bool> CloneIndexSchemaAsync(string sourceIndexName, string targetIndexName)
		{
			Console.WriteLine($"Cloning schema from '{sourceIndexName}' to '{targetIndexName}'");

			try
			{
				// Get the source index
				var sourceIndex = await _searchIndexClient.GetIndexAsync(sourceIndexName);

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
				var createIndexResponse = await _searchIndexClient.CreateOrUpdateIndexAsync(newIndex);

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
}