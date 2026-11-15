using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PsdParser
{
    internal class InvalidSignatureException(string signature) : InvalidPsdDataException($"Invalid signature: {signature}")
    {
    }
}
