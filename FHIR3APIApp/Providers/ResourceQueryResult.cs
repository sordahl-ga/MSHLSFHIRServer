using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace FHIR3APIApp.Providers
{
    public class ResourceQueryResult
    {
        public ResourceQueryResult(IEnumerable<Hl7.Fhir.Model.Resource> r, long total, string token)
        {
            this.Resources = r;
            this.Total = total;
            this.ContinuationToken = token;
        }
        public IEnumerable<Hl7.Fhir.Model.Resource> Resources { get; set; }
        public long Total { get; set; }
        public string ContinuationToken { get; set; }
    }
}