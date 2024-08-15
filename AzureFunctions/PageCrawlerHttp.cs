using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AzureSearchCrawler
{
	/// <summary>
	/// HTTP-triggered Azure Function for crawling web pages.
	/// </summary>
	public class PageCrawlerHttp : PageCrawlerBase
	{
		private readonly JsonSerializerOptions _jsonOptions;

		/// <summary>
		/// Initializes a new instance of the <see cref="PageCrawlerHttp"/> class.
		/// </summary>
		/// <param name="configuration">The configuration.</param>
		/// <param name="loggerFactory">The logger factory.</param>
		public PageCrawlerHttp(IConfiguration configuration, ILoggerFactory loggerFactory)
			: base(configuration, loggerFactory)
		{
			_jsonOptions = new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			};
		}

		/// <summary>
		/// Processes the HTTP request to crawl pages.
		/// </summary>
		/// <param name="req">The HTTP request data.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <returns>The HTTP response data.</returns>
		[Function("PageCrawlerHttp")]
		public async Task<HttpResponseData> RunAsync(
			[HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
			CancellationToken cancellationToken)
		{
			_logger.LogInformation("C# HTTP trigger function processing a request.");

			CrawlRequest? crawlRequest;
			try
			{
				crawlRequest = await JsonSerializer.DeserializeAsync<CrawlRequest>(
					req.Body, _jsonOptions, cancellationToken);

				if (crawlRequest is null)
				{
					return await CreateErrorResponseAsync(req, HttpStatusCode.BadRequest, "Request body is empty.");
				}
			}
			catch (JsonException ex)
			{
				_logger.LogError(ex, "Error deserializing request body");
				return await CreateErrorResponseAsync(req, HttpStatusCode.BadRequest, "Invalid JSON in request body.");
			}

			if (crawlRequest.Urls is not { Count: > 0 })
			{
				_logger.LogWarning("Request received with no URLs");
				return await CreateErrorResponseAsync(req, HttpStatusCode.BadRequest, "Please provide an array of URLs in the request body.");
			}

			if (string.IsNullOrWhiteSpace(crawlRequest.Source))
			{
				_logger.LogWarning("Request received with no source");
				return await CreateErrorResponseAsync(req, HttpStatusCode.BadRequest, "Please provide a source in the request body.");
			}

			try
			{
				await base.CrawlPagesAsync(crawlRequest);
				_logger.LogInformation("Successfully crawled {UrlCount} pages from source {Source}", crawlRequest.Urls.Count, crawlRequest.Source);

				var response = req.CreateResponse(HttpStatusCode.OK);
				await response.WriteStringAsync($"Successfully processed {crawlRequest.Urls.Count} URLs.");
				return response;
			}
			catch (OperationCanceledException)
			{
				_logger.LogWarning("Operation was canceled");
				return await CreateErrorResponseAsync(req, HttpStatusCode.RequestTimeout, "The operation was canceled due to timeout.");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error occurred while crawling pages");
				return await CreateErrorResponseAsync(req, HttpStatusCode.InternalServerError, "An error occurred while processing the request.");
			}
		}

		private static async Task<HttpResponseData> CreateErrorResponseAsync(HttpRequestData req, HttpStatusCode statusCode, string message)
		{
			var response = req.CreateResponse(statusCode);
			await response.WriteStringAsync(message);
			return response;
		}
	}
}