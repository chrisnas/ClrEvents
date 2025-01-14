using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Shared;

namespace dotnet_wait
{
    internal class Program
    {
        static string ToolName = "dotnet-wait";

        private ContentionInfoStore _contentionStore;
        private MethodStore _methods;
        private int _waitThreshold = 0;
        private bool _isLogging = false;

        static async Task Main(string[] args)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            Console.WriteLine(string.Format(Header, ToolName, version));

            (int pid, string pathName, int waitThreshold) parameters;
            try
            {
                parameters = ParseCommandLine(args);
            }
            catch (InvalidOperationException x)
            {
                Console.WriteLine(x.Message);
                Console.WriteLine(string.Format(Help, ToolName));
                return;
            }

            var runner = new Program();
            runner._waitThreshold = parameters.waitThreshold;

            try
            {
                if (parameters.pid != -1)
                {
                    ClrEventsManager source = new ClrEventsManager(parameters.pid, EventFilter.Contention, isLogging: false);
                    ContentionEventsManager manager = new ContentionEventsManager(source, parameters.waitThreshold);

                    // this is a blocking call until the session is disposed
                    source.ProcessEvents();
                    return;
                }

                // spawn an app to get the events from the startup; especially the Jitted methods information
                await runner.StartAndMonitor(parameters.pathName);

            }
            catch (TimeoutException)
            {
                Console.WriteLine($"The process {parameters.pid} is probably not running...");
                Console.WriteLine(string.Format(Help, ToolName));
            }
            catch (Exception x)
            {
                Console.WriteLine(x.Message);
                Console.WriteLine(string.Format(Help, ToolName));
            }
        }

        private async Task StartAndMonitor(string pathName)
        {
            var endpoint = await StartMonitoredApp(pathName);
            var client = new DiagnosticsClient(endpoint);
            EventPipeEventSource source = null;

            var streamTask = Task.Run(() =>
            {
                try
                {
                    var session = client.StartEventPipeSession(GetProviders(), requestRundown: false);
                    client.ResumeRuntime();

                    source = new EventPipeEventSource(session.EventStream);

                    RegisterListeners(source);

                    // blocking call until the session is disposed
                    source.Process();
                }
                catch (Exception x)
                {
                    Console.WriteLine(x.Message);
                    Console.WriteLine(string.Format(Help, ToolName));
                }
            });

            var inputTask = Task.Run(() =>
            {
                Console.WriteLine("Press ENTER to exit...");
                Console.ReadLine();
                if (source != null)
                {
                    source.Dispose();
                }
            });

            Task.WaitAny(streamTask, inputTask);  // exit if an error occurs in event processing
        }

        private void RegisterListeners(EventPipeEventSource source)
        {
            source.Clr.ContentionStart += OnContentionStart;
            source.Clr.ContentionStop += OnContentionStop;
            source.Clr.WaitHandleWaitStart += OnWaitHandleWaitStart;
            source.Clr.WaitHandleWaitStop += OnWaitHandleWaitStop;

            // needed to get JITed method details
            source.Clr.MethodLoadVerbose += OnMethodDetails;
            source.Clr.MethodDCStartVerboseV2 += OnMethodDetails;
        }

        private static IEnumerable<EventPipeProvider> GetProviders()
        {
            List<EventPipeProvider> providers = new List<EventPipeProvider>()
            {
                new EventPipeProvider(
                    name: "Microsoft-Windows-DotNETRuntime",
                    keywords: GetKeywords(),
                    eventLevel: EventLevel.Verbose  // verbose for WaitHandle contention
                    )
            };

            return providers;
        }

        private static long GetKeywords()
        {
            ClrTraceEventParser.Keywords keywords =
                ClrTraceEventParser.Keywords.Contention |   // thread contention timing
                ClrTraceEventParser.Keywords.WaitHandle |   // .NET 9 WaitHandle kind of contention

                // needed to resolve the method names
                ClrTraceEventParser.Keywords.Jit | // Turning on JIT events is necessary to resolve JIT compiled code
                ClrTraceEventParser.Keywords.JittedMethodILToNativeMap | // This is needed if you want line number information in the stacks
                ClrTraceEventParser.Keywords.Loader // You must include loader events as well to resolve JIT compiled code.
                ;

            return (long)keywords;
        }

