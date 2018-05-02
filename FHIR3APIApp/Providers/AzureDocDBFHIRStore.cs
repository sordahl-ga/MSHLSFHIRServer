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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure;
using System.Net;
using Newtonsoft.Json.Linq;
using Hl7.Fhir.Serialization;
using FHIR3APIApp.Utils;
using Microsoft.Azure.Documents.Linq;

namespace FHIR3APIApp.Providers
{
    public class AzureDocDBFHIRStore : IFHIRStore
    {
        /// <summary>
        /// The maximum size of a FHIR Resource before attachment corelation or error
        /// </summary>
        private static int MAXDOCSIZEBYTES = 500000;
        /// <summary>
        /// The History table client instance.
        /// </summary>
        private IFHIRHistoryStore historystore;
        /// <summary>
        /// The DocumentDB client instance.
        /// </summary>
        private DocumentClient client;
        /// <summary>
        /// The Azure DocumentDB endpoint
        /// </summary>
        private static readonly string EndpointUri = CloudConfigurationManager.GetSetting("DBStorageEndPointUri");

        /// <summary>
        /// The primary key for the Azure DocumentDB account.
        /// </summary>
        private static readonly string PrimaryKey = CloudConfigurationManager.GetSetting("DBStoragePrimaryKey");
        /// <summary>
        /// The DBName for DocumentDB
        /// </summary>
        private static readonly string DBName = CloudConfigurationManager.GetSetting("FHIRDB");
        /// <summary>
        /// The Througput offer for the FHIRDB
        /// </summary>
        private static readonly string DBDTU = CloudConfigurationManager.GetSetting("FHIRDBTHROUHPUT");

        private bool databasecreated = false;
        private ConcurrentDictionary<string, string> collection = new ConcurrentDictionary<string, string>();
        private FhirJsonParser parser = null;
        
        public string SelectAllQuery
        {
            get
            {
                return "select value c from c";
            }
        }

        public IFHIRHistoryStore HistoryStore
        {
            get
            {
                return historystore;
            }
        }
        private Hl7.Fhir.Model.Resource ConvertDocument(Document doc)
        {
            var obj = (JObject)(dynamic)doc;
            obj.Remove("_rid");
            obj.Remove("_self");
            obj.Remove("_etag");
            obj.Remove("_attachments");
            obj.Remove("_ts");
            string rt = (String)obj["resourceType"];
            Hl7.Fhir.Model.Resource t = (Hl7.Fhir.Model.Resource)parser.Parse(obj.ToString(Newtonsoft.Json.Formatting.None), FhirHelper.ResourceTypeFromString(rt));
            return t;
        }
        public AzureDocDBFHIRStore(IFHIRHistoryStore history) 
        {
            this.client = new DocumentClient(new Uri(EndpointUri), PrimaryKey, new ConnectionPolicy
            {
                ConnectionMode = ConnectionMode.Direct,
                ConnectionProtocol = Protocol.Tcp
            });
            historystore = history;
            ParserSettings ps = new ParserSettings();
            ps.AcceptUnknownMembers = true;
            ps.AllowUnrecognizedEnums = true;
            
            parser = new FhirJsonParser(ps);
            


        }    
       
        private async Task<ResourceResponse<Database>> CreateDatabaseIfNotExists(string databaseName)
        {
            if (databasecreated) return null;
            var x = await this.client.CreateDatabaseIfNotExistsAsync(new Database { Id = databaseName });
            databasecreated = true;
            return x;
           
        }
        private async Task<IResourceResponse<DocumentCollection>> CreateDocumentCollectionIfNotExists(string databaseName, string collectionName)
        {
            if (collection.ContainsKey(collectionName)) return null;
            await CreateDatabaseIfNotExists(databaseName);
            DocumentCollection collectionDefinition = new DocumentCollection();
                collectionDefinition.Id = collectionName;
                collectionDefinition.IndexingPolicy = new IndexingPolicy(new RangeIndex(DataType.String) { Precision = -1 });
            
                var x  = await client.CreateDocumentCollectionIfNotExistsAsync(
                    UriFactory.CreateDatabaseUri(databaseName),
                    collectionDefinition,
                    new RequestOptions { OfferThroughput = int.Parse(DBDTU) });
                collection.TryAdd(collectionName, collectionName);
            return x;
        }
        private async Task<Microsoft.Azure.Documents.Document> LoadFHIRResourceObject(string databaseName, string collectionName, string identity)
        {
            try
            {
                var response = await client.ReadDocumentAsync(UriFactory.CreateDocumentUri(databaseName, collectionName, identity));
                return response;
            }
            catch (Exception de)
            {
                //Trace.TraceError("Error loading resource: {0}-{1}-{2} Message: {3}",databaseName,collectionName,identity,de.Message);
                return null;
            }
          
        }
       
