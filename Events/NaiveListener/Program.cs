using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System.Diagnostics;

namespace NaiveListener
{
    class Program
    {
        private static int s_pid;

        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                int.TryParse(args[0], out s_pid);
            }
            if (s_pid == 0)
            {
                s_pid = 16484;
            }

            // list ETW sessions
            Console.WriteLine("Current ETW sessions:");
            foreach (var session in TraceEventSession.GetActiveSessionNames())
            {
                Console.WriteLine(session);
            }
            Console.WriteLine("--------------------------------------------");

            string sessionName = "EtwSessionForCLR_" + Guid.NewGuid().ToString();
            Console.WriteLine($"Starting {sessionName}...\r\n");
            using (TraceEventSession userSession = new TraceEventSession(sessionName, TraceEventSessionOptions.Create))
            {
                Task.Run(() =>
                {
                    // register handlers for events on the session source

                    // listen to all CLR events
                    userSession.Source.Clr.All += delegate (TraceEvent data)
                    {
                        // skip verbose and unneeded events
                        if (SkipEvent(data))
                            return;

                        // raw dump of the events
                        Console.WriteLine($"{data.ProcessID,7} <{data.ProviderName}>\r\n   ___[{data.ID}={(int)data.Opcode}|{data.OpcodeName}] {data.EventName} <| {data.GetType().Name}\r\n");
                        Console.WriteLine();
                    };

                    //userSession.Source.Clr.AppDomainResourceManagementThreadCreated += delegate (ThreadCreatedTraceData data)
                    //{
                    //    Process process = Process.GetProcessById(s_pid);
                    //    if (s_pid != data.ProcessID) return;
                    //    Console.WriteLine($"^ {process.Threads.Count}");
                    //};

                    //userSession.Source.Clr.AppDomainResourceManagementThreadTerminated += delegate (ThreadTerminatedOrTransitionTraceData data)
                    //{
                    //    Process process = Process.GetProcessById(s_pid);
                    //    if (s_pid != data.ProcessID) return;
                    //    Console.WriteLine($"v {process.Threads.Count}");
                    //};

                    //userSession.Source.Clr.ThreadPoolWorkerThreadAdjustmentAdjustment += delegate (ThreadPoolWorkerThreadAdjustmentTraceData data)
                    //{
                    //    Process process = Process.GetProcessById(s_pid);
                    //    if (s_pid != data.ProcessID) return;
                    //    Console.WriteLine($". {process.Threads.Count} | {data.NewWorkerThreadCount} threads due to {data.Reason.ToString()}");
                    //};

                    //userSession.Source.Clr.ThreadPoolWorkerThreadStart += delegate (ThreadPoolWorkerThreadTraceData data)
                    //{
                    //    Process process = Process.GetProcessById(s_pid);
                    //    if (s_pid != data.ProcessID) return;
                    //    Console.WriteLine($"+ {process.Threads.Count} | {data.ActiveWorkerThreadCount + data.RetiredWorkerThreadCount} = {data.ActiveWorkerThreadCount} + {data.RetiredWorkerThreadCount} threads");
                    //};

                    //userSession.Source.Clr.ThreadPoolWorkerThreadStop += delegate (ThreadPoolWorkerThreadTraceData data)
                    //{
                    //    Process process = Process.GetProcessById(s_pid);
                    //    if (s_pid != data.ProcessID) return;
                    //    Console.WriteLine($"- {process.Threads.Count} | {data.ActiveWorkerThreadCount + data.RetiredWorkerThreadCount}  = {data.ActiveWorkerThreadCount} + {data.RetiredWorkerThreadCount} threads");
                    //};

                    //userSession.Source.Clr.ThreadPoolIOEnqueue += delegate (ThreadPoolIOWorkEnqueueTraceData data)
                    //{
                    //    Process process = Process.GetProcessById(s_pid);
                    //    Console.WriteLine($"e {process.Threads.Count}");
                    //};

                    //userSession.Source.Clr.ThreadPoolIODequeue += delegate (ThreadPoolIOWorkTraceData data)
                    //{
                    //    Process process = Process.GetProcessById(s_pid);
                    //    Console.WriteLine($"d {process.Threads.Count}");
                    //};

                    //userSession.Source.Clr.IOThreadCreationStart += delegate (IOThreadTraceData data)
                    //{
                    //    Process process = Process.GetProcessById(s_pid);
                    //    if (s_pid != data.ProcessID) return;
                    //    Console.WriteLine($"$ {process.Threads.Count} | {data.IOThreadCount + data.RetiredIOThreads}  = {data.IOThreadCount} + {data.RetiredIOThreads} threads");
                    //};

                    //userSession.Source.Clr.IOThreadCreationStop += delegate (IOThreadTraceData data)
                    //{
                    //    Process process = Process.GetProcessById(s_pid);
                    //    if (s_pid != data.ProcessID) return;
                    //    Console.WriteLine($"# {process.Threads.Count} | {data.IOThreadCount + data.RetiredIOThreads}  = {data.IOThreadCount} + {data.RetiredIOThreads} threads");
                    //};


                    //userSession.Source.Dynamic.All += delegate (TraceEvent data)
                    //{
                    //    // skip verbose and unneeded events
                    //    if (SkipEvent(data))
                    //        return;

                    //    // raw dump of the events
                    //    //Console.WriteLine($"{data.ProcessID,7} <{data.ProviderName}:{data.ID}>\r\n   ___[{(int)data.Opcode}|{data.OpcodeName}] {data.EventName} <| {data.GetType().Name}\r\n");
                    //    //Console.WriteLine($"\r\n   ...[{(int)data.Opcode}|{data.OpcodeName}] {data.EventName} <| {data.GetType().Name}");
                    //};

                    // decide which provider to listen to with filters if needed
                    userSession.EnableProvider(
                        ClrTraceEventParser.ProviderGuid,  // CLR provider
                        TraceEventLevel.Verbose,
                        (ulong)(
                            //ClrTraceEventParser.Keywords.AppDomainResourceManagement | // thread termination event
                            //ClrTraceEventParser.Keywords.Contention |           // thread contention timing
                            //ClrTraceEventParser.Keywords.Threading |            // threadpool events
                            //ClrTraceEventParser.Keywords.Exception |            // get the first chance exceptions
                            //ClrTraceEventParser.Keywords.GCHeapAndTypeNames |   // for finalizer and exceptions type names
                            //ClrTraceEventParser.Keywords.Type |                 // for finalizer and exceptions type names
                            ClrTraceEventParser.Keywords.GC                     // garbage collector details
                        )
                    );

                    // this is a blocking call until the session is disposed
                    userSession.Source.Process();
                    Console.WriteLine("End of session");
                });

                // wait for the user to dismiss the session
                Console.WriteLine("Presse ENTER to exit...");
                Console.ReadLine();
            }
        }

        private static bool SkipEvent(TraceEvent data)
        {
            if (data.ProcessID != s_pid) return true;

            if ((data.TaskName != "GC"))
            {
                return true;
            }

            return
                (data.Opcode == (TraceEventOpcode)202) ||
                (data.Opcode == (TraceEventOpcode)203) ||
                (data.Opcode == (TraceEventOpcode)11) ||
                (data.Opcode == (TraceEventOpcode)32) ||
                (data.Opcode == (TraceEventOpcode)15) ||
                (data.Opcode == (TraceEventOpcode)19) 
                //(data.Opcode == (TraceEventOpcode)10) ||
                //(data.Opcode == (TraceEventOpcode)21) ||
                //(data.Opcode == (TraceEventOpcode)22) ||
                //(data.Opcode == (TraceEventOpcode)23) ||
                //(data.Opcode == (TraceEventOpcode)24) ||
                //(data.Opcode == (TraceEventOpcode)25) ||
                //(data.Opcode == (TraceEventOpcode)27) ||
                //(data.Opcode == (TraceEventOpcode)38) ||
                //(data.Opcode == (TraceEventOpcode)32) ||
                //(data.Opcode == (TraceEventOpcode)33) ||
                //(data.Opcode == (TraceEventOpcode)34) ||
                //(data.Opcode == (TraceEventOpcode)36) ||
                //(data.Opcode == (TraceEventOpcode)39) ||
                //(data.Opcode == (TraceEventOpcode)40) ||
                //(data.Opcode == (TraceEventOpcode)82)
                ;

            //return false;
        }
    }
}
