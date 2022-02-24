using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.Linq;

namespace pinfo
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Missing --p <process ID>...");
                return;
            }
            if (args[0] != "--p")
            {
                Console.WriteLine("Missing --p <process ID>...");
                return;
            }
            if (!int.TryParse(args[1], out int pid))
            {
                Console.WriteLine($"process ID '{args[1]}' must be a numeric value...");
                return;
            }

            // get environment variables via existing wrapper in DiagnosticsClient
            var client = new DiagnosticsClient(pid);
            var envVariables = client.GetProcessEnvironment();
            foreach (var variable in envVariables.Keys.OrderBy(k => k))
            {
                // NOTE: there is a value for the empty/"" key = "ExitCode=00000000"
                Console.WriteLine($"{variable,26} = {envVariables[variable]}");
            }

            // 2 commands exist to get process information but the corresponding
            // DiagnosticsClient.GetProcessInfo(Async) methods are internal  :^(
        }
    }
}
