using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Symbols;
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
    // WARNING: large memory consumption to map JITed methods names in case of several .NET apps running at the same time
    public class AllocationTickMemoryProfiler
    {
        private readonly TraceEventSession _session;
        private readonly ProcessAllocationInfo _allocations;
        private readonly SymbolReader _symbolReader;
        private readonly TextWriter _symbolLookupMessages;
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

            _symbolLookupMessages = new StringWriter();

            // By default a symbol Reader uses whatever is in the _NT_SYMBOL_PATH variable.  However you can override
            // if you wish by passing it to the SymbolReader constructor.  Since we want this to work even if you 
            // have not set an _NT_SYMBOL_PATH, so we add the Microsoft default symbol server path to be sure/
            var symbolPath = new SymbolPath(SymbolPath.SymbolPathFromEnvironment).Add(SymbolPath.MicrosoftSymbolServerPath);
            _symbolReader = new SymbolReader(_symbolLookupMessages, symbolPath.ToString());

            // By default the symbol reader will NOT read PDBs from 'unsafe' locations (like next to the EXE)  
            // because hackers might make malicious PDBs. If you wish ignore this threat, you can override this
            // check to always return 'true' for checking that a PDB is 'safe'.  
            _symbolReader.SecurityCheck = (path => true);
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

                // dump the symbol related error messages if required
                if (_verbose)
                    Console.WriteLine(_symbolLookupMessages.ToString());
            });
        }

        private void SetupProviders(TraceEventSession session)
        {
            // Note: the kernel provider MUST be the first provider to be enabled
            // If the kernel provider is not enabled, the callstacks for CLR events are still received 
            // but the symbols are not found (except for the application itself)
            // TraceEvent implementation details triggered when a module (image) is loaded
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
                ClrTraceEventParser.Keywords.GC |
                ClrTraceEventParser.Keywords.Jit |                      // Turning on JIT events is necessary to resolve JIT compiled code 
                ClrTraceEventParser.Keywords.JittedMethodILToNativeMap |// This is needed if you want line number information in the stacks
                ClrTraceEventParser.Keywords.Loader |                   // You must include loader events as well to resolve JIT compiled code. 
                ClrTraceEventParser.Keywords.Stack  // Get the callstack for each event
                )
            );

            // this provider will send events of already JITed methods
            session.EnableProvider(ClrRundownTraceEventParser.ProviderGuid, TraceEventLevel.Verbose,
            (ulong)(
                ClrTraceEventParser.Keywords.Jit |              // We need JIT events to be rundown to resolve method names
                ClrTraceEventParser.Keywords.JittedMethodILToNativeMap | // This is needed if you want line number information in the stacks
                ClrTraceEventParser.Keywords.Loader |           // As well as the module load events.  
                ClrTraceEventParser.Keywords.StartEnumeration   // This indicates to do the rundown now (at enable time)
                ));

        }

        private void SetupListeners(TraceLogEventSource source)
        {
            source.Clr.GCAllocationTick += OnAllocationTick;
        }

        private void OnAllocationTick(GCAllocationTickTraceData data)
        {
            if (FilterOutEvent(data)) return;

            if (_verbose)
            {
                Console.WriteLine($"{data.AllocationKind,7} | {data.AllocationAmount64,10} : {data.TypeName}");

                var callstack = data.CallStack();
                if (callstack != null)
                {
                    DumpStack(callstack);
                }
            }

            _allocations.AddAllocation(data.AllocationKind, (ulong)data.AllocationAmount64, data.TypeName);
        }

        private void DumpStack(TraceCallStack frame)
        {
            while (frame != null)
            {
                var codeAddress = frame.CodeAddress;
                if (codeAddress.Method == null)
                {
                    var moduleFile = codeAddress.ModuleFile;
                    if (moduleFile != null)
                    {
                        codeAddress.CodeAddresses.LookupSymbolsForModule(_symbolReader, moduleFile);
                    }
                }
                if (!string.IsNullOrEmpty(codeAddress.FullMethodName))
                    Console.WriteLine($"     {codeAddress.FullMethodName}");
                else
                    Console.WriteLine($"     0x{codeAddress.Address:x}");
                frame = frame.Caller;
            }
        }

        private bool FilterOutEvent(TraceEvent data)
        {
            // in this example, only monitor a given process 
            return data.ProcessID != _pid;
        }
    }
}
