using System.Collections.Generic;

namespace Shared
{
    public class MethodStore
    {
        // JITed methods information (start address + size + signature)
        private readonly List<MethodInfo> _methods;

        // addresses from callstacks already matching (address -> full name)
        private readonly Dictionary<ulong, string> _cache;

        public MethodStore(int pid, bool loadModules = false)
        {
            _methods = new List<MethodInfo>(1024);
            _cache = new Dictionary<ulong, string>();
        }

        public MethodInfo Add(ulong address, int size, string namespaceAndTypeName, string name, string signature)
        {
            var method = new MethodInfo(address, size, namespaceAndTypeName, name, signature);
            _methods.Add(method);
            return method;
        }

        public string GetFullName(ulong address)
        {
            if (_cache.TryGetValue(address, out var fullName))
                return fullName;

            // look for managed methods
            for (int i = 0; i < _methods.Count; i++)
            {
                var method = _methods[i];

                if ((address >= method.StartAddress) && (address < method.StartAddress + (ulong)method.Size))
                {
                    fullName = method.FullName;
                    _cache[address] = fullName;
                    return fullName;
                }
            }

            // look for native methods
            fullName = GetNativeMethodName(address);
            _cache[address] = fullName;

            return fullName;
        }

        private string GetNativeMethodName(ulong address)
        {
            return $"0x{address:x}";
        }
    }
}
