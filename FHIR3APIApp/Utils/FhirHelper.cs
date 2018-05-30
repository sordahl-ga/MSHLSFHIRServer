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
using System.Configuration;
using System.Linq;
using System.Web;
using System.Threading.Tasks;
using System.Net.Http;
using System.IO;
using Hl7.Fhir.Model;
using System.Collections.Specialized;
using Newtonsoft.Json.Linq;
using Hl7.Fhir.Serialization;
using FHIR3APIApp.Providers;

namespace FHIR3APIApp.Utils
{
    public static class FhirHelper
    {
       
        public static string URLBase64Encode(string plainText)
        {
            if (plainText == null) return null;
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return HttpServerUtility.UrlTokenEncode(plainTextBytes);
        }

        public static string URLBase64Decode(string base64EncodedData)
        {
            if (base64EncodedData == null) return null;
            return System.Text.Encoding.UTF8.GetString(HttpServerUtility.UrlTokenDecode(base64EncodedData));
        }

        public static CapabilityStatement GenerateCapabilityStatement(String url)
        {
            CapabilityStatement cs = new CapabilityStatement();
            cs.Name = "Azure HLS Team API Application FHIR Server";
            cs.Status = PublicationStatus.Draft;

            cs.Experimental = true;
            cs.Publisher = "Microsoft Corporation";
            cs.FhirVersion = "3.0.1";
            cs.AcceptUnknown = CapabilityStatement.UnknownContentCode.Both;
            cs.Format = new string[] { "json", "xml" };
            cs.Contact = new List<ContactDetail>();
            ContactDetail cc = new ContactDetail();
            cc.Name = "Steve Ordahl";
            cc.Telecom = new List<ContactPoint>();
            cc.Telecom.Add(new ContactPoint(ContactPoint.ContactPointSystem.Email, ContactPoint.ContactPointUse.Work, "stordahl@microsoft.com"));
            cs.Contact.Add(cc);
            cs.Kind = CapabilityStatement.CapabilityStatementKind.Instance;
            cs.Date = "2018-03-08";
            cs.Description = new Markdown("This is the FHIR capability statement for the HLS Team API Application FHIR Server 3.0.1");
            cs.Software = new CapabilityStatement.SoftwareComponent();
            cs.Software.Name = "Experimental Microsoft HLS Team FHIR Server API App";
            cs.Software.Version = "0.9.1";
            cs.Software.ReleaseDate = "2018-03-08";
            cs.Implementation = new CapabilityStatement.ImplementationComponent();
            cs.Implementation.Description = "MSHLS Experimental FHIR Server";
            int endpos = url.ToLower().LastIndexOf("/metadata");
            if (endpos > -1) url = url.Substring(0, endpos);
            cs.Implementation.Url = url;

            CapabilityStatement.RestComponent rc = new CapabilityStatement.RestComponent();

            rc.Mode = CapabilityStatement.RestfulCapabilityMode.Server;
            //security profile
            rc.Security = new CapabilityStatement.SecurityComponent();
            rc.Security.Service = new List<CodeableConcept>();
            rc.Security.Service.Add(new CodeableConcept("http://hl7.org/fhir/restful-security-service", "SMART-on-FHIR", "OAuth2 using SMART-on-FHIR profile (see http://docs.smarthealthit.org)"));
            rc.Security.Extension = new List<Extension>();
            Extension oauthex = new Extension();
            oauthex.Extension = new List<Extension>();
            oauthex.Url = "http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris";
            oauthex.Extension.Add(new Extension("token", new FhirUri("https://login.microsoftonline.com/microsoft.onmicrosoft.com/oauth2/token")));
            oauthex.Extension.Add(new Extension("authorize", new FhirUri("https://login.microsoftonline.com/microsoft.onmicrosoft.com/oauth2/authorize")));
            rc.Security.Extension.Add(oauthex);
            rc.Security.Cors = true;
            //All controller resources 
            var supported = System.Enum.GetValues(typeof(ResourceType));
            foreach (ResourceType k in supported)
            {
                CapabilityStatement.ResourceComponent rescomp = new CapabilityStatement.ResourceComponent();
                rescomp.Type = k;
                rescomp.Versioning = CapabilityStatement.ResourceVersionPolicy.Versioned;
                rescomp.Interaction = new List<CapabilityStatement.ResourceInteractionComponent>();
                rescomp.Interaction.Add(new CapabilityStatement.ResourceInteractionComponent() { Code = CapabilityStatement.TypeRestfulInteraction.Create });
                rescomp.Interaction.Add(new CapabilityStatement.ResourceInteractionComponent() { Code = CapabilityStatement.TypeRestfulInteraction.Update });
                rescomp.Interaction.Add(new CapabilityStatement.ResourceInteractionComponent() { Code = CapabilityStatement.TypeRestfulInteraction.Delete });
                rescomp.Interaction.Add(new CapabilityStatement.ResourceInteractionComponent() { Code = CapabilityStatement.TypeRestfulInteraction.Read });
                rescomp.Interaction.Add(new CapabilityStatement.ResourceInteractionComponent() { Code = CapabilityStatement.TypeRestfulInteraction.Vread });
                rc.Resource.Add(rescomp);
            }
            cs.Rest.Add(rc);
            return cs;
        }
    
