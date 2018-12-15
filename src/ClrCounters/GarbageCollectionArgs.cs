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
        public GarbageCollectionArgs(int processId, 
            int number, int generation, GarbageCollectionReason reason, GarbageCollectionType type, 
            bool isCompacting, long gen0Size, long gen1Size, long gen2Size, long lohSize,
            double suspensionDuration)
        {
            ProcessId = processId;
            Number = number;
            Generation = generation;
            Reason = reason;
            Type = type;
            IsCompacting = isCompacting;
            Gen0Size = gen0Size;
            Gen1Size = gen1Size;
            Gen2Size = gen2Size;
            LOHSize = lohSize;
            SuspensionDuration = suspensionDuration;
        }

        public int ProcessId { get; }


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
        /// Applications threads were suspended for this duration
        /// </summary>
        public double SuspensionDuration { get; set; }
    }

}
