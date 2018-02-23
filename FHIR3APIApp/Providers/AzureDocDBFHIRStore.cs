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
        
        private ConcurrentDictionary<string, string> collection = new ConcurrentDictionary<string, string>();
        private bool databasecreated = false;
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
        
        public AzureDocDBFHIRStore(IFHIRHistoryStore history) 
        {
            this.client = new DocumentClient(new Uri(EndpointUri), PrimaryKey);
            historystore = history;
            ParserSettings ps = new ParserSettings();
            ps.AcceptUnknownMembers = true;
            ps.AllowUnrecognizedEnums = true;
            parser = new FhirJsonParser(ps);
           
   
        }    
       
        private async Task CreateDatabaseIfNotExists(string databaseName)
        {
            // Check to verify a database with the id does not exist
            if (databasecreated) return; 
            try
            {
                await this.client.ReadDatabaseAsync(UriFactory.CreateDatabaseUri(databaseName));
               
            }
            catch (DocumentClientException de)
            {
                // If the database does not exist, create a new database
                if (de.StatusCode == HttpStatusCode.NotFound)
                {
                    await this.client.CreateDatabaseAsync(new Database { Id = databaseName });
                    databasecreated = true;
                    //Trace.TraceInformation("Created database {0}", databaseName);
                }
                else
                {
                    throw;
                }
            }
            catch (Exception e)
            {
                var s = e.Message;
            }
        }
        private async Task CreateDocumentCollectionIfNotExists(string databaseName, string collectionName)
        {
            try
            {
                if (collection.ContainsKey(collectionName)) return;
                await CreateDatabaseIfNotExists(DBName);
                await this.client.ReadDocumentCollectionAsync(UriFactory.CreateDocumentCollectionUri(databaseName, collectionName));
                if (collection.TryAdd(collectionName, collectionName)) ;//Trace.TraceInformation("Added " + collectionName + " to db collections");
               
            }
            catch (DocumentClientException de)
            {
                // If the document collection does not exist, create a new collection
                if (de.StatusCode == HttpStatusCode.NotFound)
                {
                    DocumentCollection collectionInfo = new DocumentCollection();
                    collectionInfo.Id = collectionName;

                    // Optionally, you can configure the indexing policy of a collection. Here we configure collections for maximum query flexibility 
                    // including string range queries. 
                    collectionInfo.IndexingPolicy = new IndexingPolicy(new RangeIndex(DataType.String) { Precision = -1 });
                    collectionInfo.IndexingPolicy.IndexingMode = IndexingMode.Consistent;

                    // DocumentDB collections can be reserved with throughput specified in request units/second. 1 RU is a normalized request equivalent to the read
                    // of a 1KB document.  Here we create a collection with 400 RU/s. 
                    await this.client.CreateDocumentCollectionAsync(
                        UriFactory.CreateDatabaseUri(databaseName),
                        collectionInfo,
                        new RequestOptions { OfferThroughput = 400 });

                    //Trace.TraceInformation("Created {0}", collectionName);
                    if (collection.TryAdd(collectionName, collectionName)) ;//Trace.TraceInformation("Added " + collectionName + " to db collections");
                }
                else
                {
                    throw;
                }
            }
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

            var retVal = (JObject)(dynamic)result;
            var t = parser.Parse(retVal.ToString(Newtonsoft.Json.Formatting.None),FhirHelper.ResourceTypeFromString(resourceType));
            return (Hl7.Fhir.Model.Resource) t;
        }

        public async Task<IEnumerable<Hl7.Fhir.Model.Resource>> QueryFHIRResource(string query, string resourceType)
        {

            
            List<Hl7.Fhir.Model.Resource> retVal = new List<Hl7.Fhir.Model.Resource>();
            try
            {
                await CreateDocumentCollectionIfNotExists(DBName, resourceType);
                var found = client.CreateDocumentQuery<JObject>(UriFactory.CreateDocumentCollectionUri(DBName, resourceType),
                    query).AsEnumerable();
                
                foreach (JObject obj in found)
                {
                    retVal.Add((Hl7.Fhir.Model.Resource) parser.Parse(obj.ToString(Newtonsoft.Json.Formatting.None), FhirHelper.ResourceTypeFromString(resourceType)));
                }
                return retVal;
            }
            catch (Exception de)
            {
                //Trace.TraceError("Error querying resource type: {0} Query: {1} Message: {2}", resourceType,query,de.Message);
                throw;
            }
        }
    }
}
