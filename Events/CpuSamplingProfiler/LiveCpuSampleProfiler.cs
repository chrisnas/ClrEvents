using System;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace CpuSamplingProfiler
{
    public class LiveCpuSampleProfiler : CpuSampleProfilerBase
    {
        private TraceEventSession _session;
        private Task _profilingTask;

        public LiveCpuSampleProfiler() : base()
        {
        }

        protected override bool OnStart()
        {
            if (_session != null) throw new InvalidOperationException("Profiling session already started...");

            string sessionName = "LiveCpu_Profiling_Session+" + Guid.NewGuid().ToString();
            _session = new TraceEventSession(sessionName, TraceEventSessionOptions.Create);
            if (!EnableProviders(_session))
            {
                _session.Dispose();
                _session = null;
                return false;
            }

            _profilingTask = Task.Factory.StartNew(() =>
            {
                using (TraceLogEventSource source = TraceLog.CreateFromTraceEventSession(_session))
                {
                    // CPU sampling kernel events
                    source.Kernel.PerfInfoSample += (SampledProfileTraceData data) =>
                    {
                        if (data.ProcessID != Pid) return;

                        var callstack = data.CallStack();
                        if (callstack == null) return;

                        MergeCallStack(callstack, Reader);
                    };

                    // this call exits when the session is stopped
                    source.Process();
                }
            });

            return true;
        }

        protected override void OnStop()
        {
            if (_session == null) throw new InvalidOperationException("No profiling session to stop...");

            // 1. this will stop the profiling session (events stored in the etl file)
            _session.Dispose();
            _session = null;

            // wait for the event processing task that should exit as soon as the session is disposed
            _profilingTask.Wait(1000);
        }
    }
}
