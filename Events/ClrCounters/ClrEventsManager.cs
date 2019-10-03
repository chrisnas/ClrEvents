using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Xml;
using Microsoft.Diagnostics.Tools.RuntimeClient;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Analysis;
using Microsoft.Diagnostics.Tracing.Analysis.GC;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Session;

namespace ClrCounters
{
    public class ClrEventsManager
    {
        private int _processId;
        private TypesInfo _types;
        private ContentionInfoStore _contentionStore;
        private EventFilter _filter;
        private readonly TraceEventSession _session;

        public event EventHandler<ExceptionArgs> FirstChanceException;
        public event EventHandler<FinalizeArgs> Finalize;
        public event EventHandler<ContentionArgs> Contention;
        public event EventHandler<ThreadPoolStarvationArgs> ThreadPoolStarvation;
        public event EventHandler<GarbageCollectionArgs> GarbageCollection;
        public event EventHandler<AllocationTickArgs> AllocationTick;

        private void Initialize(int processId, EventFilter filter)
        {
            _processId = processId;
            _types = new TypesInfo();
            _contentionStore = new ContentionInfoStore();
            _contentionStore.AddProcess(processId);
            _filter = filter;
        }

        // constructor for TraceEvent + ETW traces
        public ClrEventsManager(TraceEventSession session, int processId, EventFilter filter)
        {
            if (session == null)
            {
                throw new NullReferenceException($"{nameof(session)} must be provided");
            }
            Initialize(processId, filter);

            _session = session;
        }

        // constructor for EventPipe traces
        public ClrEventsManager(int processId, EventFilter filter)
        {
            Initialize(processId, filter);
        }

        private static IReadOnlyCollection<Provider> GetProviders()
        {
            var providers = new Provider[]
            {
                new Provider(
                    name: "Microsoft-Windows-DotNETRuntime",
                    keywords:   GetKeywords(),
                    eventLevel: EventLevel.Informational),
            };

            return providers;
        }

        private static ulong GetKeywords()
        {
            return (ulong)(
                ClrTraceEventParser.Keywords.Contention |           // thread contention timing
                ClrTraceEventParser.Keywords.Threading |            // threadpool events
                ClrTraceEventParser.Keywords.Exception |            // get the first chance exceptions
                ClrTraceEventParser.Keywords.GCHeapAndTypeNames |   // for finalizer type names
                ClrTraceEventParser.Keywords.Type |                 // for TypeBulkType definition of types
                ClrTraceEventParser.Keywords.GC                     // garbage collector details
                );
        }

        public void ProcessEvents()
        {
            if (_session != null)
            {
                ProcessEtwEvents();
                return;
            }

            ProcessEventPipeEvents();
        }

        private void ProcessEventPipeEvents()
        {
            var configuration = new SessionConfiguration(
                circularBufferSizeMB: 1000,
                format: EventPipeSerializationFormat.NetTrace,
                providers: GetProviders()
            );

            var binaryReader = EventPipeClient.CollectTracing(_processId, configuration, out var sessionId);
            EventPipeEventSource source = new EventPipeEventSource(binaryReader);
            RegisterListeners(source);

            // this is a blocking call
            source.Process();
        }

        private void ProcessEtwEvents()
        {
            // setup process filter if any
            TraceEventProviderOptions options = null;
            if (_processId != -1)
            {
                options = new TraceEventProviderOptions()
                {
                    ProcessIDFilter = new List<int>() { _processId },
                };
            }

            // register handlers for events on the session source
            // --------------------------------------------------
            RegisterListeners(_session.Source);

            // decide which provider to listen to with filters if needed
            _session.EnableProvider(
                ClrTraceEventParser.ProviderGuid,  // CLR provider
                ((_filter & EventFilter.AllocationTick) == EventFilter.AllocationTick) ? 
                    TraceEventLevel.Verbose : TraceEventLevel.Informational,
                GetKeywords(),
                options
            );


            // this is a blocking call until the session is disposed
            _session.Source.Process();
        }

        private void RegisterListeners(TraceEventDispatcher source)
        {
            if ((_filter & EventFilter.Exception) == EventFilter.Exception)
            {
                // get exceptions
                source.Clr.ExceptionStart += OnExceptionStart;
            }

            if ((_filter & EventFilter.Finalizer) == EventFilter.Finalizer)
            {
                // get finalizers
                source.Clr.TypeBulkType += OnTypeBulkType;
                source.Clr.GCFinalizeObject += OnGCFinalizeObject;
            }

            if ((_filter & EventFilter.Contention) == EventFilter.Contention)
            {
                // get thread contention time
                source.Clr.ContentionStart += OnContentionStart;
                source.Clr.ContentionStop += OnContentionStop;
            }

            if ((_filter & EventFilter.ThreadStarvation) == EventFilter.ThreadStarvation)
            {
                // detect ThreadPool starvation
                source.Clr.ThreadPoolWorkerThreadAdjustmentAdjustment += OnThreadPoolWorkerAdjustment;
            }

            if ((_filter & EventFilter.GC) == EventFilter.GC)
            {
                source.NeedLoadedDotNetRuntimes();
                source.AddCallbackOnProcessStart((TraceProcess proc) =>
                {
                    if (proc.ProcessID != _processId)
                        return;

                    proc.AddCallbackOnDotNetRuntimeLoad((TraceLoadedDotNetRuntime runtime) =>
                    {
                        runtime.GCEnd += (TraceProcess p, TraceGC gc) =>
                        {
                            NotifyCollection(gc);
                        };
                    });
                });

            }

            if ((_filter & EventFilter.AllocationTick) == EventFilter.AllocationTick)
            {
                // sample every ~100 KB of allocations
                source.Clr.GCAllocationTick += OnGCAllocationTick;
            }
        }

