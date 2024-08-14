using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

public class PageCrawlerQueue : PageCrawlerBase
{
	private readonly string _queueName;
	private readonly ServiceBusClient _serviceBusClient;
	public PageCrawlerQueue(IConfiguration configuration, ILoggerFactory loggerFactory) : base(configuration, loggerFactory)
	{
		_queueName = configuration["ServiceBusQueueName"];
		var serviceBusConnection = configuration["ServiceBusConnection"];

		if (string.IsNullOrEmpty(_queueName))
		{
			throw new ArgumentNullException(nameof(_queueName), "ServiceBusQueueName configuration is missing");
		}
		if (string.IsNullOrEmpty(serviceBusConnection))
		{
			throw new ArgumentNullException("ServiceBusConnection", "ServiceBusConnection configuration is missing");
		}
		_serviceBusClient = new ServiceBusClient(serviceBusConnection);
	}

	[Function("PageCrawlerQueue")]
	public async Task Run([ServiceBusTrigger("%ServiceBusQueueName%", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage triggerMessage,
		ServiceBusMessageActions triggerMessageActions)
	{
		_logger.LogInformation("CrawlPagesServiceBus function triggered. Processing messages...");
		await using var receiver = _serviceBusClient.CreateReceiver(_queueName);

		try
		{
			// Process the trigger message
			if (triggerMessage?.Body != null)
			{
				await ProcessMessage(triggerMessage, receiver);
				//await triggerMessageActions.CompleteMessageAsync(triggerMessage);
			}
			else
			{
				_logger.LogWarning("Trigger message was null or empty.");
			}

			// Check for and process any remaining messages
			while (true)
			{
				var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
				if (message == null)
				{
					_logger.LogInformation("No more messages in the queue. Exiting.");
					break;
				}

				await ProcessMessage(message, receiver);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error in message processing: {ex.Message}");
			if (triggerMessage != null)
			{
				await triggerMessageActions.AbandonMessageAsync(triggerMessage);
			}
			// Consider implementing a circuit breaker or backoff strategy here
		}
	}


	private async Task ProcessMessage(ServiceBusReceivedMessage message, ServiceBusReceiver receiver)
	{
		_logger.LogInformation("Processing message: {MessageId}", message.MessageId);

		try
		{
			var jsonDocument = JsonDocument.Parse(message.Body.ToString());
			//var crawlRequest = JsonSerializer.Deserialize<CrawlRequest>(jsonDocument);
			//var crawlRequest = jsonDocument.RootElement.Deserialize<CrawlRequest>();
			var crawlRequest = JsonSerializer.Deserialize<CrawlRequest>(jsonDocument, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

			await CrawlPages(crawlRequest);
			_logger.LogInformation("Message {MessageId} processed and removed from the queue", message.MessageId);
			await receiver.CompleteMessageAsync(message);
			return;
		}
		catch (JsonException ex)
		{
			_logger.LogError($"Error parsing JSON: {ex.Message}");
			// Abandon the message so it can be processed again
			await receiver.AbandonMessageAsync(message);
			_logger.LogWarning("Message {MessageId} abandoned due to JSON parsing error", message.MessageId);
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error processing message: {ex.Message}");
			// Abandon the message so it can be processed again
			await receiver.AbandonMessageAsync(message);
			_logger.LogWarning("Message {MessageId} abandoned due to processing error", message.MessageId);
		}
	}
}