        private async Task<int> CreateResourceIfNotExists(string databaseName, Hl7.Fhir.Model.Resource r)
        {
            int retstatus = -1; //Error
            
            try
            {
                if (r == null) return retstatus;
                string fh = historystore.InsertResourceHistoryItem(r);
                if (fh == null)
                {
                    //Trace.TraceError("Failed to update resource history...Upsert aborted for {0}-{1}", Enum.GetName(typeof(Hl7.Fhir.Model.ResourceType), r.ResourceType), r.Id);
                    return retstatus;
                }
                //Overflow remove attachments or error
                if (fh.Length > 500000)
                {
                    return retstatus;
                }
                JObject obj = JObject.Parse(fh);
                var inserted = await this.client.UpsertDocumentAsync(UriFactory.CreateDocumentCollectionUri(databaseName, Enum.GetName(typeof(Hl7.Fhir.Model.ResourceType), r.ResourceType)), obj);
                retstatus = (inserted.StatusCode == HttpStatusCode.Created ? 1 : 0);
                return retstatus;
            }
            catch (DocumentClientException de)
            {
                //Trace.TraceError("Error creating resource: {0}-{1}-{2} Message: {3}", databaseName,Enum.GetName(typeof(Hl7.Fhir.Model.ResourceType),r.ResourceType),r.Id,de.Message);
                historystore.DeleteResourceHistoryItem(r);
                //Trace.TraceInformation("Resource history entry for {0}-{1} version {2} rolledback due to document creation error.", Enum.GetName(typeof(Hl7.Fhir.Model.ResourceType), r.ResourceType), r.Id, r.Meta.VersionId);
                return retstatus;
            }
            
        }


        public async Task<bool> DeleteFHIRResource(Hl7.Fhir.Model.Resource r)
        {
            
            //TODO Implement Delete by Identity
            await CreateDocumentCollectionIfNotExists(DBName, Enum.GetName(typeof(Hl7.Fhir.Model.ResourceType), r.ResourceType));
            try {
                await this.client.DeleteDocumentAsync(UriFactory.CreateDocumentUri(DBName, Enum.GetName(typeof(Hl7.Fhir.Model.ResourceType), r.ResourceType),r.Id));
                return true;
            }
            catch (DocumentClientException de) {
                //Trace.TraceError("Error deleting resource type: {0} Id: {1} Message: {2}", r.ResourceType, r.Id, de.Message);
                return false;
            }                                           
        }

        public async Task<int> UpsertFHIRResource(Hl7.Fhir.Model.Resource r)
        {
            
            await CreateDocumentCollectionIfNotExists(DBName, Enum.GetName(typeof(Hl7.Fhir.Model.ResourceType), r.ResourceType));
            var x = await CreateResourceIfNotExists(DBName,r);
            return x;

        }

        public async Task<Hl7.Fhir.Model.Resource> LoadFHIRResource(string identity,string resourceType)
        {
            await CreateDocumentCollectionIfNotExists(DBName, resourceType);
            var result = await LoadFHIRResourceObject(DBName, resourceType, identity);
            if (result == null) return null;
            return ConvertDocument(result);
        }

        public async Task<ResourceQueryResult> QueryFHIRResource(string query, string resourceType,int count=100,string continuationToken=null,long querytotal=-1)
        {

            
                List<Hl7.Fhir.Model.Resource> retVal = new List<Hl7.Fhir.Model.Resource>();
                await CreateDocumentCollectionIfNotExists(DBName, resourceType);
                var options = new FeedOptions
                { 
                    MaxItemCount = count,
                    RequestContinuation = Utils.FhirHelper.URLBase64Decode(continuationToken)                  
                };
                var collection = UriFactory.CreateDocumentCollectionUri(DBName, resourceType);
                var docq = client.CreateDocumentQuery<Document>(collection, query, options).AsDocumentQuery();
                var rslt = await docq.ExecuteNextAsync<Document>();
                //Get Totalcount first
                if (querytotal < 0)
                {
                    //var totalquery = query.Replace("select value c", "select value count(1)");
                    //querytotal= (long)client.CreateDocumentQuery(collection, totalquery).AsEnumerable().First();
                    querytotal = rslt.Count;
                }
                foreach (Document doc in rslt)
                {
                     retVal.Add(ConvertDocument(doc));
                }
                return new ResourceQueryResult(retVal, querytotal, Utils.FhirHelper.URLBase64Encode(rslt.ResponseContinuation));
            
        }

        public async Task<bool> Initialize(List<object> parms)
        {
            await client.OpenAsync();
            return true;
        }
    }
}
