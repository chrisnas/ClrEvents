using System;

namespace Shared
{
    [Flags]
    public enum EventFilter
    {
        Exception         = 1 << 0,
        Finalizer         = 1 << 2,
        Contention        = 1 << 3,
        ThreadStarvation  = 1 << 4,
        GC                = 1 << 5,
        AllocationTick    = 1 << 6,
        Network           = 1 << 7,
        All               = ~(-1 << 8)
    }
}
