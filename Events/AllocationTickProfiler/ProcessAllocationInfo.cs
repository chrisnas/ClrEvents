using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System.Collections.Generic;

namespace AllocationTickProfiler
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

        public void AddAllocation(GCAllocationKind kind, ulong size, string typeName)
        {
            if (!_allocations.TryGetValue(typeName, out var info))
            {
                info = new AllocationInfo(typeName);
                _allocations[typeName] = info;
            }

            info.AddAllocation(kind, size);
        }
    }

    public class AllocationInfo
    {
        private readonly string _typeName;
        private ulong _smallSize;
        private ulong _largeSize;
        private ulong _smallCount;
        private ulong _largeCount;

        public AllocationInfo(string typeName)
        {
            _typeName = typeName;
        }

        public string TypeName => _typeName;
        public ulong Count => _smallCount + _largeCount;
        public ulong SmallCount => _smallCount;
        public ulong LargeCount => _largeCount;
        public ulong Size => _smallSize + _largeSize;
        public ulong SmallSize => _smallSize;
        public ulong LargeSize => _largeSize;

        public void AddAllocation(GCAllocationKind kind, ulong size)
        {
            if (kind == GCAllocationKind.Small)
            {
                _smallCount++;
                _smallSize += size;
            }
            else
            {
                _largeCount++;
                _largeSize += size;
            }
        }
    }

}
