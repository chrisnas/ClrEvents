﻿// EventPipe are used if ETW is not defined
//#define ETW

#if ETW
using Microsoft.Diagnostics.Tracing.Session;
#endif
using System;
using System.Threading.Tasks;
using Shared;

namespace ConsoleListener
{
    class Program
    {
        // ---------------------------------------------------------------------------------------------------------------
        // To debug this application, go to Project Properties | Debug tab and set the PID of the application to monitor
        // in "command line argument" text box.
        // The simulator application could be used for testing
        // ---------------------------------------------------------------------------------------------------------------
        //
        static void Main(string[] args)
        {
            // filter on process if any
            int pid = 100060;
            if (args.Length == 1)
            {
                int.TryParse(args[0], out pid);
            }

#if ETW
            // ETW implementation
            string sessionName = "EtwSessionForCLR_" + Guid.NewGuid().ToString();
            Console.WriteLine($"Starting {sessionName}...\r\n");
            using (TraceEventSession userSession = new TraceEventSession(sessionName, TraceEventSessionOptions.Create))
            {
                Task.Run(() =>
                {
                    // don't want allocation ticks by default because it might have a noticeable impact
                    ClrEventsManager manager = new ClrEventsManager(userSession, pid, EventFilter.All & ~EventFilter.AllocationTick);
                    RegisterEventHandlers(manager);

                    // this is a blocking call until the session is disposed
                    manager.ProcessEvents();
                    Console.WriteLine("End of CLR event processing");
                });

                // wait for the user to dismiss the session
                Console.WriteLine("Press ENTER to exit...");
                Console.ReadLine();
            }
#else

            // EventPipe implementation
            Console.WriteLine($"Starting event pipe session...");
            Task.Run(() =>
            {
                // don't want allocation ticks by default because it might have a noticeable impact
                //ClrEventsManager manager = new ClrEventsManager(pid, EventFilter.All & ~EventFilter.AllocationTick);
                //ClrEventsManager manager = new ClrEventsManager(pid, EventFilter.All, isLogging:true);
                //ClrEventsManager manager = new ClrEventsManager(pid, EventFilter.Network, isLogging: true);
                ClrEventsManager manager = new ClrEventsManager(pid, EventFilter.Contention, isLogging: true);
                RegisterEventHandlers(manager);

                // this is a blocking call until the session is disposed
                manager.ProcessEvents();
                Console.WriteLine("End of CLR event processing");
            });

            // wait for the user to dismiss the session
            Console.WriteLine("Press ENTER to exit...");
            Console.ReadLine();
#endif
        }

        private static void RegisterEventHandlers(ClrEventsManager manager)
        {
            manager.FirstChanceException += OnFirstChanceException;
            manager.Finalize += OnFinalize;
            manager.Contention += OnContention;
            manager.ThreadPoolStarvation += OnThreadPoolStarvation;
            manager.GarbageCollection += OnGarbageCollection;
            manager.AllocationTick += OnAllocationTick;
        }

        private static void OnThreadPoolStarvation(object sender, ThreadPoolStarvationArgs e)
        {
            Console.WriteLine($"[{e.ProcessId,7}] + #{e.WorkerThreadCount} workers");
        }

        private static void OnContention(object sender, ContentionArgs e)
        {
            if (e.IsManaged)
            {
                if (e.Duration.TotalMilliseconds > 0)
                {
                    Console.WriteLine($"[{e.ProcessId,7}.{e.ThreadId,7}] | {e.Duration.TotalMilliseconds} ms");
                    if (e.Callstack != null)
                    {
                        for (int i = 0; i < e.Callstack.Count; i++)
                        {
                            Console.WriteLine($"    {e.Callstack[i]}");
                        }
                    }
                }
            }
        }

        private static void OnFirstChanceException(object sender, ExceptionArgs e)
        {
            Console.WriteLine($"[{e.ProcessId,7}] --> {e.TypeName} : {e.Message}");
        }

        private static void OnFinalize(object sender, FinalizeArgs e)
        {
            string finalizedType = string.IsNullOrEmpty(e.TypeName) ? "#" + e.TypeId.ToString() : e.TypeName;
            Console.WriteLine($"[{e.ProcessId,7}] ~{finalizedType}");
        }
        private static void OnGarbageCollection(object sender, GarbageCollectionArgs e)
        {
            Console.WriteLine($"[{e.ProcessId,7}] g {e.Generation} #{e.Number} suspension={e.SuspensionDuration:0.00}ms | compacting={e.IsCompacting} ({e.Type} - {e.Reason})");
            Console.WriteLine($"               gen0: {e.Gen0Size,12}  gen1: {e.Gen1Size,12}  gen2: {e.Gen2Size,12}  loh: {e.LOHSize,12}");
        }

        private static void OnAllocationTick(object sender, AllocationTickArgs e)
        {
            int k = (int)e.AllocationKind;
            var kind =  (k == 0) ? "Small" : (k == 1) ? "Large" : "Pinned";
            Console.WriteLine($"[{e.ProcessId,7}] {kind, 7} | {e.AllocationAmount64,9} - {e.TypeName}");
        }
    }
}
