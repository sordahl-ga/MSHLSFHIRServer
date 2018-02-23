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
using System.Linq;
using System.Web;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Azure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.WindowsAzure.Storage.Blob;
using System.IO;
using System.Diagnostics;

namespace FHIR3APIApp.Providers
{
    public class AzureBlobHistoryStore : IFHIRHistoryStore
    {
        private static string CONTAINER = "fhirhistory";
        private CloudBlobContainer blob = null;
        public AzureBlobHistoryStore()
        {
          
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(CloudConfigurationManager.GetSetting("StorageConnectionString"));
                // Create the table if it doesn't exist.
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                blob = blobClient.GetContainerReference(CONTAINER);
                blob.CreateIfNotExists();
        }
        public string InsertResourceHistoryItem(Resource r)
        {
            try
            {
                string s = FhirSerializer.SerializeToJson(r);
                var resource = System.Text.Encoding.UTF8.GetBytes(s);
                CloudBlockBlob blockBlob = blob.GetBlockBlobReference(@Enum.GetName(typeof(ResourceType), r.ResourceType) + "/" + r.Id + "/" + r.Meta.VersionId);
                using (var stream = new MemoryStream(resource, writable: false))
                {
                    blockBlob.UploadFromStream(stream);
                }
                return s;
            }
            catch (Exception e)
            {
                Trace.TraceError("Error inserting history for resource: {0}-{1}-{2} Message:{3}", Enum.GetName(typeof(ResourceType), r.ResourceType),r.Id,r.Meta.VersionId,e.Message);
                return null;
            }
        }
        public void DeleteResourceHistoryItem(Resource r)
        {
            CloudBlockBlob blobSource = blob.GetBlockBlobReference(@Enum.GetName(typeof(ResourceType), r.ResourceType) + "/" + r.Id + "/" + r.Meta.VersionId);
            bool blobExisted = blobSource.DeleteIfExists();
            return;
        }

        public IEnumerable<string> GetResourceHistory(string resourceType, string resourceId)
        {
            //TODO: Add Paging
            List<string> retVal = new List<string>();
            string relativePath = @resourceType + "/" + resourceId;
             foreach (IListBlobItem blobItem in
                    blob.ListBlobs(relativePath, true, BlobListingDetails.All).OfType<CloudBlob>().OrderByDescending(b => b.Properties.LastModified))
            {
                string[] spl = GetFileNameFromBlobURI(blobItem.Uri).Split('/');

                if (spl != null && spl.Length > 2) {
                    string resource = GetResourceHistoryItem(spl[0], spl[1], spl[2]);
                    if (resource != null) retVal.Add(resource);
                }

            }
            return retVal;
           
        }
        private string GetFileNameFromBlobURI(Uri theUri)
        {
            string theFile = theUri.ToString();
            int dirIndex = theFile.IndexOf(CONTAINER);
            string oneFile = theFile.Substring(dirIndex + CONTAINER.Length + 1,
                theFile.Length - (dirIndex + CONTAINER.Length + 1));
            return oneFile;
        }

        public string GetResourceHistoryItem(string resourceType, string resourceid, string versionid)
        {
            CloudBlockBlob blockBlob = blob.GetBlockBlobReference(@resourceType + "/" + resourceid + "/" + versionid);
            string text = null;
            if (blockBlob.Exists())
            {
                using (var memoryStream = new MemoryStream())
                {
                    blockBlob.DownloadToStream(memoryStream);
                    text = System.Text.Encoding.UTF8.GetString(memoryStream.ToArray());
                }
            }
                    return text;
        }
    }
}