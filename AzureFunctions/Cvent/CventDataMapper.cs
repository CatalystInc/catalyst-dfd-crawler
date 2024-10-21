using AzureFunctions.Cvent.Models;
using AzureSearchCrawler;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureFunctions.Cvent
{
    public class CventDataMapper
    {
        private readonly TextExtractor _textExtractor;
        private readonly string _eventTypeMapping;
        private readonly Dictionary<string, string> _eventTypeMappingDict;

        private const string EVENT_TOPICS = "Event Topics";
        private const string EVENT_FORMAT = "Event Format";
        private const string URL_RELATIVE_PATH = "/events";

        public CventDataMapper(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            // EventTypeMapping
            _textExtractor = new TextExtractor(loggerFactory);
            _eventTypeMapping = configuration["EventTypeMapping"] ?? throw new ArgumentNullException(nameof(configuration), "EventTypeMapping is missing");

            _eventTypeMappingDict = new Dictionary<string, string>();
            var eventTypeMappingList = _eventTypeMapping.Split(";");            
            foreach ( var eventTypeMapping in eventTypeMappingList)
            {
                var mapping = eventTypeMapping.Split(':');
                _eventTypeMappingDict.Add(mapping[0], mapping[1]);
            }
        }

        public EventDetailsModel ToEventDetailsModel(EventDataModel responseModel)
        {
            var textDescription = string.Empty;
            var htmlDescription = string.Empty;
            if (!string.IsNullOrEmpty(responseModel.Description))
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(responseModel.Description);

                var pageContent = _textExtractor.ExtractEventContent(doc);
                textDescription = pageContent.TextContent;
                htmlDescription = pageContent.HtmlContent;
            }

            DateTimeOffset.TryParse(responseModel.Start, out DateTimeOffset startDate);

            var eventTopicsCustomFields = responseModel?.CustomFields?.FirstOrDefault(cf => cf.Name?.ToLower() == EVENT_TOPICS.ToLower());
            var eventFormattCustomField = responseModel?.CustomFields?.FirstOrDefault(cf => cf.Name?.ToLower() == EVENT_FORMAT.ToLower());
            var eventType = eventFormattCustomField?.Value?.FirstOrDefault();
            var eventTypeFacetValue = string.Empty;

            if (eventType != null)
            {
                _eventTypeMappingDict.TryGetValue(eventType, out eventTypeFacetValue);
            }

            var detailsModel = new EventDetailsModel
            {
                Id = responseModel?.Id,
                Title = responseModel?.Title,
                TextDescription = textDescription,
                HtmlDescription = htmlDescription,
                StartDate = startDate,
                Type = eventType,
                EventTypeFacet = eventTypeFacetValue,
                Tags = eventTopicsCustomFields?.Value,
                Url = $"{URL_RELATIVE_PATH}/{responseModel?.Id}",
                IsSupporterContent = false, // TODO: update once future JSON response field is set
            };
            return detailsModel;
        }
    }
}
