using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SampledObjectAllocationProfiler
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            ShowHeader();

            try
            {
                (bool noSampling, bool sortBySize, int topTypesLimit) parameters = GetParameters(args);
    
                TraceEventSession session = new TraceEventSession(
                    "SampledObjectAllocationMemoryProfilingSession",
                    TraceEventSessionOptions.Create
                    );
                var processes = new PerProcessProfilingState();
                var profiler = new SampledObjectAllocationMemoryProfiler(session, processes);
                var task = profiler.StartAsync(parameters.noSampling);

                Console.WriteLine("Press ENTER to stop memory profiling");
                Console.ReadLine();

                // this will exit the session.Process() call
                session.Dispose();

                try
                {
                    await task;
                    ShowResults(processes, parameters.sortBySize, parameters.topTypesLimit);

                    return 0;
                }
                catch (Exception x)
                {
                    Console.WriteLine(x.Message);
                    ShowHelp();
                }

                return 0;
            }
            catch (Exception x)
            {
                Console.WriteLine(x.Message);
                ShowHelp();
            }

            return -1;
        }

        private static (bool noSampling, bool sortBySize, int topTypesLimit) GetParameters(string[] args)
        {
            (bool noSampling, bool sortBySize, int topTypesLimit) parameters = (noSampling: false, sortBySize: true, topTypesLimit: -1);

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].ToLower() == "-a")
                {
                    parameters.noSampling = true;
                }
                else
                if (args[i].ToLower() == "-c")
                {
                    parameters.sortBySize = false;
                }
                else
                if (args[i].ToLower() == "-t")
                {
                    if (i+1 >= args.Length)
                    {
                        throw new InvalidOperationException($"Missing number after -t");
                    }

                    i++;
                    if (!int.TryParse(args[i], out var limit))
                    {
                        throw new InvalidOperationException($"{args[i]} is not a number after -t");
                    }

                    parameters.topTypesLimit = limit;
                }
                else
                {
                    throw new InvalidOperationException($"Unknown {args[i]} parameter...");
                }
            }

            return parameters;
        }

        private static void ShowResults(PerProcessProfilingState processes, bool sortBySize, int topTypesLimit)
        {
            foreach (var pid in processes.Allocations.Keys)
            {
                ShowResults(GetProcessName(pid, processes.Names), processes.Allocations[pid], sortBySize, topTypesLimit);
            }
        }

        private static string GetProcessName(int pid, Dictionary<int, string> names)
        {
            if (names.TryGetValue(pid, out var name))
                return name;

            return pid.ToString();
        }

        private static void ShowResults(string name, ProcessAllocationInfo allocations, bool sortBySize, int topTypesLimit)
        {
            Console.WriteLine($"Memory allocations for {name}");
            Console.WriteLine();
            Console.WriteLine("---------------------------------------------------------");
            Console.WriteLine("    Count        Size   Type");
            Console.WriteLine("---------------------------------------------------------");
            IEnumerable<AllocationInfo> types = (sortBySize)
                ? allocations.GetAllocations().OrderByDescending(a => a.Size)
                : allocations.GetAllocations().OrderByDescending(a => a.Count)
                ;
            if (topTypesLimit != -1)
                types = types.Take(topTypesLimit);

            foreach (var allocation in types)
            {
                Console.WriteLine($"{allocation.Count,9} {allocation.Size,11}   {allocation.TypeName}");
            }
            Console.WriteLine();
            Console.WriteLine();
        }

        private static void ShowHeader()
        {
            Console.WriteLine("SampledObjectAllocationProfiler v1.0.0 - Sampled memory profiler for .NET applications");
            Console.WriteLine("by Christophe Nasarre");
            Console.WriteLine();
        }
        private static void ShowHelp()
        {
            Console.WriteLine();
            Console.WriteLine("SampledObjectAllocationProfiler shows sampled allocations of a given .NET application.");
            Console.WriteLine("Usage: SampledObjectAllocationProfiler  [-a (all allocations)] [-c (sort by count instead of default by size)] [-t <type count (instead of all types by default)>]");
            Console.WriteLine("   Ex: SampledObjectAllocationProfiler -t 10     (top 50 sampled allocations sorted by size)");
            Console.WriteLine("   Ex: SampledObjectAllocationProfiler -a -t 10  (top 10 allocations sorted by count)");
            Console.WriteLine();
        }
    }
}
