using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using Shared;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace GcLog
{
    public sealed class GcEventListener : EventListener
    {
        // from https://docs.microsoft.com/en-us/dotnet/framework/performance/garbage-collection-etw-events
        private const int GC_KEYWORD = 0x0000001;
        private const int GCTriggered = 35;
        private const int GCStart = 1;
        private const int GCHeapStats = 4;
        private const int GCPerHeapHistory = 204;
        private const int GCGlobalHeapHistory = 205;
        private const int GCSuspendEEBegin = 9;
        private const int GCRestartEEEnd = 3;

        private GCInfo _gcInfo;

        private int _pid = Process.GetCurrentProcess().Id;
        private EventSource _eventSource;

        public event EventHandler<GarbageCollectionArgs> GcEvents;

        public void Stop()
        {
            if (_eventSource == null)
                return;

            DisableEvents(_eventSource);
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            _gcInfo = new GCInfo(Process.GetCurrentProcess().Id);

            _eventSource = eventSource;

            // look for .NET Garbage Collection events only
            if (eventSource.Name.Equals("Microsoft-Windows-DotNETRuntime"))
            {
                EnableEvents(eventSource, EventLevel.Informational, (EventKeywords)(GC_KEYWORD));
            }
        }

        private T GetFieldValue<T>(EventWrittenEventArgs e, string fieldName)
        {
            // this is not very optimum in term of performance but should not be a problem
            var index = e.PayloadNames.IndexOf(fieldName);
            if (index == -1)
                return default(T);

            return (T) e.Payload[index];
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
#if DEBUG
            Console.WriteLine($"ID = {e.EventId} Name = {e.EventName}  |  opcode={e.Opcode}  ==>  {e.GetType()}");
            for (int i = 0; i < e.Payload.Count; i++)
            {
                string payloadString = e.Payload[i] != null ? e.Payload[i].ToString() : string.Empty;
                string payloadType = e.Payload[i] != null ? e.Payload[i].GetType().ToString() : "?";
                Console.WriteLine($"    Name = \"{e.PayloadNames[i]}\" Value = \"{payloadString}\"  type={payloadType}");
            }
            Console.WriteLine($"    Task={e.Task}");
            Console.WriteLine("\n");
#endif
            switch (e.EventId)
            {
                case GCTriggered:
                    OnGcTriggered(e);
                    break;
                case GCStart:
                    OnGcStart(e);
                    break;
                case GCHeapStats:
                    OnGcHeapStats(e);
                    break;
                case GCPerHeapHistory:
                    OnGcPerHeapHistory(e);
                    break;
                case GCGlobalHeapHistory:
                    OnGcGlobalHeapHistory(e);
                    break;
                case GCSuspendEEBegin:
                    OnGcSuspendEEBegin(e);
                    break;
                case GCRestartEEEnd:
                    OnGcRestartEEEnd(e);
                    break;
            }
        }

        private void OnGcTriggered(EventWrittenEventArgs e)
        {
            ClearCollections(_gcInfo);
        }

        private void OnGcStart(EventWrittenEventArgs e)
        {
            // This event is received after a collection is started
            var newGC = BuildGCDetails(e);

            // If a BCG is already started, FGC (0/1) are possible and will finish before the BGC
            //
            if (
                (GetFieldValue<uint>(e, "Depth") == 2) &&
                ((GCType)GetFieldValue<uint>(e, "Type") == GCType.BackgroundGC)
                )
            {
                _gcInfo.CurrentBGC = newGC;
            }
            else
            {
                _gcInfo.GCInProgress = newGC;
            }

            // forthcoming expected events for gen 0/1 collections are GCGlobalHeapHistory then GCHeapStats
        }
        private GCDetails BuildGCDetails(EventWrittenEventArgs e)
        {
            return new GCDetails()
            {
                TimeStamp = e.TimeStamp,
                Number = (int)GetFieldValue<uint>(e, "Count"),
                Generation = (int)GetFieldValue<uint>(e, "Depth"),
                Type = (GCType)GetFieldValue<uint>(e, "Type"),
                Reason = (GCReason)GetFieldValue<uint>(e, "Reason")
            };
        }

        // This event provides the size of each generation after the collection
        // Note: last event for non background GC (will be GcGlobalHeapHistory for background gen 2)
        private void OnGcHeapStats(EventWrittenEventArgs e)
        {
            var currentGC = GetCurrentGC(_gcInfo);
            if (currentGC == null)
                return;

            currentGC.Heaps.Gen0Size = (long)GetFieldValue<ulong>(e, "GenerationSize0");
            currentGC.Heaps.Gen1Size = (long)GetFieldValue<ulong>(e, "GenerationSize1");
            currentGC.Heaps.Gen2Size = (long)GetFieldValue<ulong>(e, "GenerationSize2");
            currentGC.Heaps.LOHSize = (long)GetFieldValue<ulong>(e, "GenerationSize3");

            // this is the last event for a gen0/gen1 foreground collection during a background gen2 collections
            if (
                (_gcInfo.CurrentBGC != null) &&
                (currentGC.Generation < 2)
               )
            {
                Debug.Assert(_gcInfo.GCInProgress == currentGC);

                GcEvents?.Invoke(this, BuildGcArgs(currentGC));
                _gcInfo.GCInProgress = null;
            }
        }

        private void OnGcPerHeapHistory(EventWrittenEventArgs e)
        {
            // NOTE: .NET Core 3.0 Preview 5 does not marshal the before/after details
            // look at corresponding issue - https://github.com/dotnet/coreclr/issues/24506
        }

        // This event is used to figure out if a collection is compacting or not
        // Note: last event for background GC (will be GCHeapStats for ephemeral (0/1) and non concurrent gen 2 collections)
        private void OnGcGlobalHeapHistory(EventWrittenEventArgs e)
        {
            var currentGC = GetCurrentGC(_gcInfo);

            // check unexpected event (we should have received a GCStart first)
            if (currentGC == null)
                return;

            // check if the collection was compacting
            var globalMask = (GCGlobalMechanisms)GetFieldValue<uint>(e, "GlobalMechanisms");
            currentGC.IsCompacting =
                (globalMask & GCGlobalMechanisms.Compaction) == GCGlobalMechanisms.Compaction;

            // this is the last event for gen 2 background collections
            if ((GetFieldValue<uint>(e, "CondemnedGeneration") == 2) && (currentGC.Type == GCType.BackgroundGC))
            {
                // check unexpected generation mismatch: should never occur
                if (currentGC.Generation != GetFieldValue<uint>(e, "CondemnedGeneration"))
                    return;

                GcEvents?.Invoke(this, BuildGcArgs(currentGC));
                ClearCollections(_gcInfo);
            }
        }

        private void OnGcSuspendEEBegin(EventWrittenEventArgs e)
        {
            // we don't know yet what will be the next GC corresponding to this suspension
            // so it is kept until next GCStart
            _gcInfo.SuspensionStart = e.TimeStamp;
        }

        private void OnGcRestartEEEnd(EventWrittenEventArgs e)
        {
            var currentGC = GetCurrentGC(_gcInfo);
            if (currentGC == null)
            {
                // this should never happen, except if we are unlucky to have missed a GCStart event
                return;
            }

            // compute suspension time
            double suspensionDuration = 0;
            if (_gcInfo.SuspensionStart.HasValue)
            {
                suspensionDuration = (e.TimeStamp - _gcInfo.SuspensionStart.Value).TotalMilliseconds;
                _gcInfo.SuspensionStart = null;
            }
            else
            {
                // bad luck: a xxxBegin event has been missed
            }
            currentGC.PauseDuration += suspensionDuration;

            // could be the end of a gen0/gen1 or of a non concurrent gen2 GC
            if (
                (currentGC.Generation < 2) ||
                (currentGC.Type == GCType.NonConcurrentGC)
                )
            {
                GcEvents?.Invoke(this, BuildGcArgs(currentGC));
                _gcInfo.GCInProgress = null;
                return;
            }

            // in case of background gen2, just need to sum the suspension time
            // --> its end will be detected during GcGlobalHistory event
        }


        private GCDetails GetCurrentGC(GCInfo info)
        {
            if (info.GCInProgress != null)
            {
                return info.GCInProgress;
            }

            return info.CurrentBGC;
        }
        private void ClearCollections(GCInfo info)
        {
            info.CurrentBGC = null;
            info.GCInProgress = null;
            info.SuspensionStart = null;
        }


        private GarbageCollectionArgs BuildGcArgs(GCDetails info)
        {
            int number = info.Number;
            int generation = info.Generation;
            double startRelativeMSec = (double)info.TimeStamp.Ticks;  // TODO: translate in term of milli-seconds
            GarbageCollectionReason reason = (GarbageCollectionReason)info.Reason;
            GarbageCollectionType type = (GarbageCollectionType)info.Type;
            bool isCompacting = false;
            long gen0Size = info.Heaps.Gen0Size;
            long gen1Size = info.Heaps.Gen1Size;
            long gen2Size = info.Heaps.Gen2Size;
            long lohSize = info.Heaps.LOHSize;

            // we don't do the difference and PauseDuration accumulate all supension/pause durations
            double pauseDuration = info.PauseDuration;
            double suspensionDuration = 0;
            double finalPauseDuration = 0;

            // these are not available (yet) with EventPipes
            long[] objSizeBefore = new long[4] {0, 0, 0, 0};
            long[] objSizeAfter = new long[4] { 0, 0, 0, 0 };

            return new GarbageCollectionArgs(
                _pid, startRelativeMSec, number, generation, reason, type, isCompacting,
                gen0Size, gen1Size, gen2Size, lohSize, objSizeBefore, objSizeAfter,
                suspensionDuration, pauseDuration, finalPauseDuration);
        }
    }

}
