using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Session;

namespace AllocationTickProfiler
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            ShowHeader();

            (int pid, bool verbose) parameters;
            try
            {
                parameters = GetParameters(args);
            }
            catch (InvalidOperationException x)
            {
                Console.WriteLine(x.Message);
                ShowHelp();
                return -1;
            }

            if (parameters.verbose)
            {
                Console.WriteLine($"Current pid = {Process.GetCurrentProcess().Id}");
                Console.WriteLine($"Profiling process #{parameters.pid}...");
                Console.WriteLine();
            }

            var allocations = new ProcessAllocationInfo(parameters.pid);
            TraceEventSession session = new TraceEventSession(
                "AllocationTickMemoryProfilingSession",
                TraceEventSessionOptions.Create
                );
            var profiler = new AllocationTickMemoryProfiler(session, parameters.pid, allocations, parameters.verbose);
            var task = profiler.StartAsync();

            Console.WriteLine("Press ENTER to stop memory profiling");
            Console.WriteLine();
            Console.ReadLine();

            // this will exit the session.Process() call
            session.Dispose();

            try
            {
                await task;

                ShowResults(allocations);
                return 0;
            }
            catch (Exception x)
            {
                Console.WriteLine(x.Message);
                ShowHelp();
            }

            return -666;
        }

        private static (int pid, bool verbose) GetParameters(string[] args)
        {
            if (args.Length < 1)
            {
                throw new InvalidOperationException("Missing process ID");
            }

            (int pid, bool verbose) parameters = (pid: -1, verbose: false);
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].ToLower() == "-v")
                {
                    parameters.verbose = true;
                }
                else
                if (!int.TryParse(args[i], out parameters.pid))
                {
                    throw new InvalidOperationException($"{args[i]} is not a numeric pid");
                }
            }

            if (parameters.pid == -1)
            {
                throw new InvalidOperationException("Missing process ID");
            }

            return parameters;
        }

        private static void ShowResults(ProcessAllocationInfo allocations)
        {
            Console.WriteLine("  Small   Large         LOH  Type");
            Console.WriteLine("---------------------------------------------------------");
            foreach (var allocation in allocations.GetAllocations().OrderByDescending(a => a.Count))
            {
                var smallCount = (allocation.SmallCount == 0) ? "       " : $"{allocation.SmallCount,7}";
                var largeCount = (allocation.LargeCount == 0) ? "       " : $"{allocation.LargeCount,7}";
                var largeSize = (allocation.LargeSize == 0) ? "          " : $"{allocation.LargeSize,10}";
                Console.WriteLine($"{smallCount} {largeCount}  {largeSize}  {allocation.TypeName}");
            }
        }

        private static void ShowHeader()
        {
            Console.WriteLine("AllocationTickProfiler v1.0.0 - Sampled memory profiler for .NET applications");
            Console.WriteLine("by Christophe Nasarre");
            Console.WriteLine();
        }
        private static void ShowHelp()
        {
            Console.WriteLine();
            Console.WriteLine("AllocationTickProfiler shows sampled allocations of a given .NET application.");
            Console.WriteLine("Usage: AllocationTickProfiler <pid> [-v]");
            Console.WriteLine("   Ex: AllocationTickProfiler 1234     show summary at the end of profiling");
            Console.WriteLine("   Ex: AllocationTickProfiler 1234 -v  also show each sampled allocation details");
            Console.WriteLine();
        }
    }
}
