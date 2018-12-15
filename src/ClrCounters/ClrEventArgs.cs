using System;

namespace ClrCounters
{
    public class ClrEventArgs : EventArgs
    {
        public DateTime TimeStamp { get; }

        public int ProcessId { get; }

        public ClrEventArgs(DateTime timestamp, int processId)
        {
            TimeStamp = timestamp;
            ProcessId = processId;
        }
    }
}
