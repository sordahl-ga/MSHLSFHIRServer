using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace FHIR3APIApp.Models
{
    public class ResourceResponse
    {
        public ResourceResponse()
        {
            this.Response = -1;
        }
        public ResourceResponse(Hl7.Fhir.Model.Resource resource,int resp)
        {
            this.Resource = resource;
            this.Response = resp;
        }
        public Hl7.Fhir.Model.Resource Resource { get; set; }
        //-1 == Error, 0==Updated, 1==Created
        public int Response { get; set; }
    }
}