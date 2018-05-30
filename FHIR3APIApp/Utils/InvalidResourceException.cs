using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace FHIR3APIApp.Utils
{
    public class InvalidResourceException : Exception
    {
        public InvalidResourceException(string message) : base(message)
        {
        }
    }
}