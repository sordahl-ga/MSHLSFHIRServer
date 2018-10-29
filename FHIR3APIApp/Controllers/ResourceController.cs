/* 
* 2017 Microsoft Corp
* 
* THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS “AS IS”
* AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO,
* THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
* ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE
* FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
* (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
* LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
* HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
* OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
* OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Model;
using System.Text;
using Microsoft.Azure;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http.Cors;
using FHIR3APIApp.Models;
using FHIR3APIApp.Providers;
using FHIR3APIApp.Utils;
using FHIR3APIApp.Security;
namespace FHIR3APIApp.Controllers
{
    [EnableCors(origins: "*", headers: "*", methods: "*")]
    [FHIRAuthorize]
    [RequireHttps]
    [RoutePrefix("")]
    public class ResourceController : ApiController
    {

        private IFHIRStore storage;
        private SecretResolver _secresolve;
        private static string FHIRCONTENTTYPEJSON = "application/fhir+json;charset=utf-8";
        private static string FHIRCONTENTTYPEXML = "application/fhir+xml;charset=utf-8";
        private string parsemode = null;
        private bool _strict = false;
        private FhirJsonParser jsonparser = null;
        private FhirXmlParser xmlparser = null;
        private ParserSettings parsersettings = null;
        public ResourceController()
        {

        }
        //TODO: Inject Storage Implementation
        public ResourceController(IFHIRStore store) {
            _secresolve = new SecretResolver();
            parsemode = _secresolve.GetConfiguration("FHIRParserMode","open");
            _strict = (parsemode == null || parsemode.Equals("strict", StringComparison.CurrentCultureIgnoreCase) ? true : false);
            this.storage = store;
            parsersettings = new ParserSettings();
            parsersettings.AcceptUnknownMembers = !_strict;
            parsersettings.AllowUnrecognizedEnums = !_strict;
            jsonparser = new FhirJsonParser(parsersettings);
            xmlparser = new FhirXmlParser(parsersettings);
        }
        private async Task<ResourceResponse> ProcessSingleResource(Resource p,string resourceType,string matchversionid=null)
        {
            
            //Version conflict detection
            if (!String.IsNullOrEmpty(matchversionid))
            {
                var cv = await storage.LoadFHIRResource(p.Id, resourceType);
                if (cv == null || !matchversionid.Equals(cv.Meta.VersionId))
                {
                    OperationOutcome oo = new OperationOutcome();
                    oo.Issue = new System.Collections.Generic.List<OperationOutcome.IssueComponent>();
                    OperationOutcome.IssueComponent ic = new OperationOutcome.IssueComponent();
                    ic.Severity = OperationOutcome.IssueSeverity.Error;
                    ic.Code = OperationOutcome.IssueType.Exception;
                    ic.Diagnostics = "Version conflict current resource version of " + resourceType + "/" + p.Id + " is " + cv.Meta.VersionId;
                    oo.Issue.Add(ic);
                    return new ResourceResponse(oo, -1);
                }
            }
            //Prepare for Insert/update and Version
            if (String.IsNullOrEmpty(p.Id)) p.Id = Guid.NewGuid().ToString();
            p.Meta = new Meta();
            p.Meta.VersionId = Guid.NewGuid().ToString();
            p.Meta.LastUpdated = DateTimeOffset.UtcNow;
            var rslt = await storage.UpsertFHIRResource(p);
            return new ResourceResponse(p, rslt);
        }
        private async Task<HttpResponseMessage> Upsert(string resourceType,string headerid=null)
        {
            try
            {

                string raw = await Request.Content.ReadAsStringAsync();
                BaseFhirParser parser = null;
                if (IsContentTypeJSON) parser = jsonparser;
                else if (IsContentTypeXML) parser = xmlparser;
                else throw new Exception("Invalid Content-Type must be application/fhir+json or application/fhir+xml");
                var reader = IsContentTypeJSON ? FhirJsonParser.CreateFhirReader(raw) : FhirXmlParser.CreateFhirReader(raw, false);
                var p = (Resource)parser.Parse(reader, FhirHelper.ResourceTypeFromString(resourceType));
                if (p.ResourceType != FhirHelper.GetResourceType(FhirHelper.ValidateResourceType(resourceType)))
                {
                    OperationOutcome oo = new OperationOutcome();
                    oo.Issue = new System.Collections.Generic.List<OperationOutcome.IssueComponent>();
                    OperationOutcome.IssueComponent ic = new OperationOutcome.IssueComponent();
                    ic.Severity = OperationOutcome.IssueSeverity.Error;
                    ic.Code = OperationOutcome.IssueType.Exception;
                    ic.Diagnostics = "Resource provided is not of type " + resourceType;
                    oo.Issue.Add(ic);
                    var respconf = this.Request.CreateResponse(HttpStatusCode.BadRequest);
                    respconf.Content = new StringContent(SerializeResponse(oo), Encoding.UTF8);
                    respconf.Content.Headers.LastModified = DateTimeOffset.Now;
                    respconf.Headers.TryAddWithoutValidation("Accept", CurrentAcceptType);
                    respconf.Content.Headers.TryAddWithoutValidation("Content-Type", IsAccceptTypeJSON ? FHIRCONTENTTYPEJSON : FHIRCONTENTTYPEXML);
                    return respconf;
                }
                if (String.IsNullOrEmpty(p.Id) && headerid != null) p.Id = headerid;
                //Store resource regardless of type
                var dbresp = await ProcessSingleResource(p, resourceType, IsMatchVersionId);
                p = dbresp.Resource;
                var response = this.Request.CreateResponse(dbresp.Response==1 ? HttpStatusCode.Created : HttpStatusCode.OK);
                response.Content = new StringContent("", Encoding.UTF8);
                response.Content.Headers.LastModified = p.Meta.LastUpdated;
                response.Headers.Add("Location", Request.RequestUri.AbsoluteUri + (headerid==null ? "/" + p.Id :""));
                response.Headers.Add("ETag", "W/\"" + p.Meta.VersionId + "\"");
                
                //Extract and Save each Resource in bundle if it's a batch type
                if (p.ResourceType==ResourceType.Bundle && (((Bundle)p).Type==Bundle.BundleType.Batch || ((Bundle)p).Type == Bundle.BundleType.Message))
                {
                    Bundle source = (Bundle)p;
                    /*Bundle results = new Bundle();
                    results.Id = Guid.NewGuid().ToString();
                    results.Type = Bundle.BundleType.Searchset;
                    results.Total = source.Entry.Count();
                    results.Link = new System.Collections.Generic.List<Bundle.LinkComponent>();
                    results.Link.Add(new Bundle.LinkComponent() { Url = Request.RequestUri.AbsoluteUri, Relation = "original" });
                    results.Entry = new System.Collections.Generic.List<Bundle.EntryComponent>();*/
                    foreach (Bundle.EntryComponent ec in source.Entry)
                    {
                        var rslt = await ProcessSingleResource(ec.Resource, Enum.GetName(typeof(Hl7.Fhir.Model.ResourceType), ec.Resource.ResourceType));
                        //results.Entry.Add(new Bundle.EntryComponent() { Resource = rslt.Resource, FullUrl = FhirHelper.GetFullURL(Request, rslt.Resource) });
                    }
                }
                return response;
            }
            catch (Exception e)
            {
                OperationOutcome oo = new OperationOutcome();
                oo.Issue = new System.Collections.Generic.List<OperationOutcome.IssueComponent>();
                OperationOutcome.IssueComponent ic = new OperationOutcome.IssueComponent();
                ic.Severity = OperationOutcome.IssueSeverity.Error;
                ic.Code = OperationOutcome.IssueType.Exception;
                ic.Diagnostics = e.Message;
                oo.Issue.Add(ic);
                var response = this.Request.CreateResponse(HttpStatusCode.BadRequest);
                response.Headers.TryAddWithoutValidation("Accept", CurrentAcceptType);
                response.Content = new StringContent(SerializeResponse(oo), Encoding.UTF8);
                response.Content.Headers.TryAddWithoutValidation("Content-Type", IsAccceptTypeJSON ? FHIRCONTENTTYPEJSON : FHIRCONTENTTYPEXML);
                response.Content.Headers.LastModified = DateTimeOffset.Now;
                return response;
            }
        }
        [HttpPost]
        [Route("{resource}")]
        public async Task<HttpResponseMessage> Post(string resource)
        {
            return await Upsert(resource);
        }
        [HttpPut]
        [Route("{resource}")]
        public async Task<HttpResponseMessage> Put(string resource)
        {
            return await Upsert(resource);
        }
        [HttpGet]
        [Route("{resource}")]
        public async Task<HttpResponseMessage> Get(string resource)
        {
            try
            {
                string respval = null;

                if (Request.RequestUri.AbsolutePath.ToLower().EndsWith("metadata"))
                {
                    respval = SerializeResponse(FhirHelper.GenerateCapabilityStatement(Request.RequestUri.AbsoluteUri));
                }
                else
                {
                    string validResource = FhirHelper.ValidateResourceType(resource);
                    NameValueCollection nvc = HttpUtility.ParseQueryString(Request.RequestUri.Query);
                    string _id = nvc["_id"];
                    string _nextpage = nvc["_nextpage"];
                    string _count = nvc["_count"];
                    if (_count == null) _count = "100";
                    string _querytotal = nvc["_querytotal"];
                    if (_querytotal == null) _querytotal = "-1";
                    IEnumerable<Resource> retVal = null;
                    ResourceQueryResult searchrslt = null;
                    int iqueryTotal = 0;
                    if (string.IsNullOrEmpty(_id))
                    {
                        string query = FhirParmMapper.Instance.GenerateQuery(storage, validResource, nvc);
                        searchrslt = await storage.QueryFHIRResource(query, validResource, int.Parse(_count), _nextpage, long.Parse(_querytotal));
                        retVal = searchrslt.Resources;
                        iqueryTotal = (int)searchrslt.Total;

                    }
                    else
                    {
                        retVal = new List<Resource>();
                        var r = await storage.LoadFHIRResource(_id, validResource);
                        if (r != null) ((List<Resource>)retVal).Add(r);
                        iqueryTotal = retVal.Count();
                    }
                    var baseurl = Request.RequestUri.Scheme + "://" + Request.RequestUri.Authority + "/" + validResource;
                    Bundle results = new Bundle();
                    results.Id = Guid.NewGuid().ToString();
                    results.Type = Bundle.BundleType.Searchset;
                    results.Total = iqueryTotal;
                    results.Link = new System.Collections.Generic.List<Bundle.LinkComponent>();
                    NameValueCollection qscoll = Request.RequestUri.ParseQueryString();
                    qscoll.Remove("_count");
                    qscoll.Remove("_querytotal");
                    qscoll.Add("_querytotal", searchrslt.Total.ToString());
                    qscoll.Add("_count", _count);

                    results.Link.Add(new Bundle.LinkComponent() { Url = baseurl + "?" + qscoll.ToString(), Relation = "self" });

                    if (searchrslt.ContinuationToken != null)
                    {
                        qscoll.Remove("_nextpage");
                        qscoll.Add("_nextpage", searchrslt.ContinuationToken);
                        results.Link.Add(new Bundle.LinkComponent() { Url = baseurl + "?" + qscoll.ToString(), Relation = "next" });
                    }

                    results.Entry = new System.Collections.Generic.List<Bundle.EntryComponent>();
                    Bundle.SearchComponent match = new Bundle.SearchComponent();
                    match.Mode = Bundle.SearchEntryMode.Match;
                    Bundle.SearchComponent include = new Bundle.SearchComponent();
                    include.Mode = Bundle.SearchEntryMode.Include;
                    foreach (Resource p in retVal)
                    {


                        results.Entry.Add(new Bundle.EntryComponent() { Resource = p, FullUrl = FhirHelper.GetFullURL(Request, p), Search = match });
                        var includes = await FhirHelper.ProcessIncludes(p, nvc, storage);
                        foreach (Resource r in includes)
                        {
                            results.Entry.Add(new Bundle.EntryComponent() { Resource = r, FullUrl = FhirHelper.GetFullURL(Request, r), Search = include });
                        }
                    }

                    if (retVal != null)
                    {
                        respval = SerializeResponse(results);
                    }
                }
                var response = this.Request.CreateResponse(HttpStatusCode.OK);
                response.Headers.TryAddWithoutValidation("Accept", CurrentAcceptType);

                response.Content = new StringContent(respval, Encoding.UTF8);
                response.Content.Headers.TryAddWithoutValidation("Content-Type", IsAccceptTypeJSON ? FHIRCONTENTTYPEJSON : FHIRCONTENTTYPEXML);
                return response;
            } catch (Exception e)
            { 
                OperationOutcome oo = new OperationOutcome();
                oo.Issue = new System.Collections.Generic.List<OperationOutcome.IssueComponent>();
                OperationOutcome.IssueComponent ic = new OperationOutcome.IssueComponent();
                ic.Severity = OperationOutcome.IssueSeverity.Error;
                ic.Code = OperationOutcome.IssueType.Exception;
                ic.Diagnostics = e.Message;
                oo.Issue.Add(ic);
                var response = this.Request.CreateResponse(HttpStatusCode.BadRequest);
                response.Headers.TryAddWithoutValidation("Accept", CurrentAcceptType);
                response.Content = new StringContent(SerializeResponse(oo), Encoding.UTF8);
                response.Content.Headers.TryAddWithoutValidation("Content-Type", IsAccceptTypeJSON ? FHIRCONTENTTYPEJSON : FHIRCONTENTTYPEXML);
                response.Content.Headers.LastModified = DateTimeOffset.Now;
                return response;
            }
        
        }
        [HttpDelete]
        [Route("{resource}/{id}")]
        public async Task<HttpResponseMessage> Delete(string resource, string id)
        {
            try
            {
                HttpResponseMessage response = null;
                string respval = "";
                string validResource = FhirHelper.ValidateResourceType(resource);
                var retVal = await storage.LoadFHIRResource(id, validResource);
                if (retVal != null)
                {
                    var del = await storage.DeleteFHIRResource(retVal);
                    response = this.Request.CreateResponse(HttpStatusCode.NoContent);
                    response.Headers.TryAddWithoutValidation("Accept", CurrentAcceptType);

                    response.Content = new StringContent(respval, Encoding.UTF8);
                    response.Headers.Add("ETag", "W/\"" + retVal.Meta.VersionId + "\"");

                }
                else
                {
                    response = this.Request.CreateResponse(HttpStatusCode.NotFound);
                    response.Content = new StringContent("", Encoding.UTF8);
                }
                response.Content.Headers.TryAddWithoutValidation("Content-Type", IsAccceptTypeJSON ? FHIRCONTENTTYPEJSON : FHIRCONTENTTYPEXML);
                return response;
            } catch (Exception e)
            { 
                OperationOutcome oo = new OperationOutcome();
                oo.Issue = new System.Collections.Generic.List<OperationOutcome.IssueComponent>();
                OperationOutcome.IssueComponent ic = new OperationOutcome.IssueComponent();
                ic.Severity = OperationOutcome.IssueSeverity.Error;
                ic.Code = OperationOutcome.IssueType.Exception;
                ic.Diagnostics = e.Message;
                oo.Issue.Add(ic);
                var response = this.Request.CreateResponse(HttpStatusCode.BadRequest);
                response.Headers.TryAddWithoutValidation("Accept", CurrentAcceptType);
                response.Content = new StringContent(SerializeResponse(oo), Encoding.UTF8);
                response.Content.Headers.TryAddWithoutValidation("Content-Type", IsAccceptTypeJSON? FHIRCONTENTTYPEJSON : FHIRCONTENTTYPEXML);
                response.Content.Headers.LastModified = DateTimeOffset.Now;
                return response;
            }
}
        [HttpPut]
        [Route("{resource}/{id}")]
        public async Task<HttpResponseMessage> PutWithId(string resource, string id)
        {
            return await Upsert(resource,id);
        }
        [HttpPost]
        [Route("{resource}/{id}")]
        public async Task<HttpResponseMessage> PostWIthId(string resource, string id)
        {
            return await Upsert(resource,id);
        }
        [HttpGet]
        [Route("{resource}/{id}")]
        public async Task<HttpResponseMessage> Get(string resource, string id)
        {
            try
            {
                if (Request.Method == HttpMethod.Post)
                {
                    return await Upsert(resource);
                }
                if (Request.Method == HttpMethod.Put)
                {
                    return await Upsert(resource);
                }

                HttpResponseMessage response = null;
                string respval = "";
                string validResource = FhirHelper.ValidateResourceType(resource);
                var retVal = await storage.LoadFHIRResource(id, validResource);
                if (retVal != null)
                {
                    respval = SerializeResponse(retVal);
                    response = this.Request.CreateResponse(HttpStatusCode.OK);
                    response.Headers.TryAddWithoutValidation("Accept", CurrentAcceptType);
                    response.Content = new StringContent(respval, Encoding.UTF8);
                    response.Content.Headers.LastModified = retVal.Meta.LastUpdated;
                    response.Headers.Add("ETag", "W/\"" + retVal.Meta.VersionId + "\"");

                }
                else
                {
                    response = this.Request.CreateResponse(HttpStatusCode.NotFound);
                    response.Content = new StringContent("", Encoding.UTF8);
                }
                response.Content.Headers.TryAddWithoutValidation("Content-Type", IsAccceptTypeJSON ? FHIRCONTENTTYPEJSON : FHIRCONTENTTYPEXML);
                return response;
            } catch (Exception e)
            {
                OperationOutcome oo = new OperationOutcome();
                oo.Issue = new System.Collections.Generic.List<OperationOutcome.IssueComponent>();
                OperationOutcome.IssueComponent ic = new OperationOutcome.IssueComponent();
                ic.Severity = OperationOutcome.IssueSeverity.Error;
                ic.Code = OperationOutcome.IssueType.Exception;
                ic.Diagnostics = e.Message;
                oo.Issue.Add(ic);
                var response = this.Request.CreateResponse(HttpStatusCode.BadRequest);
                response.Headers.TryAddWithoutValidation("Accept", CurrentAcceptType);
                response.Content = new StringContent(SerializeResponse(oo), Encoding.UTF8);
                response.Content.Headers.TryAddWithoutValidation("Content-Type", IsAccceptTypeJSON? FHIRCONTENTTYPEJSON : FHIRCONTENTTYPEXML);
                response.Content.Headers.LastModified = DateTimeOffset.Now;
                return response;
            }
        }   
        // GET: Historical Speciic Version
        [HttpGet]
        [Route("{resource}/{id}/_history/{vid}")]
        public HttpResponseMessage GetHistory(string resource, string id, string vid)
        {
            try
            {
                HttpResponseMessage response = null;
                string respval = "";
                string validResource = FhirHelper.ValidateResourceType(resource);
                string item = storage.HistoryStore.GetResourceHistoryItem(validResource, id, vid);
                if (item != null)
                {
                    Resource retVal = (Resource)jsonparser.Parse(item, FhirHelper.ResourceTypeFromString(validResource));
                    if (retVal != null) respval = SerializeResponse(retVal);
                    response = this.Request.CreateResponse(HttpStatusCode.OK);
                    response.Headers.TryAddWithoutValidation("Accept", CurrentAcceptType);
                    response.Headers.Add("ETag", "W/\"" + retVal.Meta.VersionId + "\"");
                    response.Content = new StringContent(respval, Encoding.UTF8);
                    response.Content.Headers.LastModified = retVal.Meta.LastUpdated;
                }
                else
                {
                    response = this.Request.CreateResponse(HttpStatusCode.NotFound);
                    response.Content = new StringContent("", Encoding.UTF8);
                }
                response.Content.Headers.TryAddWithoutValidation("Content-Type", IsAccceptTypeJSON ? FHIRCONTENTTYPEJSON : FHIRCONTENTTYPEXML);
                return response;
            } catch (Exception e)
            { 
                OperationOutcome oo = new OperationOutcome();
                oo.Issue = new System.Collections.Generic.List<OperationOutcome.IssueComponent>();
                OperationOutcome.IssueComponent ic = new OperationOutcome.IssueComponent();
                ic.Severity = OperationOutcome.IssueSeverity.Error;
                ic.Code = OperationOutcome.IssueType.Exception;
                ic.Diagnostics = e.Message;
                oo.Issue.Add(ic);
                var response = this.Request.CreateResponse(HttpStatusCode.BadRequest);
                response.Headers.TryAddWithoutValidation("Accept", CurrentAcceptType);
                response.Content = new StringContent(SerializeResponse(oo), Encoding.UTF8);
                response.Content.Headers.TryAddWithoutValidation("Content-Type", IsAccceptTypeJSON? FHIRCONTENTTYPEJSON : FHIRCONTENTTYPEXML);
                response.Content.Headers.LastModified = DateTimeOffset.Now;
                return response;
            }
        }
        // GET: Historical Speciic Version
        [HttpGet]
        [Route("{resource}/{id}/_history")]
        public HttpResponseMessage GetHistoryComplete(string resource, string id)
        {
            try
            { 
                string respval = "";
                string validResource = FhirHelper.ValidateResourceType(resource);
                IEnumerable<string> history= storage.HistoryStore.GetResourceHistory(validResource, id);
                //Create Return Bundle
                Bundle results = new Bundle();
                results.Id = Guid.NewGuid().ToString();
                results.Type = Bundle.BundleType.History;
                results.Total = history.Count();
                results.Link = new System.Collections.Generic.List<Bundle.LinkComponent>();
                results.Link.Add(new Bundle.LinkComponent() { Url = Request.RequestUri.GetLeftPart(UriPartial.Authority), Relation = "self" });
                results.Entry = new System.Collections.Generic.List<Bundle.EntryComponent>();
                //Add History Items to Bundle
                foreach (string h in history)
                {
                    //todo
                    var r = (Resource)jsonparser.Parse(h, FhirHelper.ResourceTypeFromString(validResource));
                    results.Entry.Add(new Bundle.EntryComponent() { Resource = r, FullUrl = FhirHelper.GetFullURL(Request, r) });
               
                }
           
            
                //Serialize and Return Bundle
                respval = SerializeResponse(results);
                var response = this.Request.CreateResponse(HttpStatusCode.OK);
                response.Headers.TryAddWithoutValidation("Accept", CurrentAcceptType);
            
                response.Content = new StringContent(respval, Encoding.UTF8);
                response.Content.Headers.TryAddWithoutValidation("Content-Type", IsAccceptTypeJSON ? FHIRCONTENTTYPEJSON : FHIRCONTENTTYPEXML);
                return response;
            }
            catch (Exception e)
            {
                OperationOutcome oo = new OperationOutcome();
                oo.Issue = new System.Collections.Generic.List<OperationOutcome.IssueComponent>();
                OperationOutcome.IssueComponent ic = new OperationOutcome.IssueComponent();
                ic.Severity = OperationOutcome.IssueSeverity.Error;
                ic.Code = OperationOutcome.IssueType.Exception;
                ic.Diagnostics = e.Message;
                oo.Issue.Add(ic);
                var response = this.Request.CreateResponse(HttpStatusCode.BadRequest);
                response.Headers.TryAddWithoutValidation("Accept", CurrentAcceptType);
                response.Content = new StringContent(SerializeResponse(oo), Encoding.UTF8);
                response.Content.Headers.TryAddWithoutValidation("Content-Type", IsAccceptTypeJSON ? FHIRCONTENTTYPEJSON : FHIRCONTENTTYPEXML);
                response.Content.Headers.LastModified = DateTimeOffset.Now;
                return response;
            }
        }
        private string CurrentAcceptType {
            get {
                string at = System.Web.HttpContext.Current.Request.QueryString["_format"];
                if (String.IsNullOrEmpty(at)) at = System.Web.HttpContext.Current.Request.AcceptTypes.First();
                if (!String.IsNullOrEmpty(at)) {
                    if (at.Equals("text/html") || at.Equals("xml")) at = "application/xml";
                    if (at.Equals("*/*") || at.Equals("json")) at = "application/json"; 
                } else
                {
                    at = "application/json";
                }
                return at;
           }
        }
        private string IsMatchVersionId { get { return System.Web.HttpContext.Current.Request.Headers["If-Match"]; } }
        private bool IsContentTypeJSON {
            get {
                return System.Web.HttpContext.Current.Request.ContentType.ToLower().Contains("json");
            }
        }

        private bool IsContentTypeXML { get { return System.Web.HttpContext.Current.Request.ContentType.ToLower().Contains("xml"); } }
        private bool IsAccceptTypeJSON { get { return CurrentAcceptType.ToLower().Contains("json"); } }
        private string SerializeResponse(Resource retVal)
        {
            
           
            if (CurrentAcceptType.ToLower().Contains("json"))
                return FhirSerializer.SerializeToJson(retVal);
            else if (CurrentAcceptType.ToLower().Contains("xml"))
                return FhirSerializer.SerializeResourceToXml(retVal);
            else
                throw new System.Web.HttpException((int)HttpStatusCode.NotAcceptable, "Accept Type not Supported must be */xml or */json");
        }

        protected string GetBaseURL()
        {
            return Request.RequestUri.Scheme + "://" + Request.RequestUri.Host + ((Request.RequestUri.Port != 80 || Request.RequestUri.Port != 443) ? ":" + Request.RequestUri.Port.ToString() : "");
        }
      

    }
}
