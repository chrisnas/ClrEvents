using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Text;
using Microsoft.Diagnostics.Tools.RuntimeClient;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Analysis.GC;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Tpl;
using Microsoft.Diagnostics.Tracing.Session;

namespace Shared
{
    public class ClrEventsManager
    {
        private bool _isLogging;
        private readonly int _processId;
        private readonly TypesInfo _types;
        private readonly ContentionInfoStore _contentionStore;
        private readonly MethodStore _methods;
        private readonly EventFilter _filter;
        private readonly TraceEventSession _session;
        private ClrRundownTraceEventParser _rundownParser;
        private TplEtwProviderTraceEventParser _TplParser;


        // CLR events
        public event EventHandler<ExceptionArgs> FirstChanceException;
        public event EventHandler<FinalizeArgs> Finalize;
        public event EventHandler<ContentionArgs> Contention;
        public event EventHandler<ThreadPoolStarvationArgs> ThreadPoolStarvation;
        public event EventHandler<GarbageCollectionArgs> GarbageCollection;
        public event EventHandler<AllocationTickArgs> AllocationTick;

        // network events
        public event EventHandler<HandshakeStartEventArgs> HandshakeStart;
        public event EventHandler<HandshakeStopEventArgs> HandshakeStop;
        public event EventHandler<HandshakeFailedEventArgs> HandshakeFailed;
        public event EventHandler<ResolutionStartEventArgs> DnsResolutionStart;
        public event EventHandler<DnsEventArgs> DnsResolutionStop;
        public event EventHandler<DnsEventArgs> DnsResolutionFailed;
        public event EventHandler<SocketEventArgs> SocketConnectStart;
        public event EventHandler<SocketEventArgs> SocketConnectStop;
        public event EventHandler<SocketEventArgs> SocketConnectFailed;
        public event EventHandler<SocketEventArgs> SocketAcceptStart;
        public event EventHandler<SocketEventArgs> SocketAcceptStop;
        public event EventHandler<SocketEventArgs> SocketAcceptFailed;
        public event EventHandler<HttpRequestStartEventArgs> HttpRequestStart;
        public event EventHandler<HttpRequestStopEventArgs> HttpRequestStop;
        public event EventHandler<HttpRequestFailedEventArgs> HttpRequestFailed;
        public event EventHandler<HttpRequestFailedEventArgs> HttpRequestFailedDetailed;
        public event EventHandler<HttpConnectionEstablishedArgs> HttpRequestEstablished;
        public event EventHandler<HttpRequestWithConnectionIdEventArgs> HttpRequestConnectionClosed;
        public event EventHandler<HttpRequestLeftQueueEventArgs> HttpRequestLeftQueue;
        public event EventHandler<HttpRequestWithConnectionIdEventArgs> HttpRequestHeaderStart;
        public event EventHandler<EventPipeBaseArgs> HttpRequestHeaderStop;
        public event EventHandler<EventPipeBaseArgs> HttpRequestContentStart;
        public event EventHandler<HttpRequestStatusEventArgs> HttpRequestContentStop;
        public event EventHandler<EventPipeBaseArgs> HttpResponseHeaderStart;
        public event EventHandler<HttpRequestStatusEventArgs> HttpResponseHeaderStop;
        public event EventHandler<EventPipeBaseArgs> HttpResponseContentStart;
        public event EventHandler<EventPipeBaseArgs> HttpResponseContentStop;
        public event EventHandler<HttpRedirectEventArgs> HttpRedirect;


        // constructor for EventPipe traces
        public ClrEventsManager(int processId, EventFilter filter, bool isLogging = false)
        {
            _processId = processId;
            _types = new TypesInfo();
            _contentionStore = new ContentionInfoStore();
            _contentionStore.AddProcess(processId);
            _methods = new MethodStore(processId, false);
            _filter = filter;
            _isLogging = isLogging;
            _rundownParser = null;
        }

        // constructor for TraceEvent + ETW traces
        public ClrEventsManager(TraceEventSession session, int processId, EventFilter filter, bool isLogging = false)
            : this(processId, filter, isLogging)
        {
            if (session == null)
            {
                throw new NullReferenceException($"{nameof(session)} must be provided");
            }

            _session = session;
        }


        // definitions from TplEventSource
        /// <summary>
        /// Only the most basic information about the workings of the task library
        /// This sets activity IDS and logs when tasks are schedules (or waits begin)
        /// But are otherwise silent
        /// </summary>
        public const EventKeywords TaskTransfer = (EventKeywords)1;
        /// <summary>
        /// TaskTranser events plus events when tasks start and stop
        /// </summary>
        public const EventKeywords Tasks = (EventKeywords)2;
        /// <summary>
        /// Events associted with the higher level parallel APIs
        /// </summary>
        public const EventKeywords Parallel = (EventKeywords)4;
        /// <summary>
        /// These are relatively verbose events that effectively just redirect
        /// the windows AsyncCausalityTracer to ETW
        /// </summary>
        public const EventKeywords AsyncCausalityOperation = (EventKeywords)8;
        public const EventKeywords AsyncCausalityRelation = (EventKeywords)0x10;
        public const EventKeywords AsyncCausalitySynchronousWork = (EventKeywords)0x20;

        /// <summary>
        /// Emit the stops as well as the schedule/start events
        /// </summary>
        public const EventKeywords TaskStops = (EventKeywords)0x40;

