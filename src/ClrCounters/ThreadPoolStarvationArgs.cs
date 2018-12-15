using System;

namespace ClrCounters
{
    public class ThreadPoolStarvationArgs : ClrEventArgs
    {
        public ThreadPoolStarvationArgs(DateTime timestamp, int processId, int workerThreadCount)
            : base(timestamp, processId)
        {
            WorkerThreadCount = workerThreadCount;
        }

        public int WorkerThreadCount { get; set; }
    }
}
