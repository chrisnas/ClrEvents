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
        private bool _isLogging = false;  // for debugging purpose only
        private static string _outputPathname = "";

        static async Task Main(string[] args)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            Console.WriteLine(string.Format(Header, ToolName, version));

            (int pid, string executablePathname, string arguments, bool suspend, int pidToResume, string outputPathname, int waitThreshold) parameters;
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

            _outputPathname = parameters.outputPathname;

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

                // we are supporting 3 modes when spawning an application:
                if (parameters.suspend)
                {
                    // 1. -s -- <path of the app to spawn> : the child process is started in the current console and stays suspended
                    await runner.StartAndSuspend(parameters.executablePathname, parameters.arguments);
                }
                else if (parameters.pidToResume != -1)
                {
                    // 2. -r <pid of the tool instance used in 2.> : the child process was spawned in another console and we need to resume it
                    await runner.ResumeAndMonitor(parameters.pidToResume);
                }
                else
                {
                    // 3. the child process is started in the current console that is shared with us
                    await runner.StartAndMonitor(parameters.executablePathname, parameters.arguments);
                }
            }
            catch (TimeoutException)
            {
                WriteOutputLine($"The process {parameters.pid} is probably not running...");
                WriteOutputLine(string.Format(Help, ToolName));
            }
            catch (Exception x)
            {
                WriteOutputLine(x.Message);
                WriteOutputLine(string.Format(Help, ToolName));
            }
        }

        private async Task StartAndSuspend(string executablePathName, string arguments = "")
        {
            // spawn the child process and don't resume nor monitor it
            var result = await SpawnApp(executablePathName, arguments);

            Console.WriteLine($"To resume the spawn process, open another console and type: dotnet wait -r {Environment.ProcessId}");
            // simply return from here; the console is left to the child process

            //// TODO: check if we even need to start a diagnostics session and simply return; leaving the console to the spawn child app
            //var client = new DiagnosticsClient(result.endpoint);
            //EventPipeEventSource source = null;

            //var streamTask = Task.Run(() =>
            //{
            //    try
            //    {
            //        var session = client.StartEventPipeSession(GetProviders(), requestRundown: false);
            //        source = new EventPipeEventSource(session.EventStream);

            //        // blocking call until the session is disposed
            //        source.Process();
            //    }
            //    catch (Exception x)
            //    {
            //        WriteOutputLine(x.Message);
            //        WriteOutputLine(string.Format(Help, ToolName));
            //    }
            //});

            //// Note: don't handle console input in a separate task as we want to keep the console for the child process
            //streamTask.Wait();  // exit when the child process ends
        }

        private async Task ResumeAndMonitor(int pid)
        {
            // TODO: resume the child process and monitor it
            // we don't need to filter per process ID because we are only monitoring the one spawn process
            _contentionStore = new ContentionInfoStore();
            _contentionStore.AddProcess(0);
            _methods = new MethodStore(0, loadModules: false);

            // create a new diagnostics client for the process to resume
            var port = GetDiagnosticsPort(pid);
            var diagnosticsServer = new ReversedDiagnosticsServer(port);
            diagnosticsServer.Start();
            using CancellationTokenSource cancellation = new CancellationTokenSource(10000);
            var result = await diagnosticsServer.AcceptAsync(cancellation.Token);
            var client = new DiagnosticsClient(result.Endpoint);
            EventPipeEventSource source = null;

            var streamTask = Task.Run(() =>
            {
                try
                {
                    var session = client.StartEventPipeSession(GetProviders(), requestRundown: false);
                    source = new EventPipeEventSource(session.EventStream);
                    RegisterListeners(source);

                    client.ResumeRuntime();

                    // blocking call until the session is disposed
                    source.Process();
                }
                catch (Exception x)
                {
                    if (x.Source == "Microsoft.Diagnostics.FastSerialization")
                    {
                        WriteOutputLine("The monitored process has probably exited...");
                    }
                    else
                    {
                        WriteOutputLine(x.Message);
                        WriteOutputLine(string.Format(Help, ToolName));
                    }
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

        private async Task StartAndMonitor(string executablePathName, string arguments = "")
        {
            var result = await SpawnApp(executablePathName, arguments);
            _contentionStore = new ContentionInfoStore();
            _contentionStore.AddProcess(result.childPid);
            _methods = new MethodStore(result.childPid, loadModules: false);
            var client = new DiagnosticsClient(result.endpoint);
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
                    WriteOutputLine(x.Message);
                    WriteOutputLine(string.Format(Help, ToolName));
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


        private string GetDiagnosticsPort(int pid = -1)
        {
            var name = (pid == -1) ? $"dotnet-wait_{Environment.ProcessId}" : $"dotnet-wait_{pid}";
            return (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                ? name
                : Path.Combine(Path.GetTempPath(), name); // socket name
        }


        private async Task<(IpcEndpoint endpoint, int childPid)> SpawnApp(string pathName, string arguments)
        {
            // create the diagnostics server that will acknowledge the monitored app runtime to connect
            var port = GetDiagnosticsPort();
            var diagnosticsServer = new ReversedDiagnosticsServer(port);
            diagnosticsServer.Start();
            using CancellationTokenSource cancellation = new CancellationTokenSource(100000);
            var acceptTask = diagnosticsServer.AcceptAsync(cancellation.Token);

            // start the monitored app
            var psi = new ProcessStartInfo(pathName);
            if (!string.IsNullOrEmpty(arguments))
            {
                psi.Arguments = arguments;
            }
            psi.EnvironmentVariables["DOTNET_DiagnosticPorts"] = port;  // suspend, connect by default
            psi.UseShellExecute = false;
            //psi.CreateNoWindow = true;
            //psi.WindowStyle = ProcessWindowStyle.Normal;

            //psi.RedirectStandardOutput = false;
            //psi.RedirectStandardError = false;
            //psi.RedirectStandardInput = false;

            var process = System.Diagnostics.Process.Start(psi);

            // wait for the diagnostics server to accept the connection
            var result = await acceptTask;
            return (result.Endpoint, process.Id);
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

            ContentionInfo info = _contentionStore.GetContentionInfo(0, data.ThreadID);
            if (info == null)
                return;

            info.TimeStamp = data.TimeStamp;
            info.ContentionStartRelativeMSec = data.TimeStampRelativeMSec;
            info.Stack = stack;
        }

        private void OnContentionStop(ContentionStopTraceData data)
        {
            ContentionInfo info = _contentionStore.GetContentionInfo(0, data.ThreadID);
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
                    WriteOutputLine($"{threadID,7} | {duration.TotalMilliseconds} ms");
                    if (callstack != null)
                    {
                        // show the last frame at the top
                        for (int i = 0; i < callstack.Count; i++)
                        //for (int i = e.Callstack.Count - 1; i > 0; i--)
                        {
                            WriteOutputLine($"    {callstack[i]}");
                        }
                    }
                    WriteOutputLine();
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

            ContentionInfo info = _contentionStore.GetContentionInfo(0, data.ThreadID);
            if (info == null)
                return;

            //WriteOutputLine($"        {data.ThreadID,8}  | {data.TimeStampRelativeMSec}");

            info.TimeStamp = data.TimeStamp;
            info.ContentionStartRelativeMSec = data.TimeStampRelativeMSec;
            info.Stack = stack;
        }

        private void OnWaitHandleWaitStop(WaitHandleWaitStopTraceData data)
        {
            ContentionInfo info = _contentionStore.GetContentionInfo(0, data.ThreadID);
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

            //WriteOutputLine($"        {data.ThreadID,8}  < {data.TimeStampRelativeMSec} = {duration.TotalMilliseconds} ms");
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

        private static void WriteOutputLine(string line = null)
        {
            if (_outputPathname == "")
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
            else
            {
                if (line != null)
                {
                    File.AppendAllText(_outputPathname, line + Environment.NewLine);
                }
                else
                {
                    File.AppendAllText(_outputPathname, Environment.NewLine);
                }
            }
        }

        private static (int pid, string pathName, string arguments, bool suspend, int pidToResume, string outputPathname, int waitThreshold) ParseCommandLine(string[] args)
        {
            (int pid, string pathName, string arguments, bool suspend, int pidToResume, string outputPathname, int waitThreshold) parameters = (pid: -1, pathName: "", arguments: "", suspend: false, pidToResume:-1, outputPathname: "", waitThreshold: 0);
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
                else if (current == "-o")
                {
                    i++;
                    if (i < args.Length)
                    {
                        parameters.outputPathname = args[i];
                    }
                    else
                    {
                        throw new InvalidOperationException($"Missing output pathname...");
                    }
                }
                else if (current == "-s")
                {
                    parameters.suspend = true;
                }
                else if (current == "-r")
                {
                    i++;
                    if (i < args.Length)
                    {
                        if (!int.TryParse(args[i], out parameters.pidToResume))
                        {
                            throw new InvalidOperationException($"{args[i]} is not a numeric pid");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"Missing pid to resume...");
                    }
                }
                else if (current == "--")  // this is supposed to be the last one
                {
                    i++;
                    if (i < args.Length)
                    {
                        parameters.pathName = args[i];

                        // use the remaining arguments as the arguments for the child app to spawn
                        i++;
                        if (i < args.Length)
                        {
                            // from https://github.com/dotnet/diagnostics/blob/main/src/Tools/Common/ReversedServerHelpers/ReversedServerHelpers.cs#L37
                            parameters.arguments = "";
                            for (int j = i; j < args.Length; j++)
                            {
                                if (args[j].Contains(' '))
                                {
                                    parameters.arguments += $"\"{args[j].Replace("\"", "\\\"")}\"";
                                }
                                else
                                {
                                    parameters.arguments += args[j];
                                }

                                if (j != args.Length)
                                {
                                    parameters.arguments += " ";
                                }
                            }
                        }

                        // no need to look for more arguments
                        break;
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

            if ((parameters.pid == -1) && (parameters.pathName == "") && (parameters.pidToResume == -1))
            {
                throw new InvalidOperationException($"Missing pid or app to execute/resume");
            }

            // sanity checks
            // 1. -p and -- cannot be used together
            // 2. -s and -r cannot be used together
            // 3. -- cannot be used with -r
            // 4. -s must be used with --
            if ((parameters.pid != -1) && (parameters.pathName != ""))
            {
                throw new InvalidOperationException("-p and -- cannot be used together");
            }
            if (parameters.suspend && (parameters.pidToResume != -1))
            {
                throw new InvalidOperationException("-s and -r cannot be used together");
            }
            if ((parameters.pidToResume != -1) && (parameters.pathName != ""))
            {
                throw new InvalidOperationException("-- cannot be used with -r");
            }
            if (parameters.suspend && (parameters.pathName == ""))
            {
                throw new InvalidOperationException("-s must be used with -- <application path to be started>");
            }

            return parameters;
        }

        private static string Header =
            "{0} v{1} - List wait duration" + Environment.NewLine +
            "by Christophe Nasarre" + Environment.NewLine
            ;
        private static string Help =
            "Usage:  {0}  -w <min wait duration threshold>  -o <file to save the waits details>  " + Environment.NewLine +
            "             -p <process ID> " + Environment.NewLine +
            "             -s  -- <path to an app to spawn followed by its command line if needed>" + Environment.NewLine +
            "             -r <pid of the instance to use with -s> " + Environment.NewLine +
            "        If the process is already running, use -p <pid>." + Environment.NewLine +
            "        To spawn a new process, use -- <executable path>" + Environment.NewLine +
            "        Use -s when you need to start the child process in the current console and get the wait input/output in another one" + Environment.NewLine +
            "        when you use -r followed by the pid of the process using -s" + Environment.NewLine +
            "Ex: -p 1234            list all waits for running process 1234" + Environment.NewLine +
            "    -w 100 -p 1234     list waits lasting more than 100 ms for process 1234" + Environment.NewLine +
            "    -o waits.txt -- dotnet foo.dll arg1 \"a r g 2\"     start foo.dll with arguments and save the wait details in waits.txt  !!! inputs are shared between the tool and foo !!!" + Environment.NewLine +
            "    -s -- dotnet foo.dll arg1 \"a r g 2\"   start foo.dll with arguments and wait for the following command line to be run in another console" + Environment.NewLine +
            "    -r 2468                                 resume foo.dll and output wait details/input in the current console. Note: 2468 is given by the -s command" + Environment.NewLine +
            //"Usage:  {0}  -p <process ID>  -s <path to an app to spawn>  -w <min wait duration threshold>" + Environment.NewLine +
            "";
    }
}
