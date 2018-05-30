using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;

namespace FHIR3APIApp.Security
{
    public class RequireHttps : ActionFilterAttribute
    {
        public override void OnActionExecuting(HttpActionContext actionContext)
        {
            #if !DEBUG
            if (actionContext.Request.RequestUri.Scheme != Uri.UriSchemeHttps)
            {
                HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.Forbidden);
                response.Content = new StringContent("You must access via HTTPS", Encoding.UTF8, "text/html");
                actionContext.Response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            }
            #endif    
        }
    }
}