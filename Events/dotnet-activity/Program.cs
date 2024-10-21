using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Xml;
using Shared;

namespace activity
{
    public class Program
    {
        static void Main(string[] args)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            Console.WriteLine(string.Format(Header, "dotnet-activity", version));

            try
            {
                var parameters = GetParameters(args);
                var activityID = parameters.guid;
                var path = parameters.path;
                if ((activityID == Guid.Empty) && (path == null))
                {
                    throw new InvalidOperationException("No activity provided");
                }

                if (path != null)
                {
                    ParsePath(path);
                }
                else
                {
                    ParseActivity(activityID, parameters.showPID);
                }
            }
            catch (InvalidOperationException x)
            {
                Console.WriteLine(x.Message);
                Console.WriteLine(string.Format(Help, "dotnet-activity"));
            }
        }

        private static unsafe void ParsePath(string path)
        {
            Console.Write($"{path} | ");

            Guid guid = Guid.Parse("00000000-0000-0000-0000-000000000000");
            Guid* outPtr = &guid;
            int activityPathGuidOffsetStart = 0;
            var nodes = path.Split('/');
            foreach (var node in nodes)
            {
                activityPathGuidOffsetStart = ActivityHelpers.AddIdToGuid(outPtr, activityPathGuidOffsetStart, uint.Parse(node));
            }

            Console.WriteLine($"{guid}");
        }

        private static void ParseActivity(Guid activityID, bool showPID)
        {
            if (!ActivityHelpers.IsActivityPath(activityID, 0))
            {
                throw new InvalidOperationException($"{activityID} is not an ActivityID");
            }

            Console.Write($"{activityID} | ");

            if (showPID)
            {
                Console.Write($"{ActivityHelpers.ActivityPathProcessID(activityID)} | ");
            }
            Console.WriteLine($"{ActivityHelpers.ActivityPathString(activityID)}");
        }

        private static (Guid guid, bool showPID, string path) GetParameters(string[] args)
        {
            Guid guid = Guid.Empty;
            bool showPID = false;
            string path = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].ToLower() == "-pid")
                {
                    showPID = true;
                }
                else
                {
                    if (args[i].Contains("/"))
                    {
                        path = args[i];
                    }
                    else
                    if (!Guid.TryParse(args[i], out guid))
                    {
                        throw new InvalidOperationException($"{args[i]} is not a valid GUID");
                    }
                }
            }

            return (guid, showPID, path);
        }

        private static string Header =
            "{0} v{1} - Dump Activity" + Environment.NewLine +
            "by Christophe Nasarre" + Environment.NewLine
            ;
        private static string Help =
            "Show the content of an ActivityID guid" + Environment.NewLine +
            "Usage:  {0} <GUID>" + Environment.NewLine +
            "           [-pid ; the process ID is hidden by default]" + Environment.NewLine +
            "";
    }
}
