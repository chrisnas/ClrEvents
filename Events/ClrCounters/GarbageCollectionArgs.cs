namespace ClrCounters
{
    public enum GarbageCollectionReason
    {
        AllocSmall,
        Induced,
        LowMemory,
        Empty,
        AllocLarge,
        OutOfSpaceSOH,
        OutOfSpaceLOH,
        InducedNotForced,
        Internal,
        InducedLowMemory,
    }

    public enum GarbageCollectionType
    {
        NonConcurrentGC,
        BackgroundGC,
        ForegroundGC,
    }

    public struct GarbageCollectionArgs
    {
        public GarbageCollectionArgs(int processId, double startRelativeMSec,
            int number, int generation, GarbageCollectionReason reason, GarbageCollectionType type, 
            bool isCompacting, long gen0Size, long gen1Size, long gen2Size, long lohSize,
            long[] objSizeBefore, long[] objSizeAfter,
            double suspensionDuration, double pauseDuration, double finalPauseDuration)
        {
            ProcessId = processId;
            StartRelativeMSec = startRelativeMSec;
            Number = number;
            Generation = generation;
            Reason = reason;
            Type = type;
            IsCompacting = isCompacting;
            Gen0Size = gen0Size;
            Gen1Size = gen1Size;
            Gen2Size = gen2Size;
            LOHSize = lohSize;
            ObjSizeBefore = objSizeBefore;
            ObjSizeAfter = objSizeAfter;
            SuspensionDuration = suspensionDuration;
            PauseDuration = pauseDuration;
            BGCFinalPauseDuration = finalPauseDuration;
        }

        public int ProcessId { get; }

        /// <summary>
        /// Time relative to the start of the trace.  Useful for ordering
        /// </summary>
        public double StartRelativeMSec { get; set; }

        /// <summary>
        /// Number of collections since the beginning of the application
        /// </summary>
        public int Number { get; set; }

        /// <summary>
        /// Collection generation (from 0 to 2)
        /// </summary>
        public int Generation { get; set; }

        public GarbageCollectionReason Reason { get; set; }

        public GarbageCollectionType Type { get; set; }

        /// <summary>
        /// True when a compaction phase occured during the collection
        /// </summary>
        /// <remarks>This value seems invalid</remarks>
        public bool IsCompacting { get; set; }

        // size of the generations after the collection
        public long Gen0Size { get; set; }
        public long Gen1Size { get; set; }
        public long Gen2Size { get; set; }
        public long LOHSize { get; set; }

        /// <summary>
        /// Size of each generation before the collection (free space is not included)
        /// </summary>
        /// <remarks>LOH index is 3 </remarks>
        public long[] ObjSizeBefore { get; set; }

        /// <summary>
        /// Size of each generation after the collection (free space is not included)
        /// </summary>
        /// <remarks>LOH index is 3 </remarks>
        public long[] ObjSizeAfter { get; set; }

        /// <summary>
        /// Time taken by EE to suspend the application threads
        /// </summary>
        public double SuspensionDuration { get; set; }

        /// <summary>
        /// Initial pause time in GC
        /// </summary>
        public double PauseDuration { get; set; }

        /// <summary>
        /// Final pause time in GC
        /// </summary>
        public double BGCFinalPauseDuration { get; set; }
    }

}
