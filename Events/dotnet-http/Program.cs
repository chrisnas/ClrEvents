using Shared;
using System;
using System.Reflection;

namespace dotnet_http
{
    internal class Program
    {
        static string ToolName = "dotnet-http";
        static void Main(string[] args)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            Console.WriteLine(string.Format(Header, ToolName, version));

            if (args.Length != 1)
            {
                Console.WriteLine("Only a pid is expected as parameter");
                Console.WriteLine(string.Format(Help, ToolName));
                return;
            }

            if (!int.TryParse(args[0], out int pid))
            {
                Console.WriteLine($"Invalid number '{args[0]}' as pid");
                Console.WriteLine(string.Format(Help, ToolName));
                return;
            }

            try
            {
                ClrEventsManager source = new ClrEventsManager(pid, EventFilter.Network, isLogging: false);
                NetworkEventsManager networkEventsManager = new NetworkEventsManager(source, isLogging: true);

                Console.WriteLine("status |     total |______wait_|       DNS |______wait_|    socket |______wait_|     HTTPS |  Response - Url ");
                Console.WriteLine("-------|-----------|-----------|-----------|-----------|-----------|-----------|-----------|-------------------------------------------------");

                // this is a blocking call until the session is disposed
                source.ProcessEvents();
            }
            catch (Exception x)
            {
                Console.WriteLine(x.Message);
                Console.WriteLine(string.Format(Help, ToolName));
            }
        }

        private static string Header =
            "{0} v{1} - Detail HTTP requests" + Environment.NewLine +
            "by Christophe Nasarre" + Environment.NewLine
            ;
        private static string Help =
            "Usage:  {0} <process ID>" + Environment.NewLine +
            "";
    }
}
