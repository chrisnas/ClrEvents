﻿using System;

namespace Shared
{
    public class ContentionInfo
    {
        public ContentionInfo(int processId, int threadId)
        {
            ProcessId = processId;
            ThreadId = threadId;
        }

        public DateTime TimeStamp { get; set; }

        public int ProcessId { get; }

        public int ThreadId { get; set; }

        public double ContentionStartRelativeMSec { get; set; }

        public AddressStack Stack { get; set; }
    }
}
