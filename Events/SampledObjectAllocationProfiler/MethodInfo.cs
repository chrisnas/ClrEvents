using System;

namespace SampledObjectAllocationProfiler
{
    public class MethodInfo
    {
        private readonly ulong _startAddress;
        private readonly int _size;
        private readonly string _fullName;

        internal MethodInfo(ulong startAddress, int size, string namespaceAndTypeName, string name, string signature)
        {
            _startAddress = startAddress;
            _size = size;
            _fullName = ComputeFullName(startAddress, namespaceAndTypeName, name, signature);
        }

        private string ComputeFullName(ulong startAddress, string namespaceAndTypeName, string name, string signature)
        {
            var fullName = signature;

            // constructor case: name = .ctor | namespaceAndTypeName = A.B.typeName | signature = ...  (parameters)
            // --> A.B.typeName(parameters)
            if (name == ".ctor")
            {
                return $"{namespaceAndTypeName}{ExtractParameters(signature)}";
            }

            // general case: name = Foo | namespaceAndTypeName = A.B.typeName | signature = ...  (parameters)
            // --> A.B.Foo(parameters)
            fullName = $"{namespaceAndTypeName}.{name}{ExtractParameters(signature)}";
            return fullName;
        }

        private string ExtractTypeName(string namespaceAndTypeName)
        {
            var pos = namespaceAndTypeName.LastIndexOf(".", StringComparison.Ordinal);
            if (pos == -1)
            {
                return namespaceAndTypeName;
            }
            
            // skip the .
            pos++;

            return namespaceAndTypeName.Substring(pos);
        }

        private string ExtractParameters(string signature)
        {
            var pos = signature.IndexOf("  (");
            if (pos == -1)
            {
                return "(???)";
            }

            // skip double space
            pos += 2;

            var parameters = signature.Substring(pos);
            return parameters;
        }

        public ulong StartAddress => _startAddress;
        public int Size => _size;
        public string FullName => _fullName;
    }
}
