using System;
using System.IO;
using GcLog;

namespace GcLogger
{
    class Program
    {
        static void Main(string[] args)
        {
            int pid = -1;
            if (args.Length != 1)
            {
                Console.WriteLine("Missing process ID...");
                return;
            }

            if (!int.TryParse(args[0], out pid))
            {
                Console.WriteLine("Process ID must be a number...");
                return;
            }

            EtwGcLog gcLog = EtwGcLog.GetProcessGcLog(pid);
            if (gcLog == null)
            {
                Console.WriteLine($"Process {pid} is not running...");
                return;
            }

            var filename = GetUniqueFilename(pid);
            gcLog.Start(filename);

            Console.WriteLine("Press ENTER to stop collecting GC events.");
            Console.ReadLine();

            gcLog.Stop();
            Console.WriteLine($"Press ENTER to exit and look for {filename} GC log file.");
            Console.ReadLine();
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
