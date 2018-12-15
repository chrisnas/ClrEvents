using System;

namespace ClrCounters
{
    public class ContentionArgs : ClrEventArgs
    {
        public ContentionArgs(DateTime timestamp, int processId, int threadId, TimeSpan duration, bool isManaged)
        : base(timestamp, processId)
        {
            Duration = duration;
            IsManaged = isManaged;
            ThreadId = threadId;
        }

        public TimeSpan Duration { get; }

        public bool IsManaged { get; }

        public int ThreadId { get; }
    }
}