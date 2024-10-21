using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureFunctions.Cvent.Models
{
    public class EventsResponseModel
    {
        [JsonProperty("data")]
        public List<EventDataModel>? Data { get; set; }
        [JsonProperty("paging")]
        public PagingDataModel? Paging { get; set; }
        [JsonProperty("message")]
        public string? Message { get; set; }
    }

    public class EventDataModel
    {
        [JsonProperty("_links")]
        public EventLinkGroupDataMopdel? Links { get; set; }
        [JsonProperty("archiveAfter")]
        public string? ArchiveAfter { get; set; }
        [JsonProperty("category")]
        public CategoryDataModel? Category { get; set; }
        [JsonProperty("closeAfter")]
        public string? CloseAfter { get; set; }
        [JsonProperty("code")]
        public string? Code { get; set; }
        [JsonProperty("created")]
        public string? Created { get; set; }        
        [JsonProperty("createdBy")]
        public string? CreatedBy { get; set; }
        [JsonProperty("currency")]
        public string? Currency { get; set; }
        [JsonProperty("customFields")]
        public List<CustomFieldsDataModel>? CustomFields { get; set; }
        [JsonProperty("defaultLocale")]
        public string? DefaultLocale { get; set; }
        [JsonProperty("description")]
        public string? Description { get; set; }
        [JsonProperty("end")]
        public string? End { get; set; }
        [JsonProperty("eventStatus")]
        public string? EventStatus { get; set; }
        [JsonProperty("format")]
        public string? Format { get; set; }
        [JsonProperty("id")]
        public string? Id { get; set; }
        [JsonProperty("languages")]
        public List<string>? Languages { get; set; }
        [JsonProperty("lastModified")]
        public string? LastModified { get; set; }
        [JsonProperty("lastModifiedBy")]
        public string? LastModifiedBy { get; set; }
        [JsonProperty("launchAfter")]
        public string? LaunchAfter { get; set; }
        [JsonProperty("planners")]
        public List<PlannerDataModel>? Planners { get; set; }
        [JsonProperty("registrationSecurityLevel")]
        public string? RegistrationSecurityLevel { get; set; }
        [JsonProperty("start")]
        public string? Start { get; set; }
        [JsonProperty("status")]
        public string? Status { get; set; }
        [JsonProperty("testMode")]
        public bool TestMode { get; set; }
        [JsonProperty("timezone")]
        public string? Timezone { get; set; }
        [JsonProperty("title")]
        public string? Title { get; set; }
        [JsonProperty("type")]
        public string? Type { get; set; }
        [JsonProperty("virtual")]
        public string? Virtual { get; set; }

    }

    public class CustomFieldsDataModel
    {
        [JsonProperty("id")]
        public string? Id { get; set; }
        [JsonProperty("name")]
        public string? Name { get; set; }
        [JsonProperty("order")]
        public int Number { get; set; }
        [JsonProperty("type")]
        public string? Type { get; set; }
        [JsonProperty("value")]
        public List<string>? Value { get; set; }
    }

    public class PlannerDataModel
    {
        [JsonProperty("deleted")]
        public bool Deleted { get; set; }
        [JsonProperty("email")]
        public string? Email { get; set; }
        [JsonProperty("firstName")]
        public string? FirstName { get; set; }
        [JsonProperty("lastName")]
        public string? LastName { get; set; }
    }

    public class EventLinkGroupDataMopdel
    {
        [JsonProperty("agenda")]
        public LinkDataModel? Agenda { get; set; }
        [JsonProperty("invitation")]
        public LinkDataModel? Invitation { get; set; }
        [JsonProperty("registration")]
        public LinkDataModel? Registration { get; set; }
        [JsonProperty("summary")]
        public LinkDataModel? Summary { get; set; }
    }

    public class PagingDataModel
    {
        [JsonProperty("_links")]
        public PagingLinkGroupDataModel? Links { get; set; }
        [JsonProperty("currentToken")]
        public string? CurrentToken { get; set; }
        [JsonProperty("limit")]
        public int Limit { get; set; }
        [JsonProperty("totalCount")]
        public int TotalCount { get; set; }
    }

    public class PagingLinkGroupDataModel
    {
        [JsonProperty("self")]
        public LinkDataModel? Self { get; set; }
    }

    public class LinkDataModel
    {
        [JsonProperty("href")]
        public string? Href { get; set; }
    }

    public class CategoryDataModel
    {
        public string? Name { get; set; }
    }
}