        /// <summary>
        /// TasksFlowActivityIds indicate that activity ID flow from one task
        /// to any task created by it.
        /// </summary>
        public const EventKeywords TasksFlowActivityIds = (EventKeywords)0x80;

        /// <summary>
        /// Events related to the happenings of async methods.
        /// </summary>
        public const EventKeywords AsyncMethod = (EventKeywords)0x100;


        private List<Provider> GetProviders()
        {
            List<Provider> providers = new List<Provider>()
            {
                new Provider(
                    name: "Microsoft-Windows-DotNETRuntime",
                    keywords: GetKeywords(),
                    eventLevel: EventLevel.Verbose  // TODO: only verbose for allocations and WaitHandle contention ?...
                    )
            };

            if ((_filter & EventFilter.Contention) == EventFilter.Contention)
            {
                providers.Add(
                    new Provider(
                        name: ClrRundownTraceEventParser.ProviderName,
                        keywords: (ulong)(
                            ClrTraceEventParser.Keywords.Jit |
                            ClrTraceEventParser.Keywords.JittedMethodILToNativeMap |
                            ClrTraceEventParser.Keywords.Loader |
                            ClrTraceEventParser.Keywords.StartEnumeration  // This indicates to do the rundown now (at enable time)
                            ),
                        eventLevel: EventLevel.Verbose)
                    );
            }

            if ((_filter & EventFilter.Network) == EventFilter.Network)
            {
                providers.Add(
                new Provider(
                    name: "System.Net.Http",
                    keywords: (ulong)(1),
                    eventLevel: EventLevel.Verbose)
                );
                providers.Add(
                    new Provider(
                        name: "System.Net.Sockets",
                        keywords: (ulong)(0xFFFFFFFF),
                        eventLevel: EventLevel.Verbose)
                );
                providers.Add(
                    new Provider(
                        name: "System.Net.NameResolution",
                        keywords: (ulong)(0xFFFFFFFF),
                        eventLevel: EventLevel.Verbose)
                );
                providers.Add(
                    new Provider(
                        name: "System.Net.Security",
                        keywords: (ulong)(0xFFFFFFFF),
                        eventLevel: EventLevel.Verbose)
                );
                providers.Add(
                new Provider(
                    name: "System.Threading.Tasks.TplEventSource",      //   V-- this one is required to get the network events ActivityId
                    keywords: (ulong)(1 + 2 + 4 + 8 + 0x10 + 0x20 + 0x40 + 0x80 + 0x100),
                    eventLevel: EventLevel.Verbose)
                );
            }

            return providers;
        }

        private ulong GetKeywords()
        {
            ClrTraceEventParser.Keywords keywords = 0;

            if ((_filter & EventFilter.Contention) == EventFilter.Contention)
            {
                keywords |= ClrTraceEventParser.Keywords.Contention;        // thread contention timing
                keywords |= ClrTraceEventParser.Keywords.WaitHandle;        // .NET 9 WaitHandle kind of contention
                keywords |= ClrTraceEventParser.Keywords.Stack;             // we want to get the ClrStackWalk sibling events

                // events related to JITed methods
                keywords |= ClrTraceEventParser.Keywords.Jit | // Turning on JIT events is necessary to resolve JIT compiled code
                            ClrTraceEventParser.Keywords.JittedMethodILToNativeMap | // This is needed if you want line number information in the stacks
                            ClrTraceEventParser.Keywords.Loader; // You must include loader events as well to resolve JIT compiled code.
            }

            if ((_filter & EventFilter.ThreadStarvation) == EventFilter.ThreadStarvation)
            {
                keywords |= ClrTraceEventParser.Keywords.Threading;         // threadpool events
            }

            if ((_filter & EventFilter.Exception) == EventFilter.Exception)
            {
                keywords |= ClrTraceEventParser.Keywords.Exception;         // get the first chance exceptions
            }

            if (
                ((_filter & EventFilter.Finalizer) == EventFilter.Finalizer) ||
                ((_filter & EventFilter.AllocationTick) == EventFilter.AllocationTick)
                )
            {
                keywords |= ClrTraceEventParser.Keywords.GCHeapAndTypeNames // for finalizer type names
                         | ClrTraceEventParser.Keywords.Type;               // for TypeBulkType definition of types
            }

            if ((_filter & EventFilter.GC) == EventFilter.GC)
            {
                keywords |= ClrTraceEventParser.Keywords.GC;                // garbage collector details
            }

            return (ulong)keywords;
        }

        public void ProcessEvents()
        {
            if (_session != null)
            {
                ProcessEtwEvents();
                return;
            }

            ProcessEventPipeEvents();
        }

        private void ProcessEventPipeEvents()
        {
            var configuration = new SessionConfiguration(
                circularBufferSizeMB: 2000,
                format: EventPipeSerializationFormat.NetTrace,
                providers: GetProviders()
            );

            var binaryReader = EventPipeClient.CollectTracing(_processId, configuration, out var sessionId);
            EventPipeEventSource source = new EventPipeEventSource(binaryReader);
            RegisterListeners(source);

            // this is a blocking call
            source.Process();
        }

