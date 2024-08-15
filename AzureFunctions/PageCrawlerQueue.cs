using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace AzureSearchCrawler
{
	/// <summary>
	/// Processes crawl requests from an Azure Service Bus queue.
	/// </summary>
	public class PageCrawlerQueue : PageCrawlerBase, IAsyncDisposable
	{
		private readonly string _queueName;
		private readonly ServiceBusClient _serviceBusClient;
		private readonly JsonSerializerOptions _jsonOptions;

		/// <summary>
		/// Initializes a new instance of the <see cref="PageCrawlerQueue"/> class.
		/// </summary>
		/// <param name="configuration">The configuration.</param>
		/// <param name="loggerFactory">The logger factory.</param>
		/// <exception cref="ArgumentNullException">Thrown when required configuration is missing.</exception>
		public PageCrawlerQueue(IConfiguration configuration, ILoggerFactory loggerFactory)
			: base(configuration, loggerFactory)
		{
			_queueName = configuration["ServiceBusQueueName"]
				?? throw new ArgumentNullException(nameof(configuration), "ServiceBusQueueName configuration is missing");

			var serviceBusConnection = configuration["ServiceBusConnection"]
				?? throw new ArgumentNullException(nameof(configuration), "ServiceBusConnection configuration is missing");

			_serviceBusClient = new ServiceBusClient(serviceBusConnection);

			_jsonOptions = new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			};

			_logger.LogInformation("PageCrawlerQueue initialized with queue: {QueueName}", _queueName);
		}

		/// <summary>
		/// Processes messages from the Service Bus queue.
		/// </summary>
		/// <param name="triggerMessage">The trigger message.</param>
		/// <param name="triggerMessageActions">Actions for the trigger message.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		[Function("PageCrawlerQueue")]
		public async Task RunAsync(
			[ServiceBusTrigger("%ServiceBusQueueName%", Connection = "ServiceBusConnection")]
			ServiceBusReceivedMessage triggerMessage,
			ServiceBusMessageActions triggerMessageActions,
			CancellationToken cancellationToken)
		{
			_logger.LogInformation("CrawlPagesServiceBus function triggered. Processing messages...");

			await using var receiver = _serviceBusClient.CreateReceiver(_queueName);
			try
			{
				// Process the trigger message
				if (triggerMessage?.Body is not null)
				{
					await ProcessMessageAsync(triggerMessage, receiver, cancellationToken);
				}
				else
				{
					_logger.LogWarning("Trigger message was null or empty.");
				}

				// Process remaining messages
				await ProcessRemainingMessagesAsync(receiver, cancellationToken);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error in message processing");
				if (triggerMessage is not null)
				{
					await triggerMessageActions.AbandonMessageAsync(triggerMessage);
				}
				// Consider implementing a circuit breaker or backoff strategy here
			}
		}

		private async Task ProcessRemainingMessagesAsync(ServiceBusReceiver receiver, CancellationToken cancellationToken)
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5), cancellationToken);
				if (message is null)
				{
					_logger.LogInformation("No more messages in the queue. Exiting.");
					break;
				}
				await ProcessMessageAsync(message, receiver, cancellationToken);
			}
		}

		private async Task ProcessMessageAsync(ServiceBusReceivedMessage message, ServiceBusReceiver receiver, CancellationToken cancellationToken)
		{
			_logger.LogInformation("Processing message: {MessageId}", message.MessageId);
			try
			{
				var crawlRequest = await JsonSerializer.DeserializeAsync<CrawlRequest>(
					new MemoryStream(message.Body.ToArray()), _jsonOptions, cancellationToken);

				if (crawlRequest is null)
				{
					throw new JsonException("Failed to deserialize CrawlRequest");
				}

				await CrawlPagesAsync(crawlRequest);
				_logger.LogInformation("Message {MessageId} processed and removed from the queue", message.MessageId);
				await receiver.CompleteMessageAsync(message, cancellationToken);
			}
			catch (JsonException ex)
			{
				_logger.LogError(ex, "Error parsing JSON for message {MessageId}", message.MessageId);
				await receiver.AbandonMessageAsync(message);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error processing message {MessageId}", message.MessageId);
				await receiver.AbandonMessageAsync(message);
			}
		}

		/// <summary>
		/// Asynchronously releases the unmanaged resources used by the PageCrawlerQueue.
		/// </summary>
		public async ValueTask DisposeAsync()
		{
			await _serviceBusClient.DisposeAsync();
			GC.SuppressFinalize(this);
		}
	}
}