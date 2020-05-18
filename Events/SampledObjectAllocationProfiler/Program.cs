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

                using (var processes = new PerProcessProfilingState())
                { 
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
                }

                return -1;
            }
            catch (Exception x)
            {
                Console.WriteLine(x.Message);
                ShowHelp();
            }

            return -2;
        }

        private static (bool noSampling, bool sortBySize, int topTypesLimit) GetParameters(string[] args)
        {
            (bool noSampling, bool sortBySize, int topTypesLimit) parameters = (noSampling: false, sortBySize: true, topTypesLimit: 3);

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
                // skip processes without symbol resolution
                if (!processes.Methods.ContainsKey(pid)) continue;

                ShowResults(GetProcessName(pid, processes.Names), processes.Methods[pid], processes.Allocations[pid], sortBySize, topTypesLimit);
            }
        }

        private static string GetProcessName(int pid, Dictionary<int, string> names)
        {
            if (names.TryGetValue(pid, out var name))
                return name;

            return pid.ToString();
        }

        private static void ShowResults(string name, MethodStore methods, ProcessAllocations allocations, bool sortBySize, int topTypesLimit)
        {
            Console.WriteLine($"Memory allocations for {name}");
            Console.WriteLine();
            Console.WriteLine("---------------------------------------------------------");
            Console.WriteLine("    Count        Size   Type");
            Console.WriteLine("---------------------------------------------------------");
            IEnumerable<AllocationInfo> types = (sortBySize)
                ? allocations.GetAllAllocations().OrderByDescending(a => a.Size)
                : allocations.GetAllAllocations().OrderByDescending(a => a.Count)
                ;
            if (topTypesLimit != -1)
                types = types.Take(topTypesLimit);

            foreach (var allocation in types)
            {
                Console.WriteLine($"{allocation.Count,9} {allocation.Size,11}   {allocation.TypeName}");
                Console.WriteLine();
                DumpStacks(allocation, methods);
                Console.WriteLine();
            }
            Console.WriteLine();
            Console.WriteLine();
        }

        private static void DumpStacks(AllocationInfo allocation, MethodStore methods)
        {
            var stacks = allocation.Stacks.OrderByDescending(s => s.Count).Take(10);
            foreach (var stack in stacks)
            {
                Console.WriteLine($"{stack.Count,6} allocations");
                Console.WriteLine("----------------------------------");
                DumpStack(stack.Stack, methods);
                Console.WriteLine();
            }
        }

        private static void DumpStack(AddressStack stack, MethodStore methods)
        {
            var callstack = stack.Stack;
            for (int i = 0; i < Math.Min(10, callstack.Count); i++)
            {
                Console.WriteLine($"       {methods.GetFullName(callstack[i])}");
            }
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
            Console.WriteLine("Usage: SampledObjectAllocationProfiler  [-a (all allocations)] [-c (sort by count instead of default by size)] [-t <type count (instead of 3 types by default)>]");
            Console.WriteLine("   Ex: SampledObjectAllocationProfiler -t -1     (all types sampled allocations sorted by size)");
            Console.WriteLine("   Ex: SampledObjectAllocationProfiler -c -t 10  (allocations for top 10 types sorted by count)");
            Console.WriteLine();
        }
    }
}
