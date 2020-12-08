using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using ProfilerHelpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SampledObjectAllocationProfiler
{
    public class SampledObjectAllocationMemoryProfiler
    {
        private readonly TraceEventSession _session;
        private readonly PerProcessProfilingState _processes;
        
        // because we are not interested in self monitoring
        private readonly int _currentPid;

        private int _started = 0;

        public SampledObjectAllocationMemoryProfiler(TraceEventSession session, PerProcessProfilingState processes)
        {
            _session = session;
            _processes = processes;
            _currentPid = Process.GetCurrentProcess().Id;
        }

        public async Task StartAsync(bool allAllocations)
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) == 1)
            {
                throw new InvalidOperationException("Impossible to start profiling more than once.");
            }

            await Task.Factory.StartNew(() =>
            {
                using (_session)
                {
                    SetupProviders(_session, allAllocations);
                    SetupListeners(_session.Source);
                    
                    _session.Source.Process();
                }
            });
        }

        private void SetupProviders(TraceEventSession session, bool noSampling)
        {
            // Note: the kernel provider MUST be the first provider to be enabled
            // If the kernel provider is not enabled, the callstacks for CLR events are still received 
            // but the symbols are not found (except for the application itself)
            // Maybe a TraceEvent implementation details triggered when a module (image) is loaded
            var success = session.EnableKernelProvider(
                KernelTraceEventParser.Keywords.ImageLoad |
                KernelTraceEventParser.Keywords.Process,
                KernelTraceEventParser.Keywords.None
            );

            // The CLR source code indicates that the provider must be set before the monitored application starts
            // Note: no real difference between High and Low
            ClrTraceEventParser.Keywords eventsKeyword = noSampling 
                ? ClrTraceEventParser.Keywords.GCSampledObjectAllocationLow | ClrTraceEventParser.Keywords.GCSampledObjectAllocationHigh
                : ClrTraceEventParser.Keywords.GCSampledObjectAllocationLow
                ;

            success = session.EnableProvider(
                ClrTraceEventParser.ProviderGuid,
                TraceEventLevel.Verbose,    // this is needed in order to receive GCSampledObjectAllocation event
                (ulong)(

                eventsKeyword |

                // required to receive the BulkType events that allows 
                // mapping between the type ID received in the allocation events
                ClrTraceEventParser.Keywords.GCHeapAndTypeNames |
                ClrTraceEventParser.Keywords.Type |

                // events related to JITed methods
                ClrTraceEventParser.Keywords.Jit |              // Turning on JIT events is necessary to resolve JIT compiled code 
                ClrTraceEventParser.Keywords.JittedMethodILToNativeMap | // This is needed if you want line number information in the stacks
                ClrTraceEventParser.Keywords.Loader |                   // You must include loader events as well to resolve JIT compiled code. 

                // this is mandatory to get the callstacks in each CLR event payload.
                ClrTraceEventParser.Keywords.Stack
                )
            );

            // Note: ClrRundown is not needed because only new processes will be monitored
        }

        private void SetupListeners(ETWTraceEventSource source)
        {
            // register for high and low keyword
            // if both are set, each allocation will trigger an event (beware performance issues...)
            source.Clr.GCSampledObjectAllocation += OnSampleObjectAllocation;

            // required to receive the mapping between type ID (received in GCSampledObjectAllocation)
            // and their name (received in TypeBulkType)
            source.Clr.TypeBulkType += OnTypeBulkType;

            // messages to get callstacks
            // the correlation seems to be as "simple" as taking the last event on the same thread
            source.Clr.ClrStackWalk += OnClrStackWalk;

            // needed to get JITed method details
            source.Clr.MethodLoadVerbose += OnMethodDetails;
            source.Clr.MethodDCStartVerboseV2 += OnMethodDetails;

            // get notified when a module is load to map the corresponding symbols
            source.Kernel.ImageLoad += OnImageLoad;
        }

        private void OnImageLoad(ImageLoadTraceData data)
        {
            if (FilterOutEvent(data)) return;

            GetProcessMethods(data.ProcessID).AddModule(data.FileName, data.ImageBase, data.ImageSize);

            //Console.WriteLine($"{data.ProcessID}.{data.ThreadID} --> {data.FileName}");
        }

        private void OnMethodDetails(MethodLoadUnloadVerboseTraceData data)
        {
            if (FilterOutEvent(data)) return;

            // care only about jitted methods
            if (!data.IsJitted) return;

            var method = GetProcessMethods(data.ProcessID)
                .Add(data.MethodStartAddress, data.MethodSize, data.MethodNamespace, data.MethodName, data.MethodSignature);

            Console.WriteLine($"0x{data.MethodStartAddress.ToString("x12")} - {data.MethodSize,6} | {data.MethodName}");
        }

        private MethodStore GetProcessMethods(int pid)
        {
            if (!_processes.Methods.TryGetValue(pid, out var methods))
            {
                methods = new MethodStore(pid);
                _processes.Methods[pid] = methods;
            }
            return methods;
        }


        private void OnSampleObjectAllocation(GCSampledObjectAllocationTraceData data)
        {
            if (FilterOutEvent(data)) return;

            var typeName = GetProcessTypeName(data.ProcessID, data.TypeID);
            if (data.TotalSizeForTypeSample >= 85000)
            {
                var message = $"{data.ProcessID}.{data.ThreadID} - {data.TimeStampRelativeMSec,12} | Alloc {GetProcessTypeName(data.ProcessID, data.TypeID)} ({data.TotalSizeForTypeSample})";
                Console.WriteLine(message);
            }
            GetProcessAllocations(data.ProcessID)
                .AddAllocation(
                data.ThreadID, 
                (ulong)data.TotalSizeForTypeSample, 
                (ulong)data.ObjectCountForTypeSample, 
                typeName
                );
        }

        private ProcessAllocations GetProcessAllocations(int pid)
        {
            if (!_processes.Allocations.TryGetValue(pid, out var allocations))
            {
                allocations = new ProcessAllocations(pid);
                _processes.Allocations[pid] = allocations;
            }
            return allocations;
        }

        private void OnClrStackWalk(ClrStackWalkTraceData data)
        {
            if (FilterOutEvent(data)) return;

            //var message = $"{data.ProcessID}.{data.ThreadID} - {data.TimeStampRelativeMSec,12} |       {data.FrameCount} frames";
            //Console.WriteLine(message);

            var callstack = BuildCallStack(data);
            GetProcessAllocations(data.ProcessID).AddStack(data.ThreadID, callstack);
            //DumpStack(data);
        }

        private AddressStack BuildCallStack(ClrStackWalkTraceData data)
        {
            var length = data.FrameCount;
            AddressStack stack = new AddressStack(length);

            // frame 0 is the last frame of the stack (i.e. last called method)
            for (int i = 0; i < length; i++)
            {
                stack.AddFrame(data.InstructionPointer(i));
            }

            return stack;
        }

        private void DumpStack(ClrStackWalkTraceData data)
        {
            var methods = GetProcessMethods(data.ProcessID);
            for (int i = 0; i < data.FrameCount; i++)
            {
                var address = data.InstructionPointer(i);
                Console.WriteLine(methods.GetFullName(address));
            }
            Console.WriteLine();
        }

        private void OnTypeBulkType(GCBulkTypeTraceData data)
        {
            if (FilterOutEvent(data)) return;

            ProcessTypeMapping mapping = GetProcessTypesMapping(data.ProcessID);
            for (int currentType = 0; currentType < data.Count; currentType++)
            {
                GCBulkTypeValues value = data.Values(currentType);
                mapping[value.TypeID] = value.TypeName;
            }
        }

        private ProcessTypeMapping GetProcessTypesMapping(int pid)
        {
            ProcessTypeMapping mapping;
            if (!_processes.Types.TryGetValue(pid, out mapping))
            {
                AssociateProcess(pid);

                mapping = new ProcessTypeMapping(pid);
                _processes.Types[pid] = mapping;
            }
            return mapping;
        }

        private void AssociateProcess(int pid)
        {
            try
            {
                _processes.Names[pid] = Process.GetProcessById(pid).ProcessName;
            }
            catch (Exception)
            {
                Console.WriteLine($"? {pid}");
                // we might not have access to the process
            }
        }

        private string GetProcessTypeName(int pid, ulong typeID)
        {
            if (!_processes.Types.TryGetValue(pid, out var mapping))
            {
                return typeID.ToString();
            }

            var name = mapping[typeID];
            return string.IsNullOrEmpty(name) ? typeID.ToString() : name;
        }

        private bool FilterOutEvent(TraceEvent data)
        {
            return (data.ProcessID == _currentPid);
        }
    }
}