        private void ProcessEtwEvents()
        {
            // setup process filter if any
            TraceEventProviderOptions options = null;
            if (_processId != -1)
            {
                options = new TraceEventProviderOptions()
                {
                    ProcessIDFilter = new List<int>() { _processId },
                };
            }

            // register handlers for events on the session source
            // --------------------------------------------------
            RegisterListeners(_session.Source);

            // decide which provider to listen to with filters if needed
            _session.EnableProvider(
                ClrTraceEventParser.ProviderGuid,  // CLR provider
                (
                    ((_filter & EventFilter.AllocationTick) == EventFilter.AllocationTick) ||
                    ((_filter & EventFilter.Contention) == EventFilter.Contention)  // for .NET 9
                )
                ? TraceEventLevel.Verbose
                : TraceEventLevel.Informational,
                GetKeywords(),
                options
            );


            // this is a blocking call until the session is disposed
            _session.Source.Process();
        }

        private void RegisterListeners(TraceEventDispatcher source)
        {
            if ((_filter & EventFilter.Exception) == EventFilter.Exception)
            {
                // get exceptions
                source.Clr.ExceptionStart += OnExceptionStart;
            }

            if ((_filter & EventFilter.Finalizer) == EventFilter.Finalizer)
            {
                // get finalizers
                source.Clr.TypeBulkType += OnTypeBulkType;
                source.Clr.GCFinalizeObject += OnGCFinalizeObject;
            }

            if ((_filter & EventFilter.Contention) == EventFilter.Contention)
            {
                // get thread contention time
                source.Clr.ContentionStart += OnContentionStart;
                source.Clr.ContentionStop += OnContentionStop;
                source.Clr.WaitHandleWaitStart += OnWaitHandleWaitStart;
                source.Clr.WaitHandleWaitStop += OnWaitHandleWaitStop;

                // deal with call stacks but don't get native frames name (requires ETW kernel provider)
                // NOTE: these events are not sent with EventPipe; only ETW
                //source.Clr.ClrStackWalk += OnStackWalk;

                // needed to get JITed method details
                source.Clr.MethodLoadVerbose += OnMethodDetails;
                source.Clr.MethodDCStartVerboseV2 += OnMethodDetails;

                _rundownParser = new ClrRundownTraceEventParser(source);
                _rundownParser.MethodDCStartVerbose += OnMethodDetailsRundown;
                _rundownParser.MethodDCStopVerbose += OnMethodDetailsRundown;
            }

            if ((_filter & EventFilter.ThreadStarvation) == EventFilter.ThreadStarvation)
            {
                // detect ThreadPool starvation
                source.Clr.ThreadPoolWorkerThreadAdjustmentAdjustment += OnThreadPoolWorkerAdjustment;
            }

            //if ((_filter & EventFilter.GC) == EventFilter.GC)
            //{
            //    source.NeedLoadedDotNetRuntimes();
            //    source.AddCallbackOnProcessStart((TraceProcess proc) =>
            //    {
            //        if (proc.ProcessID != _processId)
            //            return;

            //        proc.AddCallbackOnDotNetRuntimeLoad((TraceLoadedDotNetRuntime runtime) =>
            //        {
            //            runtime.GCEnd += (TraceProcess p, TraceGC gc) =>
            //            {
            //                NotifyCollection(gc);
            //            };
            //        });
            //    });

            //}

            if ((_filter & EventFilter.AllocationTick) == EventFilter.AllocationTick)
            {
                // sample every ~100 KB of allocations
                source.Clr.GCAllocationTick += OnGCAllocationTick;
            }

            if (
                ((_filter & EventFilter.Network) == EventFilter.Network) |
                ((_filter & EventFilter.Contention) == EventFilter.Contention)
                )
            {
                source.AllEvents += OnEvents;
            }

            // TODO: handle TPL events via the existing TraceEvent dedicated parser
            _TplParser = new TplEtwProviderTraceEventParser(source);
            _TplParser.TaskScheduledSend += OnTaskScheduledSend;
            _TplParser.TaskExecuteStart += OnTaskExecuteStart;
            _TplParser.TaskExecuteStop += OnTaskExecuteStop;
            _TplParser.AwaitTaskContinuationScheduledSend += OnAwaitTaskContinuationScheduledSend;
            _TplParser.TaskWaitSend += OnTaskWaitSend;
            _TplParser.TaskWaitStop += OnTaskWaitStop;
        }

