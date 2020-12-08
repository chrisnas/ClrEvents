using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Linq;

namespace CpuSamplingProfiler
{
    public class EtlCpuSampleProfiler : CpuSampleProfilerBase
    {
        private string _filename;
        private TraceEventSession _session;

        public EtlCpuSampleProfiler(string filename) : base()
        {
            if (string.IsNullOrEmpty(filename)) throw new ArgumentNullException(nameof(filename), "Required trace filename...");
            _filename = filename;
        }

        protected override bool OnStart()
        {
            string sessionName = "EtlCpu_Profiling_Session+" + Guid.NewGuid().ToString();
            _session = new TraceEventSession(sessionName, _filename);

            return EnableProviders(_session);
        }

        protected override void OnStop()
        {
            if (_session == null) throw new InvalidOperationException("No profiling session to stop...");

            // 1. this will stop the profiling session (events stored in the etl file)
            _session.Dispose();
            _session = null;

            // 2. read the profiling events from the recording: an .etlx file should be created 
            var traceLog = TraceLog.OpenOrConvert(
                    _filename,
                    new TraceLogOptions() { ConversionLog = SymbolMessages }
                    );
            Console.WriteLine(SymbolMessages.ToString());

            var profiledProcess = traceLog.Processes.FirstOrDefault(tp => tp.ProcessID == Pid);
            if (profiledProcess == null)
            {
                Console.WriteLine($"No process {Pid} in the trace...");
                return;
            }

            // 3. parse profiling kernel events
            // from https://github.com/microsoft/perfview/blob/master/src/TraceEvent/Samples/41_TraceLogMonitor.cs#L150
            // from https://docs.microsoft.com/en-us/windows/win32/etw/perfinfo
            // from https://github.com/microsoft/perfview/blob/master/src/TraceEvent/Parsers/KernelTraceEventParser.cs#L3128 
            // and https://github.com/microsoft/perfview/blob/master/src/TraceEvent/Parsers/KernelTraceEventParser.cs#L2298
            //
            Guid perfInfoTaskGuid = new Guid(0xce1dbfb4, 0x137e, 0x4da6, 0x87, 0xb0, 0x3f, 0x59, 0xaa, 0x10, 0x2c, 0xbc);
            int profileOpcode = 46;
            foreach (var data in traceLog.Events)
            {
                if (data.ProcessID != Pid) continue;
                if (data.TaskGuid != perfInfoTaskGuid) continue;
                if ((uint)data.Opcode != profileOpcode) continue;

                var callstack = data.CallStack();
                if (callstack == null) continue;

                // could keep stacks on a per thread basis (data.ThreadID)
                // but usually in perfview I use Thread -> AllThreads
                // because for server application processing is handled 
                // by any thread from the ThreadPool

                MergeCallStack(callstack, Reader);
            }

            // 4. TODO: delete the .etl file (only the .etlx file is meaningful to open in Perfview)
        }
    }
}
