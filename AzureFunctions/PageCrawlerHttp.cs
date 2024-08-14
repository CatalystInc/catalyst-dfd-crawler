using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public class PageCrawlerHttp : PageCrawlerBase
{
	public PageCrawlerHttp(IConfiguration configuration, ILoggerFactory loggerFactory)
		: base(configuration, loggerFactory)
	{
		
	}

	[Function("PageCrawlerHttp")]
	public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
	{
		_logger.LogInformation("C# HTTP trigger function processing a request.");

		CrawlRequest crawlRequest;
		try
		{
			string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
			crawlRequest = JsonSerializer.Deserialize<CrawlRequest>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		}
		catch (JsonException ex)
		{
			_logger.LogError(ex, "Error deserializing request body");
			return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Invalid JSON in request body.");
		}

		if (crawlRequest?.urls == null || crawlRequest.urls.Count == 0)
		{
			_logger.LogWarning("Request received with no URLs");
			return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Please provide an array of URLs in the request body.");
		}

		if (string.IsNullOrWhiteSpace(crawlRequest.source))
		{
			_logger.LogWarning("Request received with no source");
			return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Please provide a source in the request body.");
		}

		try
		{
			await CrawlPages(crawlRequest);
			_logger.LogInformation("Successfully crawled {UrlCount} pages from source {Source}", crawlRequest.urls.Count, crawlRequest.source);

			var response = req.CreateResponse(HttpStatusCode.OK);
			await response.WriteStringAsync($"Successfully processed {crawlRequest.urls.Count} URLs.");
			return response;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error occurred while crawling pages");
			return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "An error occurred while processing the request.");
		}
	}

	private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, HttpStatusCode statusCode, string message)
	{
		var response = req.CreateResponse(statusCode);
		await response.WriteStringAsync(message);
		return response;
	}
}