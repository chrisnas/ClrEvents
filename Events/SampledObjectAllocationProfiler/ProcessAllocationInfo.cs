using System.Collections.Generic;

namespace SampledObjectAllocationProfiler
{

    public class ProcessAllocationInfo
    {
        private readonly int _pid;
        private readonly Dictionary<string, AllocationInfo> _allocations;

        public ProcessAllocationInfo(int pid)
        {
            _pid = pid;
            _allocations = new Dictionary<string, AllocationInfo>();
        }

        public int Pid => _pid;

        public AllocationInfo GetAllocations(string typeName)
        {
            return (_allocations.TryGetValue(typeName, out var info)) ? info : null;
        }

        public IEnumerable<AllocationInfo> GetAllocations()
        {
            return _allocations.Values;
        }

        public void AddAllocation(ulong size, ulong count, string typeName)
        {
            if (!_allocations.TryGetValue(typeName, out var info))
            {
                info = new AllocationInfo(typeName);
                _allocations[typeName] = info;
            }

            info.AddAllocation(size, count);
        }
    }


    public class AllocationInfo
    {
        private readonly string _typeName;
        private ulong _size;
        private ulong _count;

        internal AllocationInfo(string typeName)
        {
            _typeName = typeName;
        }

        public string TypeName => _typeName;
        public ulong Count => _count;
        public ulong Size => _size;

        internal void AddAllocation(ulong size, ulong count)
        {
            _count += count;
            _size += size;
        }
    }
}
