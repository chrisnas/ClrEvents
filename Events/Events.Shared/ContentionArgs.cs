using System;
using System.Collections.Generic;

namespace Shared
{
    public class ContentionArgs : ClrEventArgs
    {
        public ContentionArgs(DateTime timestamp, int processId, int threadId, TimeSpan duration, bool isManaged, List<string> callstack)
        : base(timestamp, processId)
        {
            Duration = duration;
            IsManaged = isManaged;
            ThreadId = threadId;
            Callstack = callstack;
        }

        public TimeSpan Duration { get; }

        public bool IsManaged { get; }

        public int ThreadId { get; }

        public List<string> Callstack { get; }
    }
}