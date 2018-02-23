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
using System.Web.Http;
using FHIR3APIApp.Utils;
using FHIR3APIApp.Providers;
using Microsoft.Practices.Unity;

namespace FHIR3APIApp
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Web API configuration and services
            //Dependency Resolution
            var container = new UnityContainer();
            container.RegisterType<IFHIRHistoryStore, AzureBlobHistoryStore>(new ContainerControlledLifetimeManager());
            container.RegisterType<IFHIRStore, AzureDocDBFHIRStore>(new ContainerControlledLifetimeManager());
            config.DependencyResolver = new UnityResolver(container);
            //Must have Cors enabled
            config.EnableCors();
            // Web API routes
            config.MapHttpAttributeRoutes();
            var fred = FHIR3APIApp.Utils.FhirParmMapper.Instance;
        }
    }
}
