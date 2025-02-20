using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AzureFunctions.Indexer
{
    /// <summary>
    /// Processes crawl requests from an Azure Service Bus queue.
    /// </summary>
    public class PageCrawlerQueue : PageCrawlerBase
    {
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="PageCrawlerQueue"/> class.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="loggerFactory">The logger factory.</param>
        /// <exception cref="ArgumentNullException">Thrown when required configuration is missing.</exception>
        public PageCrawlerQueue(IConfiguration configuration, ILoggerFactory loggerFactory) : base(configuration, loggerFactory)
        {
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            _logger.LogInformation("PageCrawlerQueue initialized.");
        }
    }
}