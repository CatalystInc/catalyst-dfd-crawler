using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Azure.Search.Documents;
using Azure;

var host = new HostBuilder()
				.ConfigureFunctionsWorkerDefaults()
				.ConfigureAppConfiguration((context, config) =>
				{
					config.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
						  .AddEnvironmentVariables();
				})
				.ConfigureServices((context, services) =>
				{
					services.AddHttpClient();
					//services.AddSingleton<PageCrawlerQueue>();
					//services.AddSingleton<IConfiguration>(context.Configuration);

					// Add any other services your application needs here
					// For example, if you have a custom crawler service:
					// services.AddSingleton<ICrawler, Crawler>();
				})
				.Build();

host.Run();