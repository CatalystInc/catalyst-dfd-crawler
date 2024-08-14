# WebCrawler
Simple web crawler that indexes pages to Azure Cognitive Search
## Functions
### CrawlIndexRunner
Timer-triggered function to grab sitemap index and queue up sitemap urls for processing

### PageCrawlerQueue
Pick up a sitemap urls from the queue and crawl the page

### PageCrawlerHttp
Index URLs from the passed payload

### Queue message / payload format
Set source to source sitemap url and urls to the list of urls to index when indexing is triggered by CrawlIndexRunner
```json
{
   "source": "https://www.catalyst.org/post-sitemap.xml",
   "urls": [
        "https://www.catalyst.org/2020/08/10/racism-gender-pay-gap-women/",
        "https://www.catalyst.org/2021/04/27/future-of-work-summit-europe-2021-takeaways/"
    ]
}
``


## Configuration items template
```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "ServiceBusQueueName": "catalyst-indexing-queue",
    "ServiceBusConnection": "Endpoint=[insert config here]",
    "CrawlIndexSchedule": "0 0 * * *",
    "SitemapsRootUrl": "https://www.catalyst.org/sitemap_index.xml",
    "SearchServiceEndpoint": "https://xccomaisearch.search.windows.net",
    "SearchIndexName": "catalyst-az-poc",
    "SearchApiKey": "[insert config here]",
    "CrawlerUserAgent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36",
    "pageContentExpression": "",
    "CrawlerMaxConcurrency": 3,
    "CrawlerMaxRetries": 3

  }
}
```