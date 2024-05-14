using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System;

namespace ClrCounters
{
    public struct AllocationTickArgs
    {
        internal AllocationTickArgs(DateTime timeStamp, int processId, int allocationAmount, long allocationAmount64,
            GCAllocationKind allocationKind, string typeName, int heapIndex, ulong address)
        {
            TimeStamp = timeStamp;
            ProcessId = processId;
            AllocationAmount = allocationAmount;
            AllocationAmount64 = allocationAmount64;
            AllocationKind = allocationKind;
            TypeName = typeName;
            HeapIndex = heapIndex;
            Address = address;
        }

        public DateTime TimeStamp { get; }
        public int ProcessId { get; }
        public int AllocationAmount { get; }
        public long AllocationAmount64 { get; }
        public GCAllocationKind AllocationKind { get; }
        public string TypeName { get; }
        public int HeapIndex { get; }
        public ulong Address { get; }
    }
}
