using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Session;

namespace AllocationTickProfiler
{
    //
    // From https://github.com/microsoft/perfview/blob/master/src/TraceEvent/Samples/41_TraceLogMonitor.cs
    // Shows how to get callstacks for ETW events and how to map address on the stack to string symbols 
    // WARNING: large memory consumption to map JITed methods names
    public class AllocationTickMemoryProfiler
    {
        private readonly TraceEventSession _session;
        private readonly ProcessAllocationInfo _allocations;
        private readonly int _pid;
        private readonly bool _verbose;
        private int _started = 0;

        public AllocationTickMemoryProfiler(TraceEventSession session, int pid, ProcessAllocationInfo allocations, bool verbose = false)
        {
            if (session == null)
                throw new NullReferenceException(nameof(session));
            _session = session;

            if (allocations == null)
                throw new NullReferenceException(nameof(allocations));
            _allocations = allocations;

            _pid = pid;
            _verbose = verbose;
        }

        public async Task StartAsync()
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) == 1)
            {
                throw new InvalidOperationException("Impossible to start profiling more than once.");
            }

            await Task.Factory.StartNew(() =>
            {
                using (_session)
                {
                    SetupProviders(_session);

                    using (TraceLogEventSource source = TraceLog.CreateFromTraceEventSession(_session))
                    {
                        SetupListeners(source);

                        source.Process();
                    }
                }
            });
        }

        private void SetupProviders(TraceEventSession session)
        {
            // Note: the kernel provider MUST be the first provider to be enabled
            // If the kernel provider is not enabled, the callstacks for CLR events are still received 
            // but the symbols are not found (except for the application itself)
            // Maybe a TraceEvent implementation details triggered when a module (image) is loaded
            session.EnableKernelProvider(
                KernelTraceEventParser.Keywords.ImageLoad |
                KernelTraceEventParser.Keywords.Process,
                KernelTraceEventParser.Keywords.None
            );

            session.EnableProvider(
                ClrTraceEventParser.ProviderGuid,
                TraceEventLevel.Verbose,    // this is needed in order to receive AllocationTick_V2 event
                (ulong)(
                // required to receive AllocationTick events
                ClrTraceEventParser.Keywords.GC
                )
            );
        }

        private void SetupListeners(TraceLogEventSource source)
        {
            source.Clr.GCAllocationTick += OnAllocationTick;
        }

        private void OnAllocationTick(GCAllocationTickTraceData data)
        {
            if (FilterOutEvent(data)) return;

            if (_verbose)
                Console.WriteLine($"{data.AllocationKind,7} | {data.AllocationAmount64,10} : {data.TypeName}");

            _allocations.AddAllocation(data.AllocationKind, (ulong)data.AllocationAmount64, data.TypeName);
        }

        private bool FilterOutEvent(TraceEvent data)
        {
            // in this example, only monitor a given process 
            return data.ProcessID != _pid;
        }
    }
}
