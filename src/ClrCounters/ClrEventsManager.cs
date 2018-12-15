using System;
using System.Collections.Generic;
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
        private readonly TraceEventSession _session;
        private readonly int _processId;
        private readonly TypesInfo _types;
        private readonly ContentionInfoStore _contentionStore;
        private readonly EventFilter _filter;

        public event EventHandler<ExceptionArgs> FirstChanceException;
        public event EventHandler<FinalizeArgs> Finalize;
        public event EventHandler<ContentionArgs> Contention;
        public event EventHandler<ThreadPoolStarvationArgs> ThreadPoolStarvation;
        public event EventHandler<GarbageCollectionArgs> GarbageCollection;
        public event EventHandler<AllocationTickArgs> AllocationTick;

        public ClrEventsManager(TraceEventSession session, int processId, EventFilter filter)
        {
            _session = session;
            _processId = processId;
            _types = new TypesInfo();
            _contentionStore = new ContentionInfoStore();
            _contentionStore.AddProcess(processId);
            _filter = filter;
        }


        public void ProcessEvents()
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

            if ((_filter & EventFilter.Exception) == EventFilter.Exception)
            {
                // get exceptions
                _session.Source.Clr.ExceptionStart += OnExceptionStart;
            }

            if ((_filter & EventFilter.Finalizer) == EventFilter.Finalizer)
            {
                // get finalizers
                _session.Source.Clr.TypeBulkType += OnTypeBulkType;
                _session.Source.Clr.GCFinalizeObject += OnGCFinalizeObject;
            }

            if ((_filter & EventFilter.Contention) == EventFilter.Contention)
            {
                // get thread contention time
                _session.Source.Clr.ContentionStart += OnContentionStart;
                _session.Source.Clr.ContentionStop += OnContentionStop;
            }

            if ((_filter & EventFilter.ThreadStarvation) == EventFilter.ThreadStarvation)
            {
                // detect ThreadPool starvation
                _session.Source.Clr.ThreadPoolWorkerThreadAdjustmentAdjustment += OnThreadPoolWorkerAdjustment;
            }

            if ((_filter & EventFilter.GC) == EventFilter.GC)
            {
                var source = _session.Source;
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
                _session.Source.Clr.GCAllocationTick += OnGCAllocationTick;
            }

            // decide which provider to listen to with filters if needed
            _session.EnableProvider(
                ClrTraceEventParser.ProviderGuid,  // CLR provider
                ((_filter & EventFilter.AllocationTick) == EventFilter.AllocationTick) ? 
                    TraceEventLevel.Verbose : TraceEventLevel.Informational,
                (ulong)(
                ClrTraceEventParser.Keywords.Contention |           // thread contention timing
                ClrTraceEventParser.Keywords.Threading |            // threadpool events
                ClrTraceEventParser.Keywords.Exception |            // get the first chance exceptions
                ClrTraceEventParser.Keywords.GCHeapAndTypeNames |   // for finalizer type names
                ClrTraceEventParser.Keywords.Type |                 // for TypeBulkType definition of types
                ClrTraceEventParser.Keywords.GC                     // garbage collector details
                ),
                options
            );


            // this is a blocking call until the session is disposed
            _session.Source.Process();
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

            listeners(this, new GarbageCollectionArgs(
                _processId,
                gc.Number,
                gc.Generation,
                (GarbageCollectionReason)gc.Reason,
                (GarbageCollectionType)gc.Type,
                !gc.IsNotCompacting(),
                gc.HeapStats.GenerationSize0,
                gc.HeapStats.GenerationSize1,
                gc.HeapStats.GenerationSize2,
                gc.HeapStats.GenerationSize3,
                gc.SuspendDurationMSec
            ));
        }
        private void NotifyAllocationTick(GCAllocationTickTraceData info)
        {
            var listeners = AllocationTick;
            if (listeners == null)
                return;

            listeners(this, new AllocationTickArgs(
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