        public static Type ResourceTypeFromString(string resourceType)
        {
            return Type.GetType("Hl7.Fhir.Model." + ValidateResourceType(resourceType) + ",Hl7.Fhir.STU3.Core");
        }
        public static string ValidateResourceType(string resourceType)
        {
            ResourceType t = GetResourceType(resourceType);
            var rts = GetResourceTypeString(t);
            if (rts.Equals(resourceType, StringComparison.CurrentCultureIgnoreCase)) {
                return rts;
            }
            throw new InvalidResourceException("Resource Type: " + resourceType + " is not valid!");
        }
        public static ResourceType GetResourceType(string resourceType)
        {
            ResourceType rt;
            Enum.TryParse(resourceType, true, out rt);
            return rt;
        }
        public static string GetResourceTypeString(ResourceType rt)
        {
            return Enum.GetName(typeof(Hl7.Fhir.Model.ResourceType),rt);
        }
        public static string GetResourceTypeString(Resource r)
        {
            return GetResourceTypeString(r.ResourceType);
        }
        public static string GetFullURL(HttpRequestMessage request, Resource r)
        {
            try
            {
                Uri baseUri = new Uri(request.RequestUri.AbsoluteUri.Replace(request.RequestUri.PathAndQuery, String.Empty));
                Uri resourceFullPath = new Uri(baseUri, VirtualPathUtility.ToAbsolute("~/" + GetResourceTypeString(r) + "/" + r.Id));
                return resourceFullPath.ToString();
            }
            catch (Exception e1)
            {
                return null;
            }
        }
        public static Patient PatientName(string standardname, HumanName.NameUse? use, Patient pat)
        {
            string[] family = standardname.Split(',');
            string[] given = null;
            if (family.Length > 1)
            {
                given = family[1].Split(' ');

            }
            if (pat.Name == null) pat.Name = new List<HumanName>();
            pat.Name.Add(new HumanName() { Use = use, Family = family[0], Given = given });
            return pat;
        }
        public static string AppendWhereClause(string wc, string expression)
        {
            string retVal = wc;
            if (String.IsNullOrEmpty(wc)) retVal = " where " + expression;
            retVal = retVal + " AND " + expression;
            return retVal;

        }
        public static Identifier FindIdentifier(List<Identifier> ids, string system, string type)
        {
            Identifier retVal = null;
            foreach (Identifier i in ids)
            {
                if (i.System.Equals(system, StringComparison.CurrentCultureIgnoreCase) && i.Type.Coding[0].Code.Equals(type, StringComparison.CurrentCultureIgnoreCase))
                {
                    retVal = i;
                    break;
                }
            }
            return retVal;
        }

        public static Resource StripAttachment(Resource source)
        {
            foreach (var prop in source.GetType().GetProperties())
            {
                Console.WriteLine("{0}={1}", prop.Name, prop.GetValue(source, null));
            }
            return null;
        }
        public static async Task<string> Read(HttpRequestMessage req)
        {
            using (var contentStream = await req.Content.ReadAsStreamAsync())
            {
                contentStream.Seek(0, SeekOrigin.Begin);
                using (var sr = new StreamReader(contentStream))
                {
                    return sr.ReadToEnd();
                }
            }
        }
        public static async Task<List<Resource>> ProcessIncludes(Resource source, NameValueCollection parms, IFHIRStore store)
        {
            var retVal = new List<Resource>();
            string includeparm = parms["_include"];
            if (!string.IsNullOrEmpty(includeparm))
            {
                JObject j = JObject.Parse(FhirSerializer.SerializeToJson(source));
                string[] incs = includeparm.Split(',');
                foreach (string t in incs)
                {
                    bool isinstance = false;
                    string[] s = t.Split(':');
                    if (s.Length > 1)
                    {
                        var prop = s[1];
                        JToken x = null;
                        try
                        {
                            if (prop.Equals("substance"))
                            {
                                x = j["suspectEntity"];
                                isinstance = true;
                            }
                            else
                            {
                                x = j[prop];

                            }
                        }
                        catch (Exception)
                        {

                        }
                        if (x != null)
                        {
                                for (int i = 0; i < x.Count(); i++)
                                {
                                    var x1 = (x.Type == JTokenType.Array ? x[i] : x);
                                    string z = (isinstance ? x1["instance"]["reference"].ToString() : x1["reference"].ToString());
                                    string[] split = z.Split('/');
                                    if (split.Length > 1)
                                    {
                                        var a1 = await store.LoadFHIRResource(split[1], split[0]);
                                        if (a1 != null) retVal.Add(a1);
                                    }
                                }
                         
                        }
                    }


                }

            }
            return retVal;
        }
    }
}