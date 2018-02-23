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
using FHIR3APIApp.Providers;
namespace FHIR3APIApp.Utils
{
    public class FhirParmMapper
    {
        private static volatile FhirParmMapper instance;
        private static object syncRoot = new Object();
        private Dictionary<string, string> _pmap = new Dictionary<string, string>();
        private FhirParmMapper()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "FHIR3APIApp.FHIRParameterMappings.txt";
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
        public string GenerateQuery(IFHIRStore store, string resourceType, NameValueCollection parms)
        {
            StringBuilder where = new StringBuilder();
            //Select statement for Resource
            StringBuilder select = new StringBuilder();
            select.Append(store.SelectAllQuery);
            foreach (string key in parms)
            {
                string value = parms[key];
                string parmdef = null;
                _pmap.TryGetValue((resourceType + "." + key), out parmdef);
                if (parmdef != null)
                {

                    //TODO Handle Setting up Parm Type and process value for prefix and modifiers
                    //Add JOINS to select
                    string join = null;
                    _pmap.TryGetValue(resourceType + "." + key + ".join", out join);
                    if (join != null)
                    {
                        if (!select.ToString().Contains(join))
                            select.Append(" " + join);
                    }
                    //Add Where clauses/bind values
                    string querypiece = null;
                    _pmap.TryGetValue(resourceType + "." + key + ".default",out querypiece);
                    if (querypiece != null)
                    {
                        if (where.Length == 0)
                        {
                            where.Append(" WHERE (");
                        }
                        else
                        {
                            where.Append(" and (");
                        }
                        //Handle bind values single or multiple
                        string[] vals = value.Split(',');
                        foreach (string s in vals)
                        {
                            string currentpiece = querypiece;
                            string[] t = s.Split('|');
                            int x = 0;
                            foreach (string u in t)
                            {
                                currentpiece = currentpiece.Replace(("~v" + x++ + "~"), u);
                            
                            }
                            where.Append("(" + currentpiece + ") OR ");

                        }
                        where.Remove(where.Length - 3, 3);
                        where.Append(")");
                        
                    }
                }
            }
            return select.ToString() + where.ToString();
        }
        public static FhirParmMapper Instance
        {
            get
            {
                if (instance==null)
                {
                    lock (syncRoot)
                    {
                        if (instance==null)
                            instance = new FhirParmMapper();
                    }
                }
                return instance;
            }
        }
    }
}