using System;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using ClrCounters;

namespace GcLog
{
    internal class GCDetails
    {
        public GCDetails()
        {
            Heaps.Gen0Size = -1;
            Heaps.Gen1Size = -1;
            Heaps.Gen2Size = -1;
            Heaps.LOHSize = -1;
        }

        public DateTime TimeStamp { get; set; }

        public double PauseDuration { get; set; }

        public int Number { get; set; }

        public GCReason Reason { get; set; }

        public GCType Type { get; set; }

        public int Generation { get; set; }

        public bool IsCompacting { get; set; }

        public HeapDetails Heaps;
    }
}
