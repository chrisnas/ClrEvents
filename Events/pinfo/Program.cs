using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.Diagnostics;
using System.Linq;

namespace pinfo
{
    class Program
    {
        static int Main(string[] args)
        {
            ShowHeader();

            (int pid, bool listProcesses) parameters;
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

            if (parameters.listProcesses)
            {
                ListProcesses();
                return 0;
            }

            ListDetails(parameters.pid);

            Console.ReadLine();

            return 0;
        }

        private static void ListDetails(int pid)
        {
            ListProcessInfo(pid);
            Console.WriteLine("-----------------------------------------------------------------------------------------");
            ListEnvironmentVariables(pid);
        }

        private static void ListProcessInfo(int pid)
        {
            // 2 commands exist to get process information but the corresponding
            // DiagnosticsClient.GetProcessInfo(Async) methods are internal.
            // So,
            //      (1) copy the corresponding project folder from the Diagnostics repository
            //      (2) add the application assembly as <InternalsVisibleTo> in the copied .csproj
            //      (3) reference this project instead of the nuget package
            // and it works  :^)
            var client = new DiagnosticsClient(pid);
            var info = client.GetProcessInfo();  // this method is internal
            Console.WriteLine($"              Command Line = {info.CommandLine}");
            Console.WriteLine($"              Architecture = {info.ProcessArchitecture}");
            Console.WriteLine($"      Entry point assembly = {info.ManagedEntrypointAssemblyName}");
            Console.WriteLine($"               CLR Version = {info.ClrProductVersionString}");
        }



        private static void ListProcesses()
        {
            var selfPid = Process.GetCurrentProcess().Id;
            foreach (var pid in DiagnosticsClient.GetPublishedProcesses())
            {
                var process = Process.GetProcessById(pid);
                Console.WriteLine($"{pid,6}{GetSeparator(pid == selfPid)}{process.ProcessName}");
            }
        }

        private static string GetSeparator(bool isCurrentProcess)
        {
            return isCurrentProcess ? " = " : "   ";
        }

        private static void ListEnvironmentVariables(int pid)
        {
            // get environment variables via existing wrapper in DiagnosticsClient
            var client = new DiagnosticsClient(pid);
            var envVariables = client.GetProcessEnvironment();
            foreach (var variable in envVariables.Keys.OrderBy(k => k))
            {
                // NOTE: there is a value for the empty/"" key = "ExitCode=00000000"
                Console.WriteLine($"{variable,26} = {envVariables[variable]}");
            }
        }

        private static (int pid, bool listProcesses) GetParameters(string[] args)
        {
            if (args.Length != 1)
            {
                throw new InvalidOperationException("Invalid or missing parameters");
            }

            (int pid, bool listProcesses) parameters = (pid: -1, listProcesses: false);
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].ToLower() == "ps")
                {
                    parameters.listProcesses = true;
                }
                else
                if (!int.TryParse(args[i], out parameters.pid))
                {
                    throw new InvalidOperationException($"{args[i]} is not a numeric pid");
                }
            }

            if ((parameters.pid == -1) && !parameters.listProcesses)
            {
                throw new InvalidOperationException($"Invalid parameter: {args[0]}");
            }

            return parameters;
        }
        private static void ShowHeader()
        {
            Console.WriteLine("pinfo v1.0.0 - Demo of CLR IPC Protocol commands");
            Console.WriteLine("by Christophe Nasarre");
            Console.WriteLine();
        }
        private static void ShowHelp()
        {
            Console.WriteLine();
            Console.WriteLine("pinfo either list .NET processes or get environment variable of a given .NET process.");
            Console.WriteLine("Usage: pinfo <pid> or ps");
            Console.WriteLine("   Ex: pinfo ps     list running .NET processes");
            Console.WriteLine("   Ex: pinfo 1234   enumerate environment variable of .NET process #1234");
            Console.WriteLine();
        }
    }
}
