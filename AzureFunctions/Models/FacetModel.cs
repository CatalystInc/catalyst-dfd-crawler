using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureFunctions.Models
{
    public class FacetModel
    {
        public bool Indexable { get; set; }
        public string FacetName { get; set; }
        public List<string> FacetValues { get; set; }
    }
}