        private void OnMethodDetailsRundown(MethodLoadUnloadVerboseTraceData data)
        {
            // care only about jitted methods
            if (!data.IsJitted) return;

            _methods.Add(data.MethodStartAddress, data.MethodSize, data.MethodNamespace, data.MethodName, data.MethodSignature);

            if (_isLogging)
            {
                WriteLogLine($">  0x{data.MethodStartAddress.ToString("x12")} - {data.MethodSize,6} | {data.MethodName}");
            }
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

        //private void OnStackWalk(ClrStackWalkTraceData data)
        //{
        //    if (_isLogging)
        //    {
        //        WriteLogLine($"tid = {data.ThreadID,7} | StackWalk");
        //    }
        //}


        private void OnTaskScheduledSend(TaskScheduledArgs args)
        {
        }

        private void OnTaskExecuteStart(TaskStartedArgs args)
        {
        }

        private void OnTaskExecuteStop(TaskCompletedArgs args)
        {
        }

        private void OnAwaitTaskContinuationScheduledSend(AwaitTaskContinuationScheduledArgs args)
        {
        }

        private void OnTaskWaitSend(TaskWaitSendArgs args)
        {
        }

        private void OnTaskWaitStop(TaskWaitStopArgs args)
        {
        }

        private static string ClrProviderGuid = new Guid("e13c0d23-ccbc-4e12-931b-d9cc2eee27e4").ToString();
        private static Guid TplEventSourceGuild = Guid.Parse("2e5dba47-a3d2-4d16-8ee0-6671ffdcd7b5");

        private void OnEvents(TraceEvent data)
        {
            if (data.ProcessID != _processId)
                return;

            // skip TPL events
            if (data.ProviderGuid == TplEventSourceGuild)
                return;

            //// skip .NET runtime events
            //if (data.ProviderGuid.ToString() == ClrProviderGuid)
            //    return;

            //// skip events that are not related to network
            //if (
            //    (data.ProviderGuid != NetSecurityEventSourceProviderGuid) &&
            //    (data.ProviderGuid != DnsEventSourceProviderGuid) &&
            //    (data.ProviderGuid != SocketEventSourceProviderGuid) &&
            //    (data.ProviderGuid != HttpEventSourceProviderGuid)
            //    )
            //    return;

            //if (data.ActivityID == Guid.Empty)
            //{
            //    return;
            //}

            //WriteLogLine($"{data.ProcessID,7} > {data.ActivityID}  ({data.ProviderName,16} - {(int)data.Keywords,8:x}) ___[{(int)data.Opcode}|{data.OpcodeName}] {data.EventName}");

            //WriteLogLine($"{data.ActivityID}  ({data.ProviderName,16} - {(int)data.Keywords,4:x}) [{(int)data.Opcode,2:x} | {data.OpcodeName,6}] {data.EventName}");

            //WriteLogLine();

            WriteLog($"{data.ThreadID,6} | {data.ActivityID,16} > event {data.ID,3} __ [{(int)data.Opcode,2}|{data.OpcodeName,6}] {data.EventName}");

            //// show the path corresponding to the ActivityID
            //// handle special ID 0 event corresponding to a message
            //if (data.ID == 0)
            //{
            //    WriteLogLine($"{data.ThreadID,6} | {data.ActivityID,16} = {ActivityHelpers.ActivityPathString(data.ActivityID, 0),16} > event {data.ID,3} __ [{(int)data.Opcode,2}|{data.OpcodeName,6}] {data.FormattedMessage} ");
            //    return;
            //}
            //else
            //{
            //    WriteLog($"{data.ThreadID,6} | {data.ActivityID,16} = {ActivityHelpers.ActivityPathString(data.ActivityID, 0),16} > event {data.ID,3} __ [{(int)data.Opcode,2}|{data.OpcodeName,6}] ");
            //}


            // NOTE: the fields names array is always empty  :^(
            //var fields = data.PayloadNames;
            //if (fields.Length == 0)
            //{
            //    return;
            //}
            //foreach (var field in fields)
            //{
            //    WriteLogLine($"   {field}");
            //}
            //WriteLogLine();

            // NOTE: not event better with dynamic members
            //var fields = data.GetDynamicMemberNames().ToArray();
            //if (fields.Length == 0)
            //{
            //    return;
            //}
            //foreach (var field in fields)
            //{
            //    WriteLogLine($"   {field} = {data.PayloadByName(field)}");
            //}
            //WriteLogLine();

            try
            {
                ParseEvent(
                    data.TimeStamp,
                    data.ThreadID,
                    data.ActivityID,
                    data.RelatedActivityID,
                    data.ProviderGuid,
                    data.TaskName,
                    (Int64)data.Keywords,
                    (UInt16)data.ID,
                    data.EventData()
                    );
            }
            catch (Exception x)
            {
                Console.WriteLine(x.Message);
            }
        }


        private static Guid NetSecurityEventSourceProviderGuid = Guid.Parse("7beee6b1-e3fa-5ddb-34be-1404ad0e2520");
        private static Guid DnsEventSourceProviderGuid = Guid.Parse("4b326142-bfb5-5ed3-8585-7714181d14b0");
        private static Guid SocketEventSourceProviderGuid = Guid.Parse("d5b2e7d4-b6ec-50ae-7cde-af89427ad21f");
        private static Guid HttpEventSourceProviderGuid = Guid.Parse("d30b5633-7ef1-5485-b4e0-94979b102068");
        private static Guid RundownProviderGuid = ClrRundownTraceEventParser.ProviderGuid;

        private void ParseEvent(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            Guid providerGuid,
            string taskName,
            Int64 keywords,
            UInt16 id,
            byte[] eventData
            )
        {
            if (providerGuid == RundownProviderGuid)
            {
                HandleRundownEvents(timestamp, threadId, activityId, relatedActivityId, id, taskName, eventData);
            }
            else
            if (providerGuid == NetSecurityEventSourceProviderGuid)
            {
                HandleNetSecurityEvent(timestamp, threadId, activityId, relatedActivityId, id, taskName, eventData);
            }
            else
            if (providerGuid == DnsEventSourceProviderGuid)
            {
                HandleDnsEvent(timestamp, threadId, activityId, relatedActivityId, id, taskName, eventData);
            }
            else
            if (providerGuid == SocketEventSourceProviderGuid)
            {
                HandleSocketEvent(timestamp, threadId, activityId, relatedActivityId, id, taskName, eventData);
            }
            else
            if (providerGuid == HttpEventSourceProviderGuid)
            {
                HandleHttpEvent(timestamp, threadId, activityId, relatedActivityId, id, taskName, eventData);
            }
            else
            {
                WriteLogLine();
            }
        }

        private void HandleRundownEvents(DateTime timestamp, int threadId, Guid activityId, Guid relatedActivityId, ushort id, string taskName, byte[] eventData)
        {
            if (_isLogging)
            {
                WriteLogLine($"Rundown event {id} - {taskName}");
            }

            switch (id)
            {
                case 143: HandleMethodDCStartVerbose(timestamp, threadId, activityId, relatedActivityId, id, taskName, eventData);

                    break;
            }
        }

        private void HandleMethodDCStartVerbose(DateTime timestamp, int threadId, Guid activityId, Guid relatedActivityId, ushort id, string taskName, byte[] eventData)
        {
            WriteLogLine("MethodDCStartVerbose");
        }

        private void HandleNetSecurityEvent(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            ushort id,
            string taskName,
            byte[] eventData
            )
        {
            switch (id)
            {
                case 1: // HandshakeStart
                    OnHandshakeStart(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 2: // HandshakeStop
                    OnHandshakeStop(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 3: // HandshakeFailed
                    OnHandshakeFailed(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                default:
                    WriteLogLine();
                    break;
            }
        }

        private void OnHandshakeStart(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("HandshakeStart");

            // bool IsServer
            // string targetHost
            EventSourcePayload payload = new EventSourcePayload(eventData);
            bool isServer = payload.GetUInt32() != 0;  // bool is serialized as a UInt32
            var targetHost = payload.GetString();
            WriteLogLine($"    SEC|> {targetHost} - isServer = {isServer}");

            HandshakeStart?.Invoke(this, new HandshakeStartEventArgs(timestamp, threadId, activityId, relatedActivityId, isServer, targetHost));
        }

        private void OnHandshakeStop(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("HandshakeStop");
            // int SslProtocol
            EventSourcePayload payload = new EventSourcePayload(eventData);
            SslProtocolsForEvents protocol = (SslProtocolsForEvents)payload.GetUInt32();
            WriteLogLine($"      <|SEC {protocol}");

             HandshakeStop?.Invoke(this, new HandshakeStopEventArgs(timestamp, threadId, activityId, relatedActivityId, protocol));
        }

        private void OnHandshakeFailed(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("HandshakeFailed");
            // bool IsServer
            // double elapsedMilliseconds
            // string exceptionMessage
            EventSourcePayload payload = new EventSourcePayload(eventData);
            bool isServer = payload.GetUInt32() != 0;  // bool is serialized as a UInt32
            var elapsed = payload.GetDouble();
            var message = payload.GetString();
            WriteLogLine($"   SECx| isServer = {isServer} - {elapsed} ms : {message}");

            HandshakeFailed?.Invoke(this, new HandshakeFailedEventArgs(timestamp, threadId, activityId, relatedActivityId, elapsed, isServer, message));
        }


        private void HandleDnsEvent(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            ushort id,
            string taskName,
            byte[] eventData
            )
        {
            switch (id)
            {
                case 1: // ResolutionStart
                    OnResolutionStart(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 2: // ResolutionStop
                    OnResolutionStop(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 3: // ResolutionFailed
                    OnResolutionFailed(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                default:
                    WriteLogLine();
                    break;
            }
        }

        private void OnResolutionStart(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("ResolutionStart");
            // string hostNameOrAddress
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var address = payload.GetString();
            WriteLogLine($"      R|> {address}");

            DnsResolutionStart?.Invoke(this, new ResolutionStartEventArgs(timestamp, threadId, activityId, relatedActivityId, address));
        }

        private void OnResolutionStop(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("ResolutionStop");
            WriteLogLine($"      <|R");

            DnsResolutionStop?.Invoke(this, new DnsEventArgs(timestamp, threadId, activityId, relatedActivityId));
        }

        private void OnResolutionFailed(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("ResolutionFailed");
            WriteLogLine($"    Rx|");

            DnsResolutionFailed?.Invoke(this, new DnsEventArgs(timestamp, threadId, activityId, relatedActivityId));
        }

        private void HandleSocketEvent(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            ushort id,
            string taskName,
            byte[] eventData
            )
        {
            switch (id)
            {
                case 1: // ConnectStart
                    OnConnectStart(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 2: // ConnectStop
                    OnConnectStop(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 3: // ConnectFailed
                    OnConnectFailed(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 4: // AcceptStart
                    OnAcceptStart(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 5: // AcceptStop
                    OnAcceptStop(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 6: // AcceptFailed
                    OnAcceptFailed(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                default:
                    WriteLogLine();
                    break;
            }
        }

        private void OnConnectStart(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("ConnectStart");

            // string address
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var address = payload.GetString();
            WriteLogLine($"      S|> {address}");

            SocketConnectStart?.Invoke(this, new SocketEventArgs(timestamp, threadId, activityId, relatedActivityId, address));
        }

        private void OnConnectStop(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("ConnectStop");
            WriteLogLine($"      <|S");

            SocketConnectStop?.Invoke(this, new SocketEventArgs(timestamp, threadId, activityId, relatedActivityId));
        }

        private void OnConnectFailed(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("ConnectFailed");
            // string exception message
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var message = payload.GetString();

            WriteLogLine($"    SCx| {message}");

            SocketConnectFailed?.Invoke(this, new SocketEventArgs(timestamp, threadId, activityId, relatedActivityId, message));
        }

        private void OnAcceptStart(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("AcceptStart");
            // string address
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var address = payload.GetString();
            WriteLogLine($"     SA|> {address}");

            SocketAcceptStart?.Invoke(this, new SocketEventArgs(timestamp, threadId, activityId, relatedActivityId, address));
        }

        private void OnAcceptStop(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("AcceptStop");
            WriteLogLine($"      <|SA");

            SocketAcceptStop?.Invoke(this, new SocketEventArgs(timestamp, threadId, activityId, relatedActivityId));
        }

        private void OnAcceptFailed(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("AcceptFailed");
            // string exception message
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var message = payload.GetString();
            WriteLogLine($"    SAx|> {message}");

            SocketAcceptFailed?.Invoke(this, new SocketEventArgs(timestamp, threadId, activityId, relatedActivityId, message));
        }

        private void HandleHttpEvent(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            ushort id,
            string taskName,
            byte[] eventData
            )
        {
            switch(id)
            {
                case 1: // RequestStart
                    OnRequestStart(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 2: // RequestStop
                    OnRequestStop(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 3: // RequestFailed
                    OnRequestFailed(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 4: // ConnectionEstablished
                    OnConnectionEstablished(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 5: // ConnectionClosed
                    OnConnectionClosed(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 6: // RequestLeftQueue
                    OnRequestLeftQueue(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 7: // RequestHeadersStart
                    OnRequestHeadersStart(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 8: // RequestHeadersStop
                    OnRequestHeadersStop(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 9: // RequestContentStart
                    OnRequestContentStart(timestamp, threadId, activityId, relatedActivityId);
                    break;
                case 10: // RequestContentStop
                    OnRequestContentStop(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 11: // ResponseHeadersStart
                    OnResponseHeadersStart(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 12: // ResponseHeadersStop
                    OnResponseHeadersStop(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 13: // ResponseContentStart
                    OnResponseContentStart(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 14: // ResponseContentStop
                    OnResponseContentStop(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 15: // RequestFailedDetailed
                    OnRequestFailedDetailed(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                case 16: // Redirect
                    OnRedirect(timestamp, threadId, activityId, relatedActivityId, eventData);
                    break;
                default:
                    WriteLogLine();
                    break;
            }
        }

        private void OnRequestStart(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("RequestStart");
            // string scheme
            // string host
            // int port
            // string path
            // byte versionMajor
            // byte versionMinor
            // enum HttpVersionPolicy
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var scheme = payload.GetString();
            var host = payload.GetString();
            var port = payload.GetUInt32();
            var path = payload.GetString();
            var versionMajor = payload.GetByte();
            var versionMinor = payload.GetByte();


            if (port != 0)
            {
                WriteLogLine($"       |> {scheme}://{host}:{port}{path}");
            }
            else
            {
                WriteLogLine($"       |> {scheme}://{host}{path}");
            }

            HttpRequestStart?.Invoke(this, new HttpRequestStartEventArgs(timestamp, threadId, activityId, relatedActivityId, scheme, host, port, path, versionMajor, versionMinor));
        }

        private void OnRequestStop(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("RequestStop");
            // int statusCode
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var statusCode = payload.GetUInt32();

            WriteLogLine($"{statusCode,5} <|");

            HttpRequestStop?.Invoke(this, new HttpRequestStopEventArgs(timestamp, threadId, activityId, relatedActivityId, statusCode));
        }

        private void OnRequestFailed(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("RequestFailed");
            // string exception message
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var message = payload.GetString();

            WriteLogLine($"      x| {message}");

            HttpRequestFailed?.Invoke(this, new HttpRequestFailedEventArgs(timestamp, threadId, activityId, relatedActivityId, message));
        }

        private void OnRequestFailedDetailed(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("RequestFailedDetailed");
            // string exception message + call stack
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var message = payload.GetString();

            WriteLogLine($"      x| {message}");
            HttpRequestFailedDetailed?.Invoke(this, new HttpRequestFailedEventArgs(timestamp, threadId, activityId, relatedActivityId, message));
        }

        private void OnConnectionEstablished(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("ConnectionEstablished");
            // byte versionMajor
            // byte versionMinor
            // long connectionId
            // string scheme
            // string host
            // int port
            // string remoteAddress
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var versionMajor = payload.GetByte();
            var versionMinor = payload.GetByte();

            // in .NET 7, nothing else is available
            Int64 connectionId = 0;
            var scheme = "";
            var host = "";
            UInt32 port = 0;
            var path = "";

            if (eventData.Length > 2)
            {
                connectionId = payload.GetInt64();
                scheme = payload.GetString();
                host = payload.GetString();
                port = payload.GetUInt32();
                path = payload.GetString();
            }

            if (port != 0)
            {
                if (path.StartsWith("/"))
                {
                    WriteLogLine($"       |= [{connectionId,3}] {scheme}://{host}:{port}{path}");
                }
                else
                {
                    WriteLogLine($"       |= [{connectionId,3}] {scheme}://{host}:{port}/{path}");
                }
            }
            else
            {
                if (scheme.Length > 0)
                {
                    WriteLogLine($"       |= [{connectionId,3}] {scheme}://{host}{path}");
                    if (path.StartsWith("/"))
                    {
                        WriteLogLine($"       |= [{connectionId,3}] {scheme}://{host}{path}");
                    }
                    else
                    {
                        WriteLogLine($"       |= [{connectionId,3}] {scheme}://{host}/{path}");
                    }
                }
                else
                {
                    WriteLogLine($"       |= [{connectionId,3}] url not available");
                }
            }

            HttpRequestEstablished?.Invoke(this,
                new HttpConnectionEstablishedArgs(
                        timestamp, threadId, activityId, relatedActivityId,
                        scheme, host, port, path, versionMajor, versionMinor, connectionId
                        )
                );
        }

        private void OnConnectionClosed(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("ConnectionClosed");
            // long connectionId
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var connectionId = payload.GetInt64();

            WriteLogLine($"       |< [{connectionId,3}]");

            HttpRequestConnectionClosed?.Invoke(this, new HttpRequestWithConnectionIdEventArgs(timestamp, threadId, activityId, relatedActivityId, connectionId));
        }

        private void OnRequestLeftQueue(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("RequestLeftQueue");
            // double timeOnQueue
            // byte versionMajor
            // byte versionMinor
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var timeOnQueue = payload.GetDouble();
            var versionMajor = payload.GetByte();
            var versionMinor = payload.GetByte();

            WriteLogLine($"       |  wait {timeOnQueue} ms in queue");

            HttpRequestLeftQueue?.Invoke(
                this,
                new HttpRequestLeftQueueEventArgs(
                        timestamp, threadId, activityId, relatedActivityId, timeOnQueue, versionMajor, versionMajor
                        )
                );
        }

        private void OnRequestHeadersStart(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("RequestHeadersStart");

            // in .NET 7, no payload
            Int64 connectionId = 0;

            if (eventData.Length > 0)
            {
                // long connectionId
                EventSourcePayload payload = new EventSourcePayload(eventData);
                connectionId = payload.GetInt64();
            }

            WriteLogLine($"       |QH[{connectionId,3}]");

            HttpRequestHeaderStart?.Invoke(this, new HttpRequestWithConnectionIdEventArgs(timestamp, threadId, activityId, relatedActivityId, connectionId));
        }

        private void OnRequestHeadersStop(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("RequestHeadersStop");
            HttpRequestHeaderStop?.Invoke(this, new EventPipeBaseArgs(timestamp, threadId, activityId, relatedActivityId));
        }

        private void OnRequestContentStart(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId
            )
        {
            WriteLogLine("RequestContentStart");

            HttpRequestContentStart?.Invoke(this, new EventPipeBaseArgs(timestamp, threadId, activityId, relatedActivityId));
        }

        private void OnRequestContentStop(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("RequestContentStop");
            // int statusCode
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var statusCode = payload.GetUInt32();

            WriteLogLine($"{statusCode,5} <|RC");

            HttpRequestContentStop?.Invoke(this, new HttpRequestStatusEventArgs(timestamp, threadId, activityId, relatedActivityId, statusCode));
        }

        private void OnResponseHeadersStart(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("ResponseHeadersStart");
            WriteLogLine($"       |RH>");

            HttpResponseHeaderStart?.Invoke(this, new EventPipeBaseArgs(timestamp, threadId, activityId, relatedActivityId));
        }

        private void OnResponseHeadersStop(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("ResponseHeadersStop");
            // int statusCode
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var statusCode = payload.GetUInt32();

            WriteLogLine($"{statusCode,5} <|RH");

            HttpResponseHeaderStop?.Invoke(this, new HttpRequestStatusEventArgs(timestamp, threadId, activityId, relatedActivityId, statusCode));
        }

        private void OnResponseContentStart(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("ResponseContentStart");
            WriteLogLine($"       |RC>");

            HttpResponseContentStart?.Invoke(this, new EventPipeBaseArgs(timestamp, threadId, activityId, relatedActivityId));
        }

        private void OnResponseContentStop(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("ResponseContentStop");
            WriteLogLine($"      <|RC");

            HttpResponseContentStop?.Invoke(this, new EventPipeBaseArgs(timestamp, threadId, activityId, relatedActivityId));
        }

        private void OnRedirect(
            DateTime timestamp,
            int threadId,
            Guid activityId,
            Guid relatedActivityId,
            byte[] eventData
            )
        {
            WriteLogLine("Redirect");
            // string redirectUrl
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var redirectUrl = payload.GetString();
            WriteLogLine($"      R|> {redirectUrl}");

            HttpRedirect?.Invoke(this, new HttpRedirectEventArgs(timestamp, threadId, activityId, relatedActivityId, redirectUrl));
        }

        private void DumpBytes(byte[] eventData)
        {
            WriteLogLine($"  #bytes = {eventData.Length}");
            //WriteLogLine(BitConverter.ToString(eventData));

            StringBuilder builder = new StringBuilder(16);
            int i = 0;
            for (i = 0; i < eventData.Length; i++)
            {
                if (i % 16 == 0)
                {
                    WriteLog("  | ");
                }

                // write each byte as hexadecimal value followed by possible UTF16 characters
                WriteLog($"{eventData[i]:X2} ");

                if ((i % 2 == 0) && (i + 1 < eventData.Length))
                {
                    var character = UnicodeEncoding.Unicode.GetString(eventData, i, 2)[0];
                    if (
                        //char.IsLetterOrDigit(character) ||
                        //char.IsPunctuation(character) ||
                        //char.IsSeparator(character)
                        (character > 32) && (character < 123)
                        )
                    {
                        builder.Append(character);
                    }
                    else
                    {
                        builder.Append(" ");
                    }
                }

                if ((i + 1) % 16 == 0)
                {
                    WriteLog(" - ");
                    WriteLogLine(builder.ToString());
                    builder.Clear();
                }
            }

            // don't forget to output the last bytes if needed
            if (builder.Length > 0)
            {
                var remaining = i % 16;
                WriteLog(new string(' ', (16 - remaining) * 3));
                WriteLog(" - ");
                WriteLogLine(builder.ToString());
            }
        }

        private void OnGCAllocationTick(GCAllocationTickTraceData data)
        {
            NotifyAllocationTick(data);
        }

        private void OnExceptionStart(ExceptionTraceData data)
        {
            if (data.ProcessID != _processId)
                return;

            NotifyFirstChanceException(data.TimeStamp, data.ProcessID, data.ExceptionType, data.ExceptionMessage);
        }

        private void OnTypeBulkType(GCBulkTypeTraceData data)
        {
            if (data.ProcessID != _processId)
                return;

            // keep track of the id/name type associations
            for (int currentType = 0; currentType < data.Count; currentType++)
            {
                GCBulkTypeValues value = data.Values(currentType);
                _types[value.TypeID] = string.Intern(value.TypeName);
            }
        }
        private void OnGCFinalizeObject(FinalizeObjectTraceData data)
        {
            if (data.ProcessID != _processId)
                return;

            // the type id should have been associated to a name via a previous TypeBulkType event
            NotifyFinalize(data.TimeStamp, data.ProcessID, data.TypeID, _types[data.TypeID]);
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
            NotifyContention(data.TimeStamp, data.ProcessID, data.ThreadID, TimeSpan.FromMilliseconds(contentionDurationMSec), isManaged, callstack);
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
            NotifyContention(data.TimeStamp, data.ProcessID, data.ThreadID, duration, isManaged, callstack);
        }



        private void OnThreadPoolWorkerAdjustment(ThreadPoolWorkerThreadAdjustmentTraceData data)
        {
            var listeners = ThreadPoolStarvation;
            listeners?.Invoke(this, new ThreadPoolStarvationArgs(data.TimeStamp, data.ProcessID, data.NewWorkerThreadCount));
        }


        private void NotifyFirstChanceException(DateTime timestamp, int processId, string typeName, string message)
        {
            var listeners = FirstChanceException;
            listeners?.Invoke(this, new ExceptionArgs(timestamp, processId, typeName, message));
        }
        private void NotifyFinalize(DateTime timeStamp, int processId, ulong typeId, string typeName)
        {
            var listeners = Finalize;
            listeners?.Invoke(this, new FinalizeArgs(timeStamp, processId, typeId, typeName));
        }
        private void NotifyContention(DateTime timeStamp, int processId, int threadId, TimeSpan duration, bool isManaged, List<string> callstack)
        {
            var listeners = Contention;
            listeners?.Invoke(this, new ContentionArgs(timeStamp, processId, threadId, duration, isManaged, callstack));
        }
        private void NotifyCollection(TraceGC gc)
        {
            var listeners = GarbageCollection;
            if (listeners == null)
                return;

            var sizesBefore = GetBeforeGenerationSizes(gc);
            var sizesAfter = GetAfterGenerationSizes(gc);
            listeners?.Invoke(this, new GarbageCollectionArgs(
                _processId,
                gc.StartRelativeMSec,
                gc.Number,
                gc.Generation,
                (GarbageCollectionReason)gc.Reason,
                (GarbageCollectionType)gc.Type,
                !gc.IsNotCompacting(),
                gc.HeapStats.GenerationSize0,
                gc.HeapStats.GenerationSize1,
                gc.HeapStats.GenerationSize2,
                gc.HeapStats.GenerationSize3,
                sizesBefore,
                sizesAfter,
                gc.SuspendDurationMSec,
                gc.PauseDurationMSec,
                gc.BGCFinalPauseMSec
            ));
        }

        private long[] GetBeforeGenerationSizes(TraceGC gc)
        {
            var before = true;
            return GetGenerationSizes(gc, before);
        }
        private long[] GetAfterGenerationSizes(TraceGC gc)
        {
            var after = false;
            return GetGenerationSizes(gc, after);
        }

        private long[] GetGenerationSizes(TraceGC gc, bool before)
        {
            var sizes = new long[4];
            if (gc.PerHeapHistories == null)
            {
                return sizes;
            }

            for (int heap = 0; heap < gc.PerHeapHistories.Count; heap++)
            {
                // LOH = 3
                for (int gen = 0; gen <= 3; gen++)
                {
                    sizes[gen] += before ?
                        gc.PerHeapHistories[heap].GenData[gen].ObjSpaceBefore:
                        gc.PerHeapHistories[heap].GenData[gen].ObjSizeAfter;
                }
            }

            return sizes;
        }

        private void NotifyAllocationTick(GCAllocationTickTraceData info)
        {
            var listeners = AllocationTick;
            listeners?.Invoke(this, new AllocationTickArgs(
                info.TimeStamp,
                info.ProcessID,
                info.AllocationAmount,
                info.AllocationAmount64,
                info.AllocationKind,
                info.TypeName,
                info.HeapIndex,
                info.Address
            ));
        }

        private void WriteLog(string text)
        {
            if (_isLogging)
            {
                Console.Write(text);
            }
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
    }
}