        private void OnGCAllocationTick(GCAllocationTickTraceData data)
        {
            NotifyAllocationTick(data);
        }

        private void OnExceptionStart(ExceptionTraceData data)
        {
            if (data.ProcessID != _processId)
                return;

            NotifyFirstChanceException(data.TimeStamp, data.ProcessID, data.ExceptionType, data.ExceptionMessage);
        }

        private void OnTypeBulkType(GCBulkTypeTraceData data)
        {
            if (data.ProcessID != _processId)
                return;

            // keep track of the id/name type associations
            for (int currentType = 0; currentType < data.Count; currentType++)
            {
                GCBulkTypeValues value = data.Values(currentType);
                _types[value.TypeID] = string.Intern(value.TypeName);
            }
        }
        private void OnGCFinalizeObject(FinalizeObjectTraceData data)
        {
            if (data.ProcessID != _processId)
                return;

            // the type id should have been associated to a name via a previous TypeBulkType event
            NotifyFinalize(data.TimeStamp, data.ProcessID, data.TypeID, _types[data.TypeID]);
        }

        private void OnContentionStart(ContentionTraceData data)
        {
            ContentionInfo info = _contentionStore.GetContentionInfo(data.ProcessID, data.ThreadID);
            if (info == null)
                return;

            info.TimeStamp = data.TimeStamp;
            info.ContentionStartRelativeMSec = data.TimeStampRelativeMSec;
        }
        private void OnContentionStop(ContentionTraceData data)
        
{
            ContentionInfo info = _contentionStore.GetContentionInfo(data.ProcessID, data.ThreadID);
            if (info == null)
                return;

            // unlucky case when we start to listen just after the ContentionStart event
            if (info.ContentionStartRelativeMSec == 0)
            {
                return;
            }

            var contentionDurationMSec = data.TimeStampRelativeMSec - info.ContentionStartRelativeMSec;
            info.ContentionStartRelativeMSec = 0;
            var isManaged = (data.ContentionFlags == ContentionFlags.Managed);
            NotifyContention(data.TimeStamp, data.ProcessID, data.ThreadID, TimeSpan.FromMilliseconds(contentionDurationMSec), isManaged);
        }

        private void OnThreadPoolWorkerAdjustment(ThreadPoolWorkerThreadAdjustmentTraceData data)
        {
            var listeners = ThreadPoolStarvation;
            listeners?.Invoke(this, new ThreadPoolStarvationArgs(data.TimeStamp, data.ProcessID, data.NewWorkerThreadCount));
        }


        private void NotifyFirstChanceException(DateTime timestamp, int processId, string typeName, string message)
        {
            var listeners = FirstChanceException;
            listeners?.Invoke(this, new ExceptionArgs(timestamp, processId, typeName, message));
        }
        private void NotifyFinalize(DateTime timeStamp, int processId, ulong typeId, string typeName)
        {
            var listeners = Finalize;
            listeners?.Invoke(this, new FinalizeArgs(timeStamp, processId, typeId, typeName));
        }
        private void NotifyContention(DateTime timeStamp, int processId, int threadId, TimeSpan duration, bool isManaged)
        {
            var listeners = Contention;
            listeners?.Invoke(this, new ContentionArgs(timeStamp, processId, threadId, duration, isManaged));
        }
        private void NotifyCollection(TraceGC gc)
        {
            var listeners = GarbageCollection;
            if (listeners == null)
                return;

            var sizesBefore = GetBeforeGenerationSizes(gc);
            var sizesAfter = GetAfterGenerationSizes(gc);
            listeners?.Invoke(this, new GarbageCollectionArgs(
                _processId,
                gc.StartRelativeMSec,
                gc.Number,
                gc.Generation,
                (GarbageCollectionReason)gc.Reason,
                (GarbageCollectionType)gc.Type,
                !gc.IsNotCompacting(),
                gc.HeapStats.GenerationSize0,
                gc.HeapStats.GenerationSize1,
                gc.HeapStats.GenerationSize2,
                gc.HeapStats.GenerationSize3,
                sizesBefore,
                sizesAfter,
                gc.SuspendDurationMSec,
                gc.PauseDurationMSec,
                gc.BGCFinalPauseMSec
            ));
        }

        private long[] GetBeforeGenerationSizes(TraceGC gc)
        {
            var before = true;
            return GetGenerationSizes(gc, before);
        }
        private long[] GetAfterGenerationSizes(TraceGC gc)
        {
            var after = false;
            return GetGenerationSizes(gc, after);
        }

        private long[] GetGenerationSizes(TraceGC gc, bool before)
        {
            var sizes = new long[4];
            if (gc.PerHeapHistories == null)
            {
                return sizes;
            }

            for (int heap = 0; heap < gc.PerHeapHistories.Count; heap++)
            {
                // LOH = 3
                for (int gen = 0; gen <= 3; gen++)
                {
                    sizes[gen] += before ? 
                        gc.PerHeapHistories[heap].GenData[gen].ObjSpaceBefore:
                        gc.PerHeapHistories[heap].GenData[gen].ObjSizeAfter;
                }
            }

            return sizes;
        }


        private void NotifyAllocationTick(GCAllocationTickTraceData info)
        {
            var listeners = AllocationTick;
            listeners?.Invoke(this, new AllocationTickArgs(
                info.TimeStamp,
                info.ProcessID,
                info.AllocationAmount,
                info.AllocationAmount64,
                info.AllocationKind == GCAllocationKind.Large,
                info.TypeName,
                info.HeapIndex,
                info.Address
            ));
        }
    }
}
