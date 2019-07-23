// need to add a reference to the following:
//  Microsoft.Diagnostics.Tools.RuntimeClient.dll (from diagnostics repo on github - not yet a nuget)
//  Microsoft.Diagnostics.Tracing TraceEvent nuget
// 
using Counters.RuntimeClient;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Text;


namespace SimpleCounters
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Missing pid...");
                return;
            }

            if (!int.TryParse(args[0], out var pid))
            {
                Console.WriteLine("Missing pid...");
                return;
            }

            var listener = new CsvCounterListener($"log-{pid}.csv", pid);
            listener.Start();

            Console.WriteLine("Press ENTER to stop collecting counters...");
            Console.ReadLine();

            listener.Stop();
        }

        private static void ProcessEvents(TraceEvent data)
        {
            if (data.EventName.Equals("EventCounters"))
            {
                IDictionary<string, object> payloadVal = (IDictionary<string, object>)(data.PayloadValue(0));
                IDictionary<string, object> payloadFields = (IDictionary<string, object>)(payloadVal["Payload"]);

                Console.Write($"{data.TimeStampRelativeMSec, 8}  |  ");
                DumpPayload(payloadFields);
                Console.WriteLine();
            }
        }

        static List<string> _counters = new List<string>(16);
        private static void DumpPayload(IDictionary<string, object> payload)
        {
            var count = 0;
            var name = "";
            var buffer = new StringBuilder(128);
            foreach (var kv in payload)
            {
                if (count == 0)
                {
                    name = kv.Value.ToString();
                    if (_counters.Contains(name))
                    {
                        Console.Write(name);
                        return;
                    }
                    _counters.Add(name);
                }
                buffer.AppendLine($"{kv.Key} = {kv.Value}");

                count++;
            }

            Console.WriteLine(buffer.ToString());
        }
    }
}
