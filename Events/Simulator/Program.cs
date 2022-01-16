using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime;
using System.Text;
using System.Threading;
using Simulator;

namespace EventTracing.Simulator
{
    class Program
    {
        private const string Invite =
                "\r\n" +
                "Enter an action x repetition count (ex: 2x10):\r\n" +
                "{0}" +
                "   x - exit" +
                ""
            ;

        private static readonly List<Tuple<string, Action>> Actions = new List<Tuple<string, Action>>()
        {
            new Tuple<string, Action>("trigger GC(0)", () => { GC.Collect(0); }),
            new Tuple<string, Action>("trigger GC(1)", () => { GC.Collect(1); }),
            new Tuple<string, Action>("trigger GC(2)", () => { GC.Collect(2); }),
            new Tuple<string, Action>("allocate 10.000 bytes", () => { Allocate(10000); }),
            new Tuple<string, Action>("allocate 200.000 bytes", () => { Allocate(200000); }),
            new Tuple<string, Action>("throw random exceptions", ExceptionAction.ThrowFirstChanceExceptions),
            new Tuple<string, Action>("create thead that waits 10 seconds", () =>
            {
                Thread t = new Thread(() => { Thread.Sleep(10000);});
                t.Start();
            }),
            new Tuple<string, Action>("create thead and abort it after 2 seconds", () =>
            {
                Thread t = new Thread(() => { Thread.Sleep(10000);});
                t.Start();
                Thread.Sleep(2000);
                t.Abort();
            }),
            new Tuple<string, Action>("start ThreadPool QueueUserWorkItems", () => { ThreadPoolAction.Run();  }),
            new Tuple<string, Action>("create ThreadPool I/O", () => { IOThreadPoolAction.Run();  }),
            new Tuple<string, Action>("start contention", () => { MultiRunner.Start(64, ContentionAction.Run, 1);  }),
            new Tuple<string, Action>("start random allocation", () => { MultiRunner.Start(64, RandomAllocationAction.Run, 1);  }),
            new Tuple<string, Action>("stop current action", MultiRunner.Stop),
        };

        private static readonly int LastAction = Actions.Count - 1;


        static void Main(string[] args)
        {
            if (GCSettings.IsServerGC)
                Console.WriteLine("GC - Server");
            else
                Console.WriteLine("GC - Workstation");

            string invite = ComputeInvite();
            while (true)
            {
                Console.WriteLine(invite);

                var choice = Console.ReadLine();
                if (!ParseAction(choice))
                    break;

                OutputStats();
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static string ComputeInvite()
        {
            StringBuilder sb = new StringBuilder(1024);


            sb.AppendFormat("PID={0}\r\n", Process.GetCurrentProcess().Id);
            for (int currentAction = 0; currentAction < Actions.Count; currentAction++)
            {
                var action = Actions[currentAction];
                sb.AppendFormat("   {0} - {1}\r\n", currentAction, action.Item1);
            }

            return string.Format(Invite, sb.ToString());
        }

        private static bool ParseAction(string result)
        {
            if (string.IsNullOrEmpty(result))
                return true;

            result = result.ToLower();
            if (result == "x")
                return false;

            int action;
            int repetitionCount = 1;
            var parts = result.Split('x');

            if (!int.TryParse(parts[0], out action))
                return true;
            if (parts.Length == 2)
            {
                if (!int.TryParse(parts[1], out repetitionCount))
                    return true;
            }

            ExecuteAction(action, repetitionCount);

            return true;
        }

        private static void ExecuteAction(int action, int repetitionCount)
        {
            if (action > LastAction)
                return;

            for (var i = 0; i < repetitionCount; i++)
            {
                Actions[action].Item2();
            }
        }

        private static void OutputStats()
        {
            Console.WriteLine("# Gen0={0}\r\n# Gen1={1}\r\n# Gen2={2}\r\nMemory={3}",
                GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
                GC.GetTotalMemory(false)
            );
        }


        private static void Allocate(int size)
        {
            var buffer = new byte[size];
        }

    }

}
