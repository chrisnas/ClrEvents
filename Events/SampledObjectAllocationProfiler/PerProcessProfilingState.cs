using System.Collections.Generic;

namespace SampledObjectAllocationProfiler
{
    public class PerProcessProfilingState
    {
        private readonly Dictionary<int, string> _processNames = new Dictionary<int, string>();
        private readonly Dictionary<int, ProcessTypeMapping> _perProcessTypes = new Dictionary<int, ProcessTypeMapping>();
        private readonly Dictionary<int, ProcessAllocationInfo> _perProcessAllocations = new Dictionary<int, ProcessAllocationInfo>();

        public Dictionary<int, string> Names => _processNames;
        public Dictionary<int, ProcessTypeMapping> Types => _perProcessTypes;
        public Dictionary<int, ProcessAllocationInfo> Allocations => _perProcessAllocations;
    }
}
