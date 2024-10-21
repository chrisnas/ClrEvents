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
        private readonly int _processId;
        private readonly TypesInfo _types;
        private readonly ContentionInfoStore _contentionStore;
        private readonly EventFilter _filter;
        private readonly TraceEventSession _session;
        private TplEtwProviderTraceEventParser _TplParser;


        public event EventHandler<ExceptionArgs> FirstChanceException;
        public event EventHandler<FinalizeArgs> Finalize;
        public event EventHandler<ContentionArgs> Contention;
        public event EventHandler<ThreadPoolStarvationArgs> ThreadPoolStarvation;
        public event EventHandler<GarbageCollectionArgs> GarbageCollection;
        public event EventHandler<AllocationTickArgs> AllocationTick;


        // constructor for EventPipe traces
        public ClrEventsManager(int processId, EventFilter filter)
        {
            _processId = processId;
            _types = new TypesInfo();
            _contentionStore = new ContentionInfoStore();
            _contentionStore.AddProcess(processId);
            _filter = filter;
        }

        // constructor for TraceEvent + ETW traces
        public ClrEventsManager(TraceEventSession session, int processId, EventFilter filter)
            : this(processId, filter)
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



        private static IReadOnlyCollection<Provider> GetProviders()
        {
            var providers = new Provider[]
            {
                new Provider(
                    name: "Microsoft-Windows-DotNETRuntime",
                    keywords:   GetKeywords(),
                    eventLevel: EventLevel.Verbose),

                new Provider(
                    name: "System.Net.Http",
                    keywords: (ulong)(1),
                    eventLevel: EventLevel.Verbose),

                new Provider(
                    name: "System.Net.Sockets",
                    keywords: (ulong)(0xFFFFFFFF),
                    eventLevel: EventLevel.Verbose),

                new Provider(
                    name: "System.Net.NameResolution",
                    keywords: (ulong)(0xFFFFFFFF),
                    eventLevel: EventLevel.Verbose),

                new Provider(
                    name: "System.Net.Security",
                    keywords: (ulong)(0xFFFFFFFF),
                    eventLevel: EventLevel.Verbose),

                new Provider(
                    name: "System.Threading.Tasks.TplEventSource",
                    keywords: (ulong)(1 + 2 + 4 + 8 + 0x10 + 0x20 + 0x40 + 0x80 + 0x100),
                    eventLevel: EventLevel.Verbose),

            };

            return providers;
        }

        private static ulong GetKeywords()
        {
            return (ulong)(
                ClrTraceEventParser.Keywords.Contention |           // thread contention timing
                ClrTraceEventParser.Keywords.Threading |            // threadpool events
                ClrTraceEventParser.Keywords.Exception |            // get the first chance exceptions
                ClrTraceEventParser.Keywords.GCHeapAndTypeNames |   // for finalizer type names
                ClrTraceEventParser.Keywords.Type |                 // for TypeBulkType definition of types
                ClrTraceEventParser.Keywords.GC                     // garbage collector details
                );
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
                ((_filter & EventFilter.AllocationTick) == EventFilter.AllocationTick) ?
                    TraceEventLevel.Verbose : TraceEventLevel.Informational,
                GetKeywords(),
                options
            );


            // this is a blocking call until the session is disposed
            _session.Source.Process();
        }

        private void RegisterListeners(TraceEventDispatcher source)
        {
            //if ((_filter & EventFilter.Exception) == EventFilter.Exception)
            //{
            //    // get exceptions
            //    source.Clr.ExceptionStart += OnExceptionStart;
            //}

            //if ((_filter & EventFilter.Finalizer) == EventFilter.Finalizer)
            //{
            //    // get finalizers
            //    source.Clr.TypeBulkType += OnTypeBulkType;
            //    source.Clr.GCFinalizeObject += OnGCFinalizeObject;
            //}

            //if ((_filter & EventFilter.Contention) == EventFilter.Contention)
            //{
            //    // get thread contention time
            //    source.Clr.ContentionStart += OnContentionStart;
            //    source.Clr.ContentionStop += OnContentionStop;
            //}

            //if ((_filter & EventFilter.ThreadStarvation) == EventFilter.ThreadStarvation)
            //{
            //    // detect ThreadPool starvation
            //    source.Clr.ThreadPoolWorkerThreadAdjustmentAdjustment += OnThreadPoolWorkerAdjustment;
            //}

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

            //if ((_filter & EventFilter.AllocationTick) == EventFilter.AllocationTick)
            //{
            //    // sample every ~100 KB of allocations
            //    source.Clr.GCAllocationTick += OnGCAllocationTick;
            //}

            source.AllEvents += OnEvents;

            // TPL events
            _TplParser = new TplEtwProviderTraceEventParser(source);
            _TplParser.TaskScheduledSend += OnTaskScheduledSend;
            _TplParser.TaskExecuteStart += OnTaskExecuteStart;
            _TplParser.TaskExecuteStop += OnTaskExecuteStop;
            _TplParser.AwaitTaskContinuationScheduledSend += OnAwaitTaskContinuationScheduledSend;
            _TplParser.TaskWaitSend += OnTaskWaitSend;
            _TplParser.TaskWaitStop += OnTaskWaitStop;
        }

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

            // skip .NET runtime events
            if (data.ProviderGuid.ToString() == ClrProviderGuid)
                return;

            // skip TPL events
            if (data.ProviderGuid == TplEventSourceGuild)
                return;

            if (data.ActivityID == Guid.Empty)
            {
                return;
            }

            //Console.WriteLine($"{data.ProcessID,7} > {data.ActivityID}  ({data.ProviderName,16} - {(int)data.Keywords,8:x}) ___[{(int)data.Opcode}|{data.OpcodeName}] {data.EventName}");

            //Console.WriteLine($"{data.ActivityID}  ({data.ProviderName,16} - {(int)data.Keywords,4:x}) [{(int)data.Opcode,2:x} | {data.OpcodeName,6}] {data.EventName}");

            //Console.WriteLine();

            //Console.Write($"{data.ThreadID,6} | {data.ActivityID,16} > event {data.ID,3} __ [{(int)data.Opcode,2}|{data.OpcodeName,6}] ");
            Console.Write($"{data.ThreadID,6} | {data.ActivityID,16} = {ActivityHelpers.ActivityPathString(data.ActivityID, 0),16} > event {data.ID,3} __ [{(int)data.Opcode,2}|{data.OpcodeName,6}] ");

            // NOTE: the fields names array is always empty  :^(
            //var fields = data.PayloadNames;
            //if (fields.Length == 0)
            //{
            //    return;
            //}
            //foreach (var field in fields)
            //{
            //    Console.WriteLine($"   {field}");
            //}
            //Console.WriteLine();

            // NOTE: not event better with dynamic members
            //var fields = data.GetDynamicMemberNames().ToArray();
            //if (fields.Length == 0)
            //{
            //    return;
            //}
            //foreach (var field in fields)
            //{
            //    Console.WriteLine($"   {field} = {data.PayloadByName(field)}");
            //}
            //Console.WriteLine();

            ParseEvent(data.ProviderGuid, data.TaskName, (Int64)data.Keywords, (UInt16)data.ID, data.EventData());
        }


        private static Guid NetSecurityEventSourceProviderGuid = Guid.Parse("7beee6b1-e3fa-5ddb-34be-1404ad0e2520");
        private static Guid DnsEventSourceProviderGuid = Guid.Parse("4b326142-bfb5-5ed3-8585-7714181d14b0");
        private static Guid SocketEventSourceProviderGuid = Guid.Parse("d5b2e7d4-b6ec-50ae-7cde-af89427ad21f");
        private static Guid HttpEventSourceProviderGuid = Guid.Parse("d30b5633-7ef1-5485-b4e0-94979b102068");

        private void ParseEvent(Guid providerGuid, string taskName, Int64 keywords, UInt16 id, byte[] eventData)
        {
            if (providerGuid == NetSecurityEventSourceProviderGuid)
            {
                HandleNetSecurityEvent(id, taskName, eventData);
            }
            else
            if (providerGuid == DnsEventSourceProviderGuid)
            {
                HandleDnsEvent(id, taskName, eventData);
            }
            else
            if (providerGuid == SocketEventSourceProviderGuid)
            {
                HandleSocketEvent(id, taskName, eventData);
            }
            else
            if (providerGuid == HttpEventSourceProviderGuid)
            {
                HandleHttpEvent(id, taskName, eventData);
            }
            else
            {
                Console.WriteLine();
            }
        }

        private void HandleNetSecurityEvent(ushort id, string taskName, byte[] eventData)
        {
            switch (id)
            {
                case 1: // HandshakeStart
                    OnHandshakeStart(eventData);
                    break;
                case 2: // HandshakeStop
                    OnHandshakeStop(eventData);
                    break;
                case 3: // HandshakeFailed
                    OnHandshakeFailed(eventData);
                    break;
                default:
                    Console.WriteLine();
                    break;
            }
        }

        private void OnHandshakeStart(byte[] eventData)
        {
            Console.WriteLine("HandshakeStart");

            // bool IsServer
            // string targetHost
            EventSourcePayload payload = new EventSourcePayload(eventData);
            bool isServer = payload.GetUInt32() != 0;  // bool is serialized as a UInt32
            var targetHost = payload.GetString();
            Console.WriteLine($"    SEC|> {targetHost} - isServer = {isServer}");
        }

        //
        // Summary:
        //     Defines the possible versions of System.Security.Authentication.SslProtocols.
        [Flags]
        public enum SslProtocolsForEvents
        {
            //
            // Summary:
            //     Allows the operating system to choose the best protocol to use, and to block
            //     protocols that are not secure. Unless your app has a specific reason not to,
            //     you should use this field.
            None = 0,
            //
            // Summary:
            //     Specifies the SSL 2.0 protocol. SSL 2.0 has been superseded by the TLS protocol
            //     and is provided for backward compatibility only.
            Ssl2 = 12,
            //
            // Summary:
            //     Specifies the SSL 3.0 protocol. SSL 3.0 has been superseded by the TLS protocol
            //     and is provided for backward compatibility only.
            Ssl3 = 48,
            //
            // Summary:
            //     Specifies the TLS 1.0 security protocol. TLS 1.0 is provided for backward compatibility
            //     only. The TLS protocol is defined in IETF RFC 2246. This member is obsolete starting
            //     in .NET 7.
            Tls = 192,
            //
            // Summary:
            //     Use None instead of Default. Default permits only the Secure Sockets Layer (SSL)
            //     3.0 or Transport Layer Security (TLS) 1.0 protocols to be negotiated, and those
            //     options are now considered obsolete. Consequently, Default is not allowed in
            //     many organizations. Despite the name of this field, System.Net.Security.SslStream
            //     does not use it as a default except under special circumstances.
            Default = 240,
            //
            // Summary:
            //     Specifies the TLS 1.1 security protocol. The TLS protocol is defined in IETF
            //     RFC 4346. This member is obsolete starting in .NET 7.
            Tls11 = 768,
            //
            // Summary:
            //     Specifies the TLS 1.2 security protocol. The TLS protocol is defined in IETF
            //     RFC 5246.
            Tls12 = 3072,
            //
            // Summary:
            //     Specifies the TLS 1.3 security protocol. The TLS protocol is defined in IETF
            //     RFC 8446.
            Tls13 = 12288
        }

        private void OnHandshakeStop(byte[] eventData)
        {
            Console.WriteLine("HandshakeStop");
            // int SslProtocol
            EventSourcePayload payload = new EventSourcePayload(eventData);
            SslProtocolsForEvents protocol = (SslProtocolsForEvents)payload.GetUInt32();
            Console.WriteLine($"      <|SEC {protocol}");
        }

        private void OnHandshakeFailed(byte[] eventData)
        {
            Console.WriteLine("HandshakeFailed");
            // bool IsServer
            // double elapsedMilliseconds
            // string exceptionMessage
            EventSourcePayload payload = new EventSourcePayload(eventData);
            bool isServer = payload.GetUInt32() != 0;  // bool is serialized as a UInt32
            var elapsed = payload.GetDouble();
            var message = payload.GetString();
            Console.WriteLine($"   SECx| isServer = {isServer} - {elapsed} ms : {message}");
        }


        private void HandleDnsEvent(ushort id, string taskName, byte[] eventData)
        {
            switch (id)
            {
                case 1: // ResolutionStart
                    OnResolutionStart(eventData);
                    break;
                case 2: // ResolutionStop
                    OnResolutionStop(eventData);
                    break;
                case 3: // ResolutionFailed
                    OnResolutionFailed(eventData);
                    break;
                default:
                    Console.WriteLine();
                    break;
            }
        }

        private void OnResolutionStart(byte[] eventData)
        {
            Console.WriteLine("ResolutionStart");
            // string hostNameOrAddress
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var address = payload.GetString();
            Console.WriteLine($"      R|> {address}");
        }

        private void OnResolutionStop(byte[] eventData)
        {
            Console.WriteLine("ResolutionStop");
            Console.WriteLine($"      <|R");
        }

        private void OnResolutionFailed(byte[] eventData)
        {
            Console.WriteLine("ResolutionFailed");
            Console.WriteLine($"    Rx|");
        }

        private void HandleSocketEvent(ushort id, string taskName, byte[] eventData)
        {
            switch (id)
            {
                case 1: // ConnectStart
                    OnConnectStart(eventData);
                    break;
                case 2: // ConnectStop
                    OnConnectStop(eventData);
                    break;
                case 3: // ConnectFailed
                    OnConnectFailed(eventData);
                    break;
                case 4: // AcceptStart
                    OnAcceptStart(eventData);
                    break;
                case 5: // AcceptStop
                    OnAcceptStop(eventData);
                    break;
                case 6: // AcceptFailed
                    OnAcceptFailed(eventData);
                    break;
                default:
                    Console.WriteLine();
                    break;
            }
        }

        private void OnConnectStart(byte[] eventData)
        {
            Console.WriteLine("ConnectStart");

            // string address
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var address = payload.GetString();
            Console.WriteLine($"      S|> {address}");
        }

        private void OnConnectStop(byte[] eventData)
        {
            Console.WriteLine("ConnectStop");
            Console.WriteLine($"      <|S");
        }

        private void OnConnectFailed(byte[] eventData)
        {
            Console.WriteLine("ConnectFailed");
            // string exception message
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var message = payload.GetString();

            Console.WriteLine($"    SCx| {message}");
        }

        private void OnAcceptStart(byte[] eventData)
        {
            Console.WriteLine("AcceptStart");
            // string address
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var address = payload.GetString();
            Console.WriteLine($"     SA|> {address}");
        }

        private void OnAcceptStop(byte[] eventData)
        {
            Console.WriteLine("AcceptStop");
            Console.WriteLine($"      <|SA");
        }

        private void OnAcceptFailed(byte[] eventData)
        {
            Console.WriteLine("AcceptFailed");
            // string exception message
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var message = payload.GetString();
            Console.WriteLine($"    SAx|> {message}");
        }

        private Dictionary<Guid, String> _states = new Dictionary<Guid, string>();

        private void HandleHttpEvent(ushort id, string taskName, byte[] eventData)
        {
            switch(id)
            {
                case 1: // RequestStart
                    OnRequestStart(eventData);
                    break;
                case 2: // RequestStop
                    OnRequestStop(eventData);
                    break;
                case 3: // RequestFailed
                    OnRequestFailed(eventData);
                    break;
                case 4: // ConnectionEstablished
                    OnConnectionEstablished(eventData);
                    break;
                case 5: // ConnectionClosed --
                    Console.WriteLine("ConnectionClosed");
                    break;
                case 6: // RequestLeftQueue
                    OnRequestLeftQueue(eventData);
                    break;
                case 7: // RequestHeadersStart
                    OnRequestHeadersStart(eventData);
                    break;
                case 8: // RequestHeadersStop
                    OnRequestHeadersStop(eventData);
                    break;
                case 9: // RequestContentStart --
                    Console.WriteLine("RequestContentStart");
                    break;
                case 10: // RequestContentStop --
                    Console.WriteLine("RequestContentStop");
                    break;
                case 11: // ResponseHeadersStart
                    OnResponseHeadersStart(eventData);
                    break;
                case 12: // ResponseHeadersStop
                    OnResponseHeadersStop(eventData);
                    break;
                case 13: // ResponseContentStart
                    OnResponseContentStart(eventData);
                    break;
                case 14: // ResponseContentStop
                    OnResponseContentStop(eventData);
                    break;
                case 15: // RequestFailedDetailed --
                    OnRequestFailedDetailed(eventData);
                    break;
                case 16: // Redirect
                    OnRedirect(eventData);
                    break;
                default:
                    Console.WriteLine();
                    break;
            }
        }

        private void OnRequestStart(byte[] eventData)
        {
            //DumpBytes(eventData);

            Console.WriteLine("RequestStart");
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
                Console.WriteLine($"       |> {scheme}://{host}:{port}{path}");
            }
            else
            {
                Console.WriteLine($"       |> {scheme}://{host}{path}");
            }
        }

        private void OnRequestStop(byte[] eventData)
        {
            //DumpBytes(eventData);

            Console.WriteLine("RequestStop");
            // int statusCode
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var statusCode = payload.GetUInt32();

            Console.WriteLine($"{statusCode,5} <|");  // TODO: how to get the original request?  Could it be based on the thread ID?
        }

        private void OnRequestFailed(byte[] eventData)
        {
            Console.WriteLine("RequestFailed");
            // string exception message
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var message = payload.GetString();

            Console.WriteLine($"      x| {message}");
        }

        private void OnRequestFailedDetailed(byte[] eventData)
        {
            Console.WriteLine("RequestFailedDetailed");
            // string exception message
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var message = payload.GetString();

            Console.WriteLine($"      x| {message}");
        }

        private void OnConnectionEstablished(byte[] eventData)
        {
            Console.WriteLine("ConnectionEstablished");
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
            var connectionId = payload.GetUInt64();
            var scheme = payload.GetString();
            var host = payload.GetString();
            var port = payload.GetUInt32();
            var path = payload.GetString();

            if (port != 0)
            {
                if (path.StartsWith("/"))
                {
                    Console.WriteLine($"       |= [{connectionId,3}] {scheme}://{host}:{port}{path}");
                }
                else
                {
                    Console.WriteLine($"       |= [{connectionId,3}] {scheme}://{host}:{port}/{path}");
                }
            }
            else
            {
                Console.WriteLine($"       |= [{connectionId,3}] {scheme}://{host}{path}");
                if (path.StartsWith("/"))
                {
                    Console.WriteLine($"       |= [{connectionId,3}] {scheme}://{host}{path}");
                }
                else
                {
                    Console.WriteLine($"       |= [{connectionId,3}] {scheme}://{host}/{path}");
                }
            }
        }

        private void OnRequestLeftQueue(byte[] eventData)
        {
            Console.WriteLine("RequestLeftQueue");
            // double timeOnQueue
            // byte versionMajor
            // byte versionMinor
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var timeOnQueue = payload.GetDouble();
            var versionMajor = payload.GetByte();
            var versionMinor = payload.GetByte();

            Console.WriteLine($"       |  wait {timeOnQueue} ms in queue");
        }

        private void OnRequestHeadersStart(byte[] eventData)
        {
            Console.WriteLine("RequestHeadersStart");
            // long connectionId
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var connectionId = payload.GetUInt64();

            Console.WriteLine($"       |QH[{connectionId,3}]");
        }

        private void OnRequestHeadersStop(byte[] eventData)
        {
            Console.WriteLine("RequestHeadersStop");
            Console.WriteLine($"      <|QH");
        }

        private void OnResponseHeadersStart(byte[] eventData)
        {
            Console.WriteLine("ResponseHeadersStart");
            Console.WriteLine($"       |RH>");
        }

        private void OnResponseHeadersStop(byte[] eventData)
        {
            Console.WriteLine("ResponseHeadersStop");
            // int statusCode
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var statusCode = payload.GetUInt32();

            Console.WriteLine($"{statusCode,5} <|RH");
        }

        private void OnResponseContentStart(byte[] eventData)
        {
            Console.WriteLine("ResponseContentStart");
            Console.WriteLine($"       |RC>");
        }

        private void OnResponseContentStop(byte[] eventData)
        {
            Console.WriteLine("ResponseContentStop");
            Console.WriteLine($"      <|RC");
        }

        private void OnRedirect(byte[] eventData)
        {
            // string redirectUrl
            EventSourcePayload payload = new EventSourcePayload(eventData);
            var redirectUrl = payload.GetString();
            Console.WriteLine($"      R|> {redirectUrl}");
        }

        private void DumpBytes(byte[] eventData)
        {
            Console.WriteLine($"  #bytes = {eventData.Length}");
            //Console.WriteLine(BitConverter.ToString(eventData));

            StringBuilder builder = new StringBuilder(16);
            int i = 0;
            for (i = 0; i < eventData.Length; i++)
            {
                if (i % 16 == 0)
                {
                    Console.Write("  | ");
                }

                // write each byte as hexadecimal value followed by possible UTF16 characters
                Console.Write($"{eventData[i]:X2} ");

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
                    Console.Write(" - ");
                    Console.WriteLine(builder);
                    builder.Clear();
                }
            }

            // don't forget to output the last bytes if needed
            if (builder.Length > 0)
            {
                var remaining = i % 16;
                Console.Write(new string(' ', (16 - remaining) * 3));
                Console.Write(" - ");
                Console.WriteLine(builder);
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

        private void OnContentionStart(ContentionTraceData data)
        {
            ContentionInfo info = _contentionStore.GetContentionInfo(data.ProcessID, data.ThreadID);
            if (info == null)
                return;

            info.TimeStamp = data.TimeStamp;
            info.ContentionStartRelativeMSec = data.TimeStampRelativeMSec;
        }
        private void OnContentionStop(ContentionTraceData data)

{
            ContentionInfo info = _contentionStore.GetContentionInfo(data.ProcessID, data.ThreadID);
            if (info == null)
                return;

            // unlucky case when we start to listen just after the ContentionStart event
            if (info.ContentionStartRelativeMSec == 0)
            {
                return;
            }

            var contentionDurationMSec = data.TimeStampRelativeMSec - info.ContentionStartRelativeMSec;
            info.ContentionStartRelativeMSec = 0;
            var isManaged = (data.ContentionFlags == ContentionFlags.Managed);
            NotifyContention(data.TimeStamp, data.ProcessID, data.ThreadID, TimeSpan.FromMilliseconds(contentionDurationMSec), isManaged);
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
        private void NotifyContention(DateTime timeStamp, int processId, int threadId, TimeSpan duration, bool isManaged)
        {
            var listeners = Contention;
            listeners?.Invoke(this, new ContentionArgs(timeStamp, processId, threadId, duration, isManaged));
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
    }
}
