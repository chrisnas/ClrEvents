using System;

namespace ClrCounters
{
    public class FinalizeArgs : ClrEventArgs
    {
        public FinalizeArgs(DateTime timeStamp, int processId, ulong typeId, string typeName)
        : base(timeStamp, processId)
        {
            TypeId = typeId;
            TypeName = typeName;
        }

        public ulong TypeId { get; }

        public string TypeName { get; }
    }
}
