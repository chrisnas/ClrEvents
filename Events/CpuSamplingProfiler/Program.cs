using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace CpuSamplingProfiler
{
    class Program
    {
        static int Main(string[] args)
        {
            ShowHeader();

            var parameters = GetParameters(args);
            if (parameters.pid == -1)
            {
                Console.WriteLine("Missing process ID...");
                ShowHelp();
                return -1;
            }

            ICpuSampleProfiler profiler;
            if (parameters.generateEtlxFile)
                profiler = new EtlCpuSampleProfiler($"trace-{parameters.pid}.etl");
            else
                profiler = new LiveCpuSampleProfiler();

            if (!profiler.Start(parameters.pid))
            {
                Console.WriteLine("Failed to start profiling session...");
                ShowHelp();
                return -1;
            }

            Console.WriteLine("Press ENTER to stop CPU profiling");
            Console.WriteLine();
            Console.ReadLine();

            profiler.Stop();
            ShowResults(profiler);

            return 0;
        }


        private static void ShowResults(ICpuSampleProfiler profiler)
        {
            var visitor = new ConsoleRenderer();
            Console.WriteLine();
            foreach (var stack in profiler.Stacks.Stacks.OrderByDescending(s => s.CountAsNode + s.CountAsLeaf))
            {
                Console.Write("________________________________________________");
                stack.Render(visitor);
                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine();
            }
        }

        private static (int pid, bool generateEtlxFile) GetParameters(string[] args)
        {
            int pid = -1;
            bool generateEtlxFile = false;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].ToLower() == "-p")
                {
                    if (i + 1 >= args.Length)
                    {
                        throw new InvalidOperationException($"Missing process id after -p");
                    }

                    i++;
                    if (!int.TryParse(args[i], out pid))
                    {
                        throw new InvalidOperationException($"{args[i]} is not a number after -p");
                    }
                }
                else if (args[i].ToLower() == "-f")
                {
                    generateEtlxFile = true;
                }
                else
                {
                    throw new InvalidOperationException($"Unknown {args[i]} parameter...");
                }
            }

            return (pid, generateEtlxFile);
        }


        private static void ShowHeader()
        {
            Console.WriteLine("CpuSamplingProfiler v1.0.0 - Sampled CPU profiler for .NET applications");
            Console.WriteLine("by Christophe Nasarre");
            Console.WriteLine();
        }
        private static void ShowHelp()
        {
            Console.WriteLine();
            Console.WriteLine("Usage: CpuSamplingProfiler -p <pid> [-f]");
            Console.WriteLine("   Ex: CpuSamplingProfiler -p 1234     (collect CPU samples for process 1234)");
            Console.WriteLine("   Ex: CpuSamplingProfiler -p 1234  -f (collect CPU samples for process 1234 and generate trace-1234.etlx file)");
            Console.WriteLine();
        }
    }
}
