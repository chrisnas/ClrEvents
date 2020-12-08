using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Generic;
using System.IO;

namespace CpuSamplingProfiler
{
    public abstract class CpuSampleProfilerBase : ICpuSampleProfiler
    {
        private int _pid;
        private long _stackCount;
        private MergedSymbolicStacks _stacks;
        private Dictionary<TraceModuleFile, bool> _missingSymbols;
        private SymbolReader _symbolReader;
        private TextWriter _symbolLookupMessages;

        public CpuSampleProfilerBase()
        {
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

        public long TotalStackCount => _stackCount;

        public MergedSymbolicStacks Stacks => _stacks;

        protected int Pid => _pid;

        protected SymbolReader Reader => _symbolReader;

        protected TextWriter SymbolMessages => _symbolLookupMessages;

        protected bool EnableProviders(TraceEventSession session)
        {
            session.BufferSizeMB = 256;

            // Note: it could fail if the user does not have the required privileges
            var success = session.EnableKernelProvider(
                KernelTraceEventParser.Keywords.ImageLoad |
                KernelTraceEventParser.Keywords.Process |
                KernelTraceEventParser.Keywords.Profile,
                stackCapture: KernelTraceEventParser.Keywords.Profile
                );
            if (!success) return false;

            // this call always returns false  :^(
            session.EnableProvider(
                ClrTraceEventParser.ProviderGuid,
                TraceEventLevel.Verbose,
                (ulong)(
                // events related to JITed methods
                ClrTraceEventParser.Keywords.Jit |                       // Turning on JIT events is necessary to resolve JIT compiled code 
                ClrTraceEventParser.Keywords.JittedMethodILToNativeMap | // This is needed if you want line number information in the stacks
                ClrTraceEventParser.Keywords.Loader                      // You must include loader events as well to resolve JIT compiled code. 
                )
            );

            // this provider will send events of already JITed methods
            session.EnableProvider(
                ClrRundownTraceEventParser.ProviderGuid,
                TraceEventLevel.Verbose,
                (ulong)(
                ClrTraceEventParser.Keywords.Jit |              // We need JIT events to be rundown to resolve method names
                ClrTraceEventParser.Keywords.JittedMethodILToNativeMap | // This is needed if you want line number information in the stacks
                ClrTraceEventParser.Keywords.Loader |           // As well as the module load events.  
                ClrTraceEventParser.Keywords.StartEnumeration   // This indicates to do the rundown now (at enable time)
                ));

            return true;
        }

        public bool Start(int pid)
        {
            if (_pid > 0) throw new InvalidOperationException("Profiling session cannot be started more than once at a time...");
            if (pid <= 0) throw new ArgumentOutOfRangeException(nameof(pid), "Process ID must be greater than 0...");

            _pid = pid;
            _stackCount = 0;
            _stacks = new MergedSymbolicStacks();
            _missingSymbols = new Dictionary<TraceModuleFile, bool>();

            return OnStart();
        }

        public void Stop()
        {
            OnStop();
            _pid = 0;
        }

        protected abstract bool OnStart();
        protected abstract void OnStop();


        protected void MergeCallStack(TraceCallStack callStack, SymbolReader reader)
        {
            var currentFrame = callStack.Depth;
            var frames = new SymbolicFrame[callStack.Depth];

            // the first element of callstack is the last frame: we need to iterate on each frame
            // up to the first one before adding them into the MergedSymbolicStack
            while (callStack != null)
            {
                var codeAddress = callStack.CodeAddress;
                if (codeAddress.Method == null)
                {
                    var moduleFile = codeAddress.ModuleFile;
                    if (moduleFile != null)
                    {
                        // TODO: this seems to trigger extremely slow retrieval of symbols 
                        //       through HTTP requests: see how to delay it AFTER the user
                        //       stops the profiling
                        if (!_missingSymbols.TryGetValue(moduleFile, out var _))
                        {
                            codeAddress.CodeAddresses.LookupSymbolsForModule(reader, moduleFile);
                            if (codeAddress.Method == null)
                            {
                                _missingSymbols[moduleFile] = true;
#if DEBUG
                                Console.WriteLine($"Missing symbols for {moduleFile.ImageBase:x} - {moduleFile.Name}");
#endif
                            }
                        }
                    }
                }
                frames[--currentFrame] = new SymbolicFrame(
                    codeAddress.Address,
                    codeAddress.FullMethodName
                    );

#if DEBUG
                if (!string.IsNullOrEmpty(codeAddress.FullMethodName))
                    Console.WriteLine($"     {codeAddress.FullMethodName}");
                else
                    Console.WriteLine($"     0x{codeAddress.Address:x}");
#endif

                callStack = callStack.Caller;
            }

#if DEBUG
            Console.WriteLine($"-----------------------------------------");
            Console.WriteLine();
#endif

            _stackCount++;
            _stacks.AddStack(frames);
        }
    }
}
