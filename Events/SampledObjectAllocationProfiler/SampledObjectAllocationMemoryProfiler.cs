using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using System;
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
            session.EnableKernelProvider(
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

            session.EnableProvider(
                ClrTraceEventParser.ProviderGuid,
                TraceEventLevel.Informational,    // this is needed in order to receive GCSampledObjectAllocation event
                (ulong)(

                eventsKeyword |

                // required to receive the BulkType events that allows 
                // mapping between the type ID received in the allocation events
                ClrTraceEventParser.Keywords.GCHeapAndTypeNames |
                ClrTraceEventParser.Keywords.Type |

                // this is mandatory to get the callstacks in each CLR event payload.
                ClrTraceEventParser.Keywords.Stack
                )
            );
        }

        private void SetupListeners(ETWTraceEventSource source)
        {
            // register for high and low keyword
            // if both are set, each allocation will trigger an event (beware performance issues...)
            source.Clr.GCSampledObjectAllocation += OnSampleObjectAllocation;

            // required to receive the mapping between type ID (received in GCSampledObjectAllocation)
            // and their name (received in TypeBulkType)
            source.Clr.TypeBulkType += OnTypeBulkType;

            // not supported for Linux
            //source.Kernel.ProcessStart += OnProcessStart;
        }

        private void OnProcessStart(ProcessTraceData data)
        {
            // not supported for Linux
            //Console.WriteLine($"+ {data.ImageFileName}");
            //_processes.Names[data.ProcessID] = data.ImageFileName;
        }

        private void OnSampleObjectAllocation(GCSampledObjectAllocationTraceData data)
        {
            if (FilterOutEvent(data)) return;

            GetProcessAllocations(data.ProcessID)
                .AddAllocation(
                    (ulong)data.TotalSizeForTypeSample, 
                    (ulong)data.ObjectCountForTypeSample, 
                    GetProcessTypeName(data.ProcessID, data.TypeID)
                    );
        }

        private ProcessAllocationInfo GetProcessAllocations(int pid)
        {
            if (!_processes.Allocations.TryGetValue(pid, out var allocations))
            {
                allocations = new ProcessAllocationInfo(pid);
                _processes.Allocations[pid] = allocations;
            }
            return allocations;
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
            return data.ProcessID == _currentPid;
        }
    }
}
