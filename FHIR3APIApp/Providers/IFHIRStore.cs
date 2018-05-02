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
using System.Text;
using Hl7.Fhir.Model;
namespace FHIR3APIApp.Providers
{
    public interface IFHIRStore
    {
        System.Threading.Tasks.Task<ResourceQueryResult> QueryFHIRResource(string query,string resourceType,int pagesize=100,string continuationToken=null,long querytotal=-1);
        System.Threading.Tasks.Task<int> UpsertFHIRResource(Resource r);
        System.Threading.Tasks.Task<Resource> LoadFHIRResource(string identity,string resourceType);
        System.Threading.Tasks.Task<bool> DeleteFHIRResource(Resource r);
        System.Threading.Tasks.Task<bool> Initialize(List<Object> parms);
        string SelectAllQuery { get; }
        IFHIRHistoryStore HistoryStore { get; }
 
    }

}
