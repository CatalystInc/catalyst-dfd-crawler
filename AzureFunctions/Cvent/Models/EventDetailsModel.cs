using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureFunctions.Cvent.Models
{
    public class EventDetailsModel
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? TextDescription { get; set; }
        public string? HtmlDescription { get; set; }
        public string? Type { get; set; }
        public string? EventTypeFacet { get; set; }
        public bool IsSupporterContent { get; set; }
        public DateTimeOffset StartDate { get; set; }
        public List<string>? Tags { get; set; }
        public string Url { get; set; }
    }
}
