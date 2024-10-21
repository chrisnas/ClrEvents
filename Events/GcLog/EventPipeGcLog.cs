using System;
using System.Text;
using Shared;

namespace GcLog
{
    public class EventPipeGcLog : GcLogBase
    {
        // TODO: don't forget to update the header if you are adding more columns
        private const string Header =
            "StartRelativeMSec,Number,Generation,Type,Reason,IsCompacting,SuspensionDurationInMilliSeconds,PauseDurationInMilliSeconds,FinalPauseDurationInMilliSeconds,Gen0Size,Gen1Size,Gen2Size,LOHSize,ObjGen0Before,ObjGen1Before,ObjGen2Before,ObjLOHBefore,ObjGen0After,ObjGen1After,ObjGen2After,ObjLOHAfter";

        private int _pid;
        private GcEventListener _listener;
        private StringBuilder _line = new StringBuilder(2048);

        private EventPipeGcLog(int PID)
        {
            _pid = PID;
        }

        public static EventPipeGcLog GetLog(int pid)
        {
            return new EventPipeGcLog(pid);
        }


        protected override void OnStart()
        {
            if (_listener != null)
                throw new InvalidOperationException("Already started");

            _listener = new GcEventListener();

            _listener.GcEvents += OnGc;
            WriteLine(Header);
        }

        protected override void OnStop()
        {
            if (_listener == null)
                throw new InvalidOperationException("Can't stop if not started");

            _listener.Stop();
            _listener = null;
        }

        private void OnGc(object sender, GarbageCollectionArgs e)
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
