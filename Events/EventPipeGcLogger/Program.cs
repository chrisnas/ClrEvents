using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Threading.Tasks;
using GcLog;

namespace EventPipeGcLogger
{
    class Program
    {
        static void Main(string[] args)
        {
            if (GCSettings.IsServerGC)
                Console.WriteLine("BACKGROUND");
            else
                Console.WriteLine("WORKSTATION");

            var pid = Process.GetCurrentProcess().Id;
            Console.WriteLine($"pid = {pid}");

            // create the EventPipe-based GC logger
            var gcLog = EventPipeGcLog.GetLog(pid);

            var filename = GetUniqueFilename(pid);
            try
            {
                gcLog.Start(filename);

                Console.WriteLine("Press ENTER to start allocating");
                Console.ReadLine();

                Task.Run(() =>
                {
                    const int MAX_CACHED_ALLOCATIONS = 2048;

                    Random r = new Random(Environment.TickCount);
                    var cache = new List<byte[]>(MAX_CACHED_ALLOCATIONS);

                    while (true)
                    {
                        // a few allocations should end up into the LOH
                        var memory = new byte[(r.Next(87)+1) * 1024];

                        if (cache.Count == MAX_CACHED_ALLOCATIONS)
                        {
                            cache.Clear();
                        }
                        cache.Add(memory);

                        Task.Yield();
                        //Task.Delay(50).Wait();
                    }
                });

                Console.WriteLine("Press ENTER to exit...");
                Console.ReadLine();

            }
            finally
            {
                gcLog.Stop();
            }

            Environment.Exit(0);
        }

        private static string GetUniqueFilename(int pid)
        {
            var now = DateTime.Now;
            string filename = Path.Combine(Environment.CurrentDirectory,
                $"{pid.ToString()}_{now.Year}{now.Month}{now.Day}_{now.Hour}{now.Minute}{now.Second}.csv"
            );
            return filename;
        }

    }
}
