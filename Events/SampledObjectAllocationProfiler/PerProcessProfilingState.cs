using ProfilerHelpers;
using System;
using System.Collections.Generic;

namespace SampledObjectAllocationProfiler
{
    public class PerProcessProfilingState : IDisposable
    {
        private bool _disposed;

        private readonly Dictionary<int, string> _processNames = new Dictionary<int, string>();
        private readonly Dictionary<int, ProcessTypeMapping> _perProcessTypes = new Dictionary<int, ProcessTypeMapping>();
        private readonly Dictionary<int, ProcessAllocations> _perProcessAllocations = new Dictionary<int, ProcessAllocations>();
        private readonly Dictionary<int, MethodStore> _methods = new Dictionary<int, MethodStore>();

        public Dictionary<int, string> Names => _processNames;
        public Dictionary<int, ProcessTypeMapping> Types => _perProcessTypes;
        public Dictionary<int, ProcessAllocations> Allocations => _perProcessAllocations;
        public Dictionary<int, MethodStore> Methods => _methods;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var methodStore in _methods.Values)
            {
                methodStore.Dispose();
            }
        }
    }
}
