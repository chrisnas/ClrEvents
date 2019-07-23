using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using ClrCounters;
using Microsoft.Diagnostics.Tracing.Session;

namespace GcLog
{
    public class EtwGcLog : GcLogBase
    {
        // TODO: don't forget to update the header if you are adding more columns 
        private const string Header =
            "StartRelativeMSec,Number,Generation,Type,Reason,IsCompacting,SuspensionDurationInMilliSeconds,PauseDurationInMilliSeconds,FinalPauseDurationInMilliSeconds,Gen0Size,Gen1Size,Gen2Size,LOHSize,ObjGen0Before,ObjGen1Before,ObjGen2Before,ObjLOHBefore,ObjGen0After,ObjGen1After,ObjGen2After,ObjLOHAfter";

        private int _pid;
        private TraceEventSession _userSession;
        private StringBuilder _line = new StringBuilder(2048);

        private EtwGcLog(int PID)
        {
            _pid = PID;
        }

        public static EtwGcLog GetProcessGcLog(int pid)
        {
            EtwGcLog gcLog = null;
            try
            {
                var process = Process.GetProcessById(pid);
                process.Dispose();

                gcLog = new EtwGcLog(pid);
            }
            catch (System.ArgumentException)
            {
                // there is no running process with the given pid
            }

            return gcLog;
        }

        protected override void OnStart()
        {
            string sessionName = $"GcLogEtwSession_{_pid.ToString()}_{Guid.NewGuid().ToString()}";
            Console.WriteLine($"Starting {sessionName}...\r\n");
            _userSession = new TraceEventSession(sessionName, TraceEventSessionOptions.Create);

            Task.Run(() =>
            {
                // only want to receive GC event
                ClrEventsManager manager = new ClrEventsManager(_userSession, _pid, EventFilter.GC);
                manager.GarbageCollection += OnGarbageCollection;

                // this is a blocking call until the session is disposed
                manager.ProcessEvents();
                Console.WriteLine("End of CLR event processing");
            });

            // add a header to the .csv file
            WriteLine(Header);
        }

        protected override void OnStop()
        {
            // when the session is disposed, the call to ProcessEvents() returns
            _userSession.Dispose();
        }

        private void OnGarbageCollection(object sender, GarbageCollectionArgs e)
        {
            _line.Clear();
            _line.AppendFormat("{0},", e.StartRelativeMSec.ToString());
            _line.AppendFormat("{0},", e.Number.ToString());
            _line.AppendFormat("{0},", e.Generation.ToString());
            _line.AppendFormat("{0},", e.Type);
            _line.AppendFormat("{0},", e.Reason);
            _line.AppendFormat("{0},", e.IsCompacting.ToString());
            _line.AppendFormat("{0},", e.SuspensionDuration.ToString());
            _line.AppendFormat("{0},", e.PauseDuration.ToString());
            _line.AppendFormat("{0},", e.BGCFinalPauseDuration.ToString());
            _line.AppendFormat("{0},", e.Gen0Size.ToString());
            _line.AppendFormat("{0},", e.Gen1Size.ToString());
            _line.AppendFormat("{0},", e.Gen2Size.ToString());
            _line.AppendFormat("{0},", e.LOHSize.ToString());
            _line.AppendFormat("{0},", e.ObjSizeBefore[0].ToString());
            _line.AppendFormat("{0},", e.ObjSizeBefore[1].ToString());
            _line.AppendFormat("{0},", e.ObjSizeBefore[2].ToString());
            _line.AppendFormat("{0},", e.ObjSizeBefore[3].ToString());
            _line.AppendFormat("{0},", e.ObjSizeAfter[0].ToString());
            _line.AppendFormat("{0},", e.ObjSizeAfter[1].ToString());
            _line.AppendFormat("{0},", e.ObjSizeAfter[2].ToString());
            _line.AppendFormat("{0}", e.ObjSizeAfter[3].ToString());

            WriteLine(_line.ToString());
        }
    }
}
