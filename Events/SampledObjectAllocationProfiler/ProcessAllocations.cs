using System;
using System.Collections.Generic;

namespace SampledObjectAllocationProfiler
{

    public class ProcessAllocations
    {
        private readonly int _pid;
        private readonly Dictionary<string, AllocationInfo> _allocations;
        private readonly Dictionary<int, AllocationInfo> _perThreadLastAllocation;

        public ProcessAllocations(int pid)
        {
            _pid = pid;
            _allocations = new Dictionary<string, AllocationInfo>();
            _perThreadLastAllocation = new Dictionary<int, AllocationInfo>();
        }

        public int Pid => _pid;

        public AllocationInfo GetAllocations(string typeName)
        {
            return (_allocations.TryGetValue(typeName, out var info)) ? info : null;
        }

        public IEnumerable<AllocationInfo> GetAllAllocations()
        {
            return _allocations.Values;
        }

        public AllocationInfo AddAllocation(int threadID, ulong size, ulong count, string typeName)
        {
            if (!_allocations.TryGetValue(typeName, out var info))
            {
                info = new AllocationInfo(typeName);
                _allocations[typeName] = info;
            }

            info.AddAllocation(size, count);

            // the last allocation is still here without the corresponding stack
            if (_perThreadLastAllocation.TryGetValue(threadID, out var lastAlloc))
            {
                Console.WriteLine("no stack for the last allocation");
            }

            // keep track of the allocation for the given thread
            // --> will be used when the corresponding call stack event will be received
            _perThreadLastAllocation[threadID] = info;

            return info;
        }

        public void AddStack(int threadID, AddressStack stack)
        {
            if (_perThreadLastAllocation.TryGetValue(threadID, out var lastAlloc))
            {
                lastAlloc.AddStack(stack);
                _perThreadLastAllocation.Remove(threadID);
                return;
            }

            //Console.WriteLine("no last allocation for the stack event");
        }
    }


    public class AllocationInfo
    {
        private readonly string _typeName;
        private ulong _size;
        private ulong _count;
        private List<StackInfo> _stacks;

        internal AllocationInfo(string typeName)
        {
            _typeName = typeName;
            _stacks = new List<StackInfo>();
        }

        public string TypeName => _typeName;
        public ulong Count => _count;
        public ulong Size => _size;
        public IReadOnlyList<StackInfo> Stacks => _stacks;

        internal void AddAllocation(ulong size, ulong count)
        {
            _count += count;
            _size += size;
        }

        internal void AddStack(AddressStack stack)
        {
            var info = GetInfo(stack);
            if (info == null)
            {
                info = new StackInfo(stack);
                _stacks.Add(info);
            }

            info.Count++;
        }

        private StackInfo GetInfo(AddressStack stack)
        {
            for (int i = 0; i < _stacks.Count; i++)
            {
                var info = _stacks[i];
                if (stack.Equals(info.Stack)) return info;
            }

            return null;
        }
    }

    public class StackInfo
    {
        private readonly AddressStack _stack;
        public ulong Count;

        internal StackInfo(AddressStack stack)
        {
            Count = 0;
            _stack = stack;
        }

        public AddressStack Stack => _stack;
    }
}
