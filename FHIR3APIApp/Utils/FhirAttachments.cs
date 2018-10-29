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
using System.IO;
using System.Reflection;
using System.Collections.Specialized;
using System.Text;
using Hl7.Fhir.Model;
using Newtonsoft.Json.Linq;

namespace FHIR3APIApp.Utils
{
    public class FhirAttachments
    {
        private static volatile FhirAttachments instance;
        private static object syncRoot = new Object();
        private Dictionary<string, string> _pmap = new Dictionary<string, string>();
        private FhirAttachments()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "FHIR3APIApp.AttachmentResources.txt";
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            using (StreamReader reader = new StreamReader(stream))
            {
                string s = null;
                while ((s=reader.ReadLine()) !=null)
                {
                    if (!s.StartsWith("#")) {
                        int split = s.IndexOf('=');
                        if (split > -1)
                        {
                            string name = s.Substring(0, split);
                            string value = s.Substring(split+1);
                            _pmap.Add(name, value);
                        }
                    }
                }
            }

        }
        public bool HasAttachment(JObject res)
        {
            return (res["_attach"] != null);

        }
        
        public JObject RemoveAttachementData(string resourcetype, JObject res)
        {
            string[] paths = GetAttachmentPaths(resourcetype);
            res.Remove("_fhirattach");
            if (paths == null) return res;
            bool inlineattach = false;
            foreach (string path in paths)
            {
                IEnumerable<JToken> attachments = res.SelectTokens(path);
                foreach (JToken tok in attachments)
                {
                        if (tok.Type == JTokenType.Array)
                        {
                            foreach (JToken j in tok)
                            {
                                string val = (string)j["data"];
                                if (!String.IsNullOrEmpty(val))
                                {
                                    j["data"] = "~REMOVED~";
                                    inlineattach = true;
                                }

                            }
                        }
                        else
                        {
                            string val = (string)tok["data"];
                            if (!String.IsNullOrEmpty(val))
                            {
                                tok["data"] = "~REMOVED~";
                                inlineattach = true;
                            }
                        }
                    }
                
          
            }
            if (inlineattach) res.Add("_fhirattach", "true");
            return res;
        }
        private string[] GetAttachmentPaths(string resourceType)
        {
            string retVal = null;
            _pmap.TryGetValue(resourceType, out retVal);
            return (retVal != null ? retVal.Split(',') : null);
        }
        public static FhirAttachments Instance
        {
            get
            {
                if (instance==null)
                {
                    lock (syncRoot)
                    {
                        if (instance==null)
                            instance = new FhirAttachments();
                    }
                }
                return instance;
            }
        }
    }
}