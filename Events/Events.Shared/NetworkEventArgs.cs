using System;

namespace Shared
{
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

    public class EventPipeBaseArgs
    {
        public EventPipeBaseArgs(DateTime timestamp, int threadId, Guid activityId, Guid relatedActivityId)
        {
            Timestamp = timestamp;
            ThreadId = threadId;
            ActivityId = activityId;
            RelatedActivityId = relatedActivityId;
        }

        public DateTime Timestamp { get; set; }
        public int ThreadId { get; set; }
        public Guid ActivityId { get; private set; }
        public Guid RelatedActivityId { get; private set; }
    }


    public class HandshakeStartEventArgs : EventPipeBaseArgs
    {
        internal HandshakeStartEventArgs(DateTime timestamp, int threadId, Guid activityId, Guid relatedActivityId, bool isServer, string targetHost)
        :
        base(timestamp, threadId, activityId, relatedActivityId)
        {
            IsServer = isServer;
            TargetHost = targetHost;
        }

        public bool IsServer { get; set; }
        public string TargetHost { get; set; }
    }

    public class HandshakeStopEventArgs : EventPipeBaseArgs
    {
        public HandshakeStopEventArgs(DateTime timestamp, int threadId, Guid activityId, Guid relatedActivityId, SslProtocolsForEvents protocol)
        :
        base(timestamp, threadId, activityId, relatedActivityId)
        {
            Protocol = protocol;
        }

        public SslProtocolsForEvents Protocol { get; set; }
    }

    public class HandshakeFailedEventArgs : EventPipeBaseArgs
    {
        public HandshakeFailedEventArgs(DateTime timestamp, int threadId, Guid activityId, Guid relatedActivityId, double elapsedMilliseconds, bool isServer, string message)
        :
        base(timestamp, threadId, activityId, relatedActivityId)
        {
            ElapsedMilliseconds = elapsedMilliseconds;
            IsServer = isServer;
            Message = message;
        }

        public double ElapsedMilliseconds { get; set; }
        public  bool IsServer { get; set; }
        public string Message { get; set; }
    }

    public class DnsEventArgs : EventPipeBaseArgs
    {
        public DnsEventArgs(DateTime timestamp, int threadId, Guid activityId, Guid relatedActivityId)
        :
        base(timestamp, threadId, activityId, relatedActivityId)
        {
        }
    }

    public class ResolutionStartEventArgs : DnsEventArgs
    {
        public ResolutionStartEventArgs(DateTime timestamp, int threadId, Guid activityId, Guid relatedActivityId, string address)
        :
        base(timestamp, threadId, activityId, relatedActivityId)
        {
            Address = address;
        }

        public string Address { get; set; }
    }

    public class SocketEventArgs : EventPipeBaseArgs
    {
        public SocketEventArgs(DateTime timestamp, int threadId, Guid activityId, Guid relatedActivityId, string info = null)
        :
        base(timestamp, threadId, activityId, relatedActivityId)
        {
            Info = info;
        }

        public string Info { get; set; }
    }

    public class HttpRequestStartEventArgs : EventPipeBaseArgs
    {
        public HttpRequestStartEventArgs(
            DateTime timestamp, int threadId, Guid activityId, Guid relatedActivityId,
            string scheme, string host,
            UInt32 port, string path,
            byte versionMajor, byte versionMinor
            )
        :
        base(timestamp, threadId, activityId, relatedActivityId)
        {
            Scheme = scheme;
            Host = host;
            Port = port;
            Path = path;
            VersionMajor = versionMajor;
            VersionMinor = versionMinor;
        }

        public string Scheme { get; set; }
        public string Host { get; set; }
        public UInt32 Port { get; set; }
        public string Path { get; set; }
        public byte VersionMajor { get; set; }
        public byte VersionMinor { get; set; }
    }

    public class HttpRequestStopEventArgs : EventPipeBaseArgs
    {
        public HttpRequestStopEventArgs(DateTime timestamp, int threadId, Guid activityId, Guid relatedActivityId, UInt32 statusCode)
        :
        base(timestamp, threadId, activityId, relatedActivityId)
        {
            StatusCode = statusCode;
        }

        public UInt32 StatusCode { get; set; }
    }

    public class HttpRequestFailedEventArgs : EventPipeBaseArgs
    {
        public HttpRequestFailedEventArgs(DateTime timestamp, int threadId, Guid activityId, Guid relatedActivityId, string messageWithException)
        :
        base(timestamp, threadId, activityId, relatedActivityId)
        {
            MessageWithException = messageWithException;
        }

        public string MessageWithException { get; set; }
    }

    public class HttpConnectionEstablishedArgs : EventPipeBaseArgs
    {
        public HttpConnectionEstablishedArgs(
            DateTime timestamp, int threadId, Guid activityId, Guid relatedActivityId,
            string scheme, string host,
            UInt32 port, string remoteAddress,
            byte versionMajor, byte versionMinor,
            Int64 connectionId
            )
        :
        base(timestamp, threadId, activityId, relatedActivityId)
        {
            Scheme = scheme;
            Host = host;
            Port = port;
            RemoteAddress = remoteAddress;
            VersionMajor = versionMajor;
            VersionMinor = versionMinor;
            ConnectionId = connectionId;
        }

        public string Scheme { get; set; }
        public string Host { get; set; }
        public UInt32 Port { get; set; }
        public string RemoteAddress { get; set; }
        public byte VersionMajor { get; set; }
        public byte VersionMinor { get; set; }
        public Int64 ConnectionId { get; set; }
    }

    public class HttpRequestWithConnectionIdEventArgs : EventPipeBaseArgs
    {
        public HttpRequestWithConnectionIdEventArgs(DateTime timestamp, int threadId, Guid activityId, Guid relatedActivityId, long connectionId)
        :
        base(timestamp, threadId, activityId, relatedActivityId)
        {
            ConnectionId = connectionId;
        }

        public long ConnectionId { get; set; }
    }

    public class HttpRequestLeftQueueEventArgs : EventPipeBaseArgs
    {
        public HttpRequestLeftQueueEventArgs(
            DateTime timestamp, int threadId, Guid activityId, Guid relatedActivityId,
            double timeOnQueue, byte versionMajor, byte versionMinor
            )
        :
        base(timestamp, threadId, activityId, relatedActivityId)
        {
            TimeOnQueue = timeOnQueue;
            VersionMajor = versionMajor;
            VersionMinor = versionMinor;
        }

        public double TimeOnQueue { get; set; }
        public byte VersionMajor { get; set; }
        public byte VersionMinor { get; set; }
    }

    public class HttpRequestStatusEventArgs : EventPipeBaseArgs
    {
        public HttpRequestStatusEventArgs(DateTime timestamp, int threadId, Guid activityId, Guid relatedActivityId, UInt32 statusCode)
        :
        base(timestamp, threadId, activityId, relatedActivityId)
        {
            StatusCode = statusCode;
        }

        public UInt32 StatusCode { get; set; }
    }

    public class HttpRedirectEventArgs : EventPipeBaseArgs
    {
        public HttpRedirectEventArgs(DateTime timestamp, int threadId, Guid activityId, Guid relatedActivityId, string redirectUrl)
        :
        base(timestamp, threadId, activityId, relatedActivityId)
        {
            RedirectUrl = redirectUrl;
        }

        public string RedirectUrl { get; set; }
    }
}
