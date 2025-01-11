using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace AzureFunctions.Indexer
{
    public class IndexSwapCommandMessageSender : IAsyncDisposable
    {
        private readonly ILogger<IndexSwapCommandMessageSender> _logger;
        private readonly ServiceBusClient _serviceBusClient;
        private readonly ServiceBusSender _sender;

        public SwapCommandMessageSender(
            ILogger<IndexSwapCommandMessageSender> logger,
            string serviceBusConnectionString,
            string queueName)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceBusClient = new ServiceBusClient(serviceBusConnectionString);
            _sender = _serviceBusClient.CreateSender(queueName);
        }

        public async Task SendSwapCommandMessage(string swapCommand, string sitemapSource)
        {
            if (string.IsNullOrWhiteSpace(swapCommand) || string.IsNullOrWhiteSpace(sitemapSource))
            {
                _logger.LogError("Invalid swap command or sitemap source provided.");
                return;
            }

            var message = new { source = sitemapSource, indexSwap = swapCommand };
            var messageBody = JsonSerializer.Serialize(message);
            var serviceBusMessage = new ServiceBusMessage(messageBody);

            try
            {
                await _sender.SendMessageAsync(serviceBusMessage);
                _logger.LogInformation("Sent IndexSwap: {SwapCommand} message for sitemap: {SitemapSource}", swapCommand, sitemapSource);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending IndexSwap: {SwapCommand} message for sitemap: {SitemapSource}. Error message: {message}", swapCommand, sitemapSource, ex.Message);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_sender != null)
            {
                await _sender.DisposeAsync();
            }
            if (_serviceBusClient != null)
            {
                await _serviceBusClient.DisposeAsync();
            }
            GC.SuppressFinalize(this);
        }
    }
}