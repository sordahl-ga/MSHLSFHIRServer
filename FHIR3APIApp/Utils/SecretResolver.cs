using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Web;
using Microsoft.Azure;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
namespace FHIR3APIApp.Utils
{
    public class SecretResolver
    {
        private KeyVaultClient _client = null;
        private string _kvuri = null;
        public SecretResolver()
        {
                _kvuri = CloudConfigurationManager.GetSetting("KeyVaultURI");
                if (_kvuri != null)
                {
                    if (_kvuri.Last().ToString() != "/") _kvuri += "/";
                    
                }
            

        }
        private KeyVaultClient GetClient()
        {
            return null;
            if (_client != null) return _client;
            if (_kvuri == null) return null;
            try
            {
               
                    AzureServiceTokenProvider azureServiceTokenProvider = new AzureServiceTokenProvider();
                    _client = new KeyVaultClient(
                        new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback));
                return _client;
                
            }
            catch (Exception e)
            {
                Trace.TraceError("Error loading keyvault client: Message: {0}", e.Message);
            }
            return null;

        }
        public string GetConfiguration(string configname,string defaultval=null)
        {
            var retVal =  CloudConfigurationManager.GetSetting(configname);
            return (retVal != null ? retVal : defaultval);
        }
        public async System.Threading.Tasks.Task<string> GetSecret(string secretname)
        {
            //Key Vault Not Specified use Configuration
            var c = GetClient();
            if (c!=null)
            {
                var secret = await c.GetSecretAsync(_kvuri + "secrets/" + secretname)
                    .ConfigureAwait(false);
                return secret.Value;
            }
            else
            {
                return GetConfiguration(secretname);
            }
        }
    }
}