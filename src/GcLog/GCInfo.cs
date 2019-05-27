using System;

namespace GcLog
{
    internal class GCInfo
    {
        public GCInfo(int processId)
        {
            ProcessId = processId;
        }

        // time when SuspendEEBegin is received for this process
        // --> from here, all app threads will be suspended until RestartEEStop is received
        // Note that we don't know yet what will be the triggered GC
        public DateTime? SuspensionStart { get; set; }

        // When a background garbage collection (BGC) is started,
        // other foreground garbage collection (FGC) for gen 0 and 1 could happen
        // before the original BGC ends
        //
        public GCDetails CurrentBGC { get; set; }

        // this could contain a FGC after a BGC has started
        // or a non-concurrent GC
        public GCDetails GCInProgress { get; set; }


        public int ProcessId { get; }
    }

}