        private async Task<IpcEndpoint> StartMonitoredApp(string pathName)
        {
            // create the diagnostics server that will acknowledge the monitored app runtime to connect
            var name = $"dotnet-wait_{Environment.ProcessId}";
            var port = (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                ? name
                : Path.Combine(Path.GetTempPath(), name); // socket name
            var diagnosticsServer = new ReversedDiagnosticsServer(port);
            diagnosticsServer.Start();
            using CancellationTokenSource cancellation = new CancellationTokenSource(10000);
            var acceptTask = diagnosticsServer.AcceptAsync(cancellation.Token);

            // start the monitored app
            // TODO: do not support "dotnet foo.dll" or parameters
            var psi = new ProcessStartInfo(pathName);
            psi.EnvironmentVariables["DOTNET_DiagnosticPorts"] = port;  // suspend, connect by default
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Normal;
            //psi.RedirectStandardOutput = false;
            //psi.RedirectStandardError = false;
            //psi.RedirectStandardInput = false;

            var process = System.Diagnostics.Process.Start(psi);
            _contentionStore = new ContentionInfoStore();
            _contentionStore.AddProcess(process.Id);
            _methods = new MethodStore(process.Id, loadModules: false);

            // wait for the diagnostics server to accept the connection
            var result = await acceptTask;
            return result.Endpoint;
        }

        private void OnMethodDetails(MethodLoadUnloadVerboseTraceData data)
        {
            // care only about jitted methods
            if (!data.IsJitted) return;

            _methods.Add(data.MethodStartAddress, data.MethodSize, data.MethodNamespace, data.MethodName, data.MethodSignature);

            if (_isLogging)
            {
                WriteLogLine($"   0x{data.MethodStartAddress.ToString("x12")} - {data.MethodSize,6} | {data.MethodName}");
            }
        }

        private AddressStack BuildCallStack(EventPipeUnresolvedStack data)
        {
            if (data == null)
                return null;

            var length = data.Addresses.Length;
            AddressStack stack = new AddressStack(length);

            // frame 0 is the last frame of the stack (i.e. last called method)
            for (int i = 0; i < length; i++)
            {
                stack.AddFrame(data.Addresses[i]);
            }

            return stack;
        }

        private void OnContentionStart(ContentionStartTraceData data)
        {
            // TODO: move the ReadFrom code to AddressStack to have only 1 class to handle call stacks
            var callStack = EventPipeUnresolvedStack.ReadFrom(data);
            var stack = BuildCallStack(callStack);

            if (_isLogging)
            {
                int nbFrames = (callStack == null) ? 0 : callStack.Addresses.Length;
                WriteLogLine($"   {nbFrames} frames");
            }

            ContentionInfo info = _contentionStore.GetContentionInfo(data.ProcessID, data.ThreadID);
            if (info == null)
                return;

            info.TimeStamp = data.TimeStamp;
            info.ContentionStartRelativeMSec = data.TimeStampRelativeMSec;
            info.Stack = stack;
        }

        private void OnContentionStop(ContentionStopTraceData data)
        {
            ContentionInfo info = _contentionStore.GetContentionInfo(data.ProcessID, data.ThreadID);
            if (info == null)
                return;

            // unlucky case when we start to listen just after the ContentionStart event
            if (info.ContentionStartRelativeMSec == 0)
            {
                return;
            }

            // TODO: new versions of .NET are providing the duration in data.DurationNs
            var contentionDurationMSec = data.TimeStampRelativeMSec - info.ContentionStartRelativeMSec;

            info.ContentionStartRelativeMSec = 0;
            var isManaged = (data.ContentionFlags == ContentionFlags.Managed);
            var callstack = SymbolizeStack(info.Stack);
            OnWait(data.TimeStamp, data.ProcessID, data.ThreadID, TimeSpan.FromMilliseconds(contentionDurationMSec), isManaged, callstack);
        }

        private void OnWait(DateTime timeStamp, int processID, int threadID, TimeSpan duration, bool isManaged, List<string> callstack)
        {
            if (isManaged)
            {
                if (duration.TotalMilliseconds > _waitThreshold)
                {
                    Console.WriteLine($"{threadID,7} | {duration.TotalMilliseconds} ms");
                    if (callstack != null)
                    {
                        // show the last frame at the top
                        for (int i = 0; i < callstack.Count; i++)
                        //for (int i = e.Callstack.Count - 1; i > 0; i--)
                        {
                            Console.WriteLine($"    {callstack[i]}");
                        }
                    }
                    Console.WriteLine();
                }
            }
        }

        private List<string> SymbolizeStack(AddressStack stack)
        {
            if (stack == null)
                return null;

            List<string> callstack = new List<string>(stack.Stack.Count);
            foreach (var frame in stack.Stack)
            {
                string method = _methods.GetFullName(frame);
                callstack.Add(method);
            }

            return callstack;
        }

        // new .NET 9 contention events for WaitHandle-derived classes
        private void OnWaitHandleWaitStart(WaitHandleWaitStartTraceData data)
        {
            // TODO: move the ReadFrom code to AddressStack to have only 1 class to handle call stacks
            var callStack = EventPipeUnresolvedStack.ReadFrom(data);
            var stack = BuildCallStack(callStack);

            if (_isLogging)
            {
                int nbFrames = (callStack == null) ? 0 : callStack.Addresses.Length;
                WriteLogLine($"   {nbFrames} frames");
            }

            ContentionInfo info = _contentionStore.GetContentionInfo(data.ProcessID, data.ThreadID);
            if (info == null)
                return;

            //Console.WriteLine($"        {data.ThreadID,8}  | {data.TimeStampRelativeMSec}");

            info.TimeStamp = data.TimeStamp;
            info.ContentionStartRelativeMSec = data.TimeStampRelativeMSec;
            info.Stack = stack;
        }

        private void OnWaitHandleWaitStop(WaitHandleWaitStopTraceData data)
        {
            ContentionInfo info = _contentionStore.GetContentionInfo(data.ProcessID, data.ThreadID);
            if (info == null)
                return;

            // unlucky case when we start to listen just after the WaitHandleStart event
            if (info.ContentionStartRelativeMSec == 0)
            {
                return;
            }

            // Too bad the duration is not provided in the payload like in ContentionStop...
            var contentionDurationMSec = data.TimeStampRelativeMSec - info.ContentionStartRelativeMSec;

            info.ContentionStartRelativeMSec = 0;
            bool isManaged = true;  // always managed
            var duration = TimeSpan.FromMilliseconds(contentionDurationMSec);
            var callstack = SymbolizeStack(info.Stack);

            //Console.WriteLine($"        {data.ThreadID,8}  < {data.TimeStampRelativeMSec} = {duration.TotalMilliseconds} ms");
            OnWait(data.TimeStamp, data.ProcessID, data.ThreadID, duration, isManaged, callstack);
        }


        private void WriteLogLine(string line = null)
        {
            if (_isLogging)
            {
                if (line != null)
                {
                    Console.WriteLine(line);
                }
                else
                {
                    Console.WriteLine();
                }
            }
        }

        private static (int pid, string pathName, int waitThreshold) ParseCommandLine(string[] args)
        {
            (int pid, string pathName, int waitThreshold) parameters = (pid: -1, pathName: "", waitThreshold: 0);
            for (int i = 0; i < args.Length; i++)
            {
                var current = args[i].ToLower();
                if (current == "-p")
                {
                    i++;
                    if (i < args.Length)
                    {
                        if (!int.TryParse(args[i], out parameters.pid))
                        {
                            throw new InvalidOperationException($"{args[i]} is not a numeric pid");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"Missing pid value...");
                    }
                }
                else if (current == "--")
                {
                    i++;
                    if (i < args.Length)
                    {
                        parameters.pathName = args[i];

                        // TODO: use the remaining arguments are the argument for the app to spawn
                    }
                    else
                    {
                        throw new InvalidOperationException($"Missing path name value...");
                    }
                }
                else if (current == "-w")
                {
                    i++;
                    if (i < args.Length)
                    {
                        if (!int.TryParse(args[i], out parameters.waitThreshold))
                        {
                            throw new InvalidOperationException($"{args[i]} is not a numeric wait threshold");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"Missing wait duration threshold value...");
                    }
                }
            }

            if ((parameters.pid == -1) && (parameters.pathName == ""))
            {
                throw new InvalidOperationException($"Missing pid or app to execute");
            }

            return parameters;
        }

        private static string Header =
            "{0} v{1} - List wait duration" + Environment.NewLine +
            "by Christophe Nasarre" + Environment.NewLine
            ;
        private static string Help =
            "Usage:  {0}  -s <path to an app to spawn>  -w <min wait duration threshold>" + Environment.NewLine +
            //"Usage:  {0}  -p <process ID>  -s <path to an app to spawn>  -w <min wait duration threshold>" + Environment.NewLine +
            "";
    }
}
