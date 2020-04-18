using System.Collections.Generic;

namespace SampledObjectAllocationProfiler
{
    // Contains the mapping between type ID received by SampleObjectAllocation(Low/High) events 
    // and their name received by TypeBulkType events
    public class ProcessTypeMapping
    {
        private readonly Dictionary<ulong, string> _typesIdToName;

        public ProcessTypeMapping(int processId)
        {
            ProcessId = processId;
            _typesIdToName = new Dictionary<ulong, string>();
        }

        public int ProcessId { get; set; }

        public string this[ulong id]
        {
            get
            {
                if (!_typesIdToName.ContainsKey(id))
                    return null;

                return _typesIdToName[id];
            }
            set
            {
                _typesIdToName[id] = value;
            }
        }

    }
}
