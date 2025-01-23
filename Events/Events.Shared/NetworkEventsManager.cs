using System;
using System.Collections.Generic;

namespace Shared
{
    public class HttpRequestEventArgs : EventArgs
    {
        public string Url { get; set; }
        public  DateTime Duration { get; set; }
        public  UInt32 StatusCode { get; set; }
    }

    public class NetworkEventsManager
    {
        public NetworkEventsManager(ClrEventsManager source, bool isLogging = false)
        {
            _isLogging = isLogging;

            SetupListeners(source);
        }

        private void SetupListeners(ClrEventsManager source)
        {
            source.HttpRequestStart += OnHttpRequestStart;
            source.HttpRequestStop += OnHttpRequestStop;
            source.HttpRequestFailed += OnHttpRequestFailed;
            source.HttpRequestLeftQueue += OnHttpRequestLeftQueue;
            source.HttpRedirect += OnHttpRedirect;

            source.DnsResolutionStart += OnDnsResolutionStart;
            source.DnsResolutionStop += OnDnsResolutionStop;
            source.DnsResolutionFailed += OnDnsResolutionStop;

            source.SocketConnectStart += OnSocketConnectStart;
            source.SocketConnectStop += OnSocketConnectStop;
            source.SocketConnectFailed += OnSocketConnectStop;
            // Note: SocketAcceptXXX events provide limited information about incoming connections (duration and error if any)

            source.HandshakeStart += OnHandshakeStart;
            source.HandshakeStop += OnHandshakeStop;
            source.HandshakeFailed += OnHandshakeFailed;

            source.HttpRequestHeaderStart += OnHttpRequestHeaderStart;
            source.HttpResponseHeaderStop += OnHttpResponseHeaderStop;
            source.HttpResponseContentStop += OnHttpResponseContentStop;
        }

        private void OnHttpRequestStart(object sender, HttpRequestStartEventArgs e)
        {
            var requestInfo = new HttpRequestInfo(e.Timestamp, e.Scheme, e.Host, e.Port, e.Path);
            var root = ActivityHelpers.ActivityPathString(e.ActivityId);
            if (_requests.TryGetValue(root, out HttpRequestInfo existingRequest))
            {
                Console.WriteLine($"      ? {existingRequest.Url}");
                return;
            }

            _requests.Add(root, requestInfo);
        }


        private void DumpDurations(HttpRequestInfo info)
        {
            double dnsDuration = info.DnsDuration + ((info.Redirect != null) ? info.Redirect.DnsDuration : 0);
            if (dnsDuration > 0)
            {
                double dnsWait = info.DnsWait + ((info.Redirect != null) ? info.Redirect.DnsWait : 0);
                Console.Write($"{dnsWait,9:F3} | {dnsDuration,9:F3} | ");
            }
            else
            {
                Console.Write($"          |           | ");
            }

            double socketDuration = info.SocketDuration + ((info.Redirect != null) ? info.Redirect.SocketDuration : 0);
            if (socketDuration > 0)
            {
                double wait = info.SocketWait + ((info.Redirect != null) ? info.Redirect.SocketWait : 0);
                Console.Write($"{wait,9:F3} | {socketDuration,9:F3} | ");
            }
            else
            {
                Console.Write($"          |           | ");
            }

            double handshakeDuration = info.HandshakeDuration + ((info.Redirect != null) ? info.Redirect.HandshakeDuration : 0);
            if (handshakeDuration > 0)
            {
                double wait = info.HandshakeWait + ((info.Redirect != null) ? info.Redirect.HandshakeWait : 0);
                Console.Write($"{wait,9:F3} | {handshakeDuration,9:F3} | ");
            }
            else
            {
                Console.Write($"          |           | ");
            }

            //// TODO: try to understand what this duration means
            //if (info.QueueingDuration > 0)
            //{
            //    Console.Write($"{info.QueueingDuration,9:F3} | ");
            //}
            //else
            //{
            //    Console.Write($"          | ");
            //}

            double reqRespDuration = info.ReqRespDuration + ((info.Redirect != null) ? info.Redirect.ReqRespDuration : 0);
            if (reqRespDuration > 0)
            {
                //double wait = 0;
                //if (info.QueueingDuration > 0)
                //{
                //    wait = (info.ReqRespStartTime - info.QueueuingEndTime).TotalMilliseconds;
                //}
                //else
                //if (info.HandshakeDuration > 0)
                //{
                //    wait = (info.ReqRespStartTime - info.HandshakeStartTime).TotalMilliseconds - info.HandshakeDuration;
                //}
                //else
                //if (info.SocketDuration > 0)
                //{
                //    wait = (info.ReqRespStartTime - info.SocketConnectionStartTime).TotalMilliseconds - info.SocketDuration;
                //}
                //else
                //if (info.DnsDuration > 0)
                //{
                //    wait = (info.ReqRespStartTime - info.DnsStartTime).TotalMilliseconds - info.DnsDuration;
                //}
                //else
                //{
                //    wait = (info.ReqRespStartTime - info.StartTime).TotalMilliseconds;
                //}
                //Console.Write($"{wait,9:F3} | {info.ReqRespDuration,9:F3}");

                Console.Write($"{reqRespDuration,9:F3}");
            }
            else
            {
                // TODO: this should never happen
                Console.Write($"           ");
            }
        }

        private void OnHttpRequestStop(object sender, HttpRequestStopEventArgs e)
        {
            var root = ActivityHelpers.ActivityPathString(e.ActivityId);
            if (!_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                return;
            }

            Console.Write($"   {e.StatusCode,3} | {(e.Timestamp - info.StartTime).TotalMilliseconds,9:F3} | ");
            DumpDurations(info);
            Console.Write($" - {info.Url}");
            if (info.Redirect != null)
            {
                Console.Write($" -> {info.Redirect.Url}");
            }

            if (info.Error != null)
            {
                Console.WriteLine($" ~ {info.Error}");
            }
            else
            {
                Console.WriteLine();
            }

            _requests.Remove(root);
        }

        private void OnHttpRequestFailed(object sender, HttpRequestFailedEventArgs e)
        {
            var root = ActivityHelpers.ActivityPathString(e.ActivityId);
            if (_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                info.Error = e.Text;
            }
        }

        private void OnDnsResolutionStart(object sender, ResolutionStartEventArgs e)
        {
            var root = GetRoot(e.ActivityId);
            if (!_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                return;
            }

            if (info.Redirect == null)
            {
                info.DnsStartTime = e.Timestamp;
            }
            else
            {
                info.Redirect.DnsStartTime = e.Timestamp;
            }
        }

        private void OnDnsResolutionStop(object sender, DnsEventArgs e)
        {
            var root = GetRoot(e.ActivityId);
            if (!_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                return;
            }

            if (info.Redirect == null)
            {
                info.DnsDuration = (e.Timestamp - info.DnsStartTime).TotalMilliseconds;
                info.DnsWait = (info.DnsStartTime - info.StartTime).TotalMilliseconds;
            }
            else
            {
                info.Redirect.DnsDuration = (e.Timestamp - info.Redirect.DnsStartTime).TotalMilliseconds;
                info.DnsWait = (info.Redirect.DnsStartTime - info.Redirect.StartTime).TotalMilliseconds;
            }
        }

        private void OnSocketConnectStart(object sender, SocketEventArgs e)
        {
            var root = GetRoot(e.ActivityId);
            if (!_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                return;
            }

            if (info.Redirect == null)
            {
                info.SocketConnectionStartTime = e.Timestamp;
            }
            else
            {
                info.Redirect.SocketConnectionStartTime = e.Timestamp;
            }
        }

        private void OnSocketConnectStop(object sender, SocketEventArgs e)
        {
            var root = GetRoot(e.ActivityId);
            if (!_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                return;
            }

            if (info.Redirect == null)
            {
                info.SocketDuration = (e.Timestamp - info.SocketConnectionStartTime).TotalMilliseconds;
                if (info.DnsDuration > 0)
                {
                    info.SocketWait = (info.SocketConnectionStartTime - info.DnsStartTime).TotalMilliseconds - info.DnsDuration;
                }
                else
                {
                    info.SocketWait = (info.SocketConnectionStartTime - info.StartTime).TotalMilliseconds;

                }
            }
            else
            {
                info.Redirect.SocketDuration = (e.Timestamp - info.Redirect.SocketConnectionStartTime).TotalMilliseconds;
                if (info.Redirect.DnsDuration > 0)
                {
                    info.Redirect.SocketWait = (info.Redirect.SocketConnectionStartTime - info.Redirect.DnsStartTime).TotalMilliseconds - info.Redirect.DnsDuration;
                }
                else
                {
                    info.Redirect.SocketWait = (info.Redirect.SocketConnectionStartTime - info.Redirect.StartTime).TotalMilliseconds;
                }
            }
        }

        private void OnHttpRequestLeftQueue(object sender, HttpRequestLeftQueueEventArgs e)
        {
            // since this is an Info event, the activityID is the root
            var root = ActivityHelpers.ActivityPathString(e.ActivityId);
            if (!_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                return;
            }

            if (info.Redirect == null)
            {
                info.QueueuingEndTime = e.Timestamp;
                info.QueueingDuration = e.TimeOnQueue;
            }
            else
            {
                info.Redirect.QueueuingEndTime = e.Timestamp;
                info.Redirect.QueueingDuration = e.TimeOnQueue;
            }
        }

        // not emitted in .NET 7
        private void OnHttpRedirect(object sender, HttpRedirectEventArgs e)
        {
            // since this is an Info event, the activityID is the root
            var root = ActivityHelpers.ActivityPathString(e.ActivityId);
            if (_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                info.Redirect.Url = e.RedirectUrl;
            }
        }

        private void OnHandshakeStart(object sender, HandshakeStartEventArgs e)
        {
            var root = GetRoot(e.ActivityId);
            if (!_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                return;
            }

            if (info.Redirect == null)
            {
                info.HandshakeStartTime = e.Timestamp;
            }
            else
            {
                info.Redirect.HandshakeStartTime = e.Timestamp;
            }
        }

        private void OnHandshakeStop(object sender, HandshakeStopEventArgs e)
        {
            var root = GetRoot(e.ActivityId);
            if (_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                UpdateHandshakeDuration(info, e.Timestamp);
                UpdateHandshakeWait(info);
            }
        }

        private void OnHandshakeFailed(object sender, HandshakeFailedEventArgs e)
        {
            var root = GetRoot(e.ActivityId);
            if (_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                UpdateHandshakeDuration(info, e.Timestamp);
                UpdateHandshakeWait(info);

                info.HandshakeErrorMessage = e.Message;
            }
        }

        private void UpdateHandshakeDuration(HttpRequestInfo info, DateTime timestamp)
        {
            if (info.Redirect == null)
            {
                info.HandshakeDuration = (timestamp - info.HandshakeStartTime).TotalMilliseconds;
            }
            else
            {
                info.Redirect.HandshakeDuration = (timestamp - info.Redirect.HandshakeStartTime).TotalMilliseconds;
            }
        }

        private void UpdateHandshakeWait(HttpRequestInfo info)
        {
            if (info.Redirect == null)
            {
                if (info.SocketDuration > 0)
                {
                    info.HandshakeWait = (info.HandshakeStartTime - info.SocketConnectionStartTime).TotalMilliseconds - info.SocketDuration;
                }
                else
                {
                    if (info.DnsDuration > 0)
                    {
                        info.HandshakeWait = (info.HandshakeStartTime - info.DnsStartTime).TotalMilliseconds - info.DnsDuration;
                    }
                    else
                    {
                        info.HandshakeWait = (info.HandshakeStartTime - info.StartTime).TotalMilliseconds;
                    }
                }
            }
            else
            {
                if (info.Redirect.SocketDuration > 0)
                {
                    info.Redirect.HandshakeWait = (info.Redirect.HandshakeStartTime - info.Redirect.SocketConnectionStartTime).TotalMilliseconds - info.Redirect.SocketDuration;
                }
                else
                {
                    if (info.Redirect.DnsDuration > 0)
                    {
                        info.Redirect.HandshakeWait = (info.Redirect.HandshakeStartTime - info.Redirect.DnsStartTime).TotalMilliseconds - info.Redirect.DnsDuration;
                    }
                    else
                    {
                        info.Redirect.HandshakeWait = (info.Redirect.HandshakeStartTime - info.Redirect.StartTime).TotalMilliseconds;
                    }
                }
            }
        }

        private void OnHttpRequestHeaderStart(object sender, EventPipeBaseArgs e)
        {
            var root = GetRoot(e.ActivityId);
            if (!_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                return;
            }

            if (info.Redirect == null)
            {
                info.ReqRespStartTime = e.Timestamp;
            }
            else
            {
                info.Redirect.ReqRespStartTime = e.Timestamp;
            }
        }

        private void OnHttpResponseHeaderStop(object sender, HttpRequestStatusEventArgs e)
        {
            // used to detect redirection in .NET 8+
            if ((e.StatusCode < 300) || (e.StatusCode > 308))
            {
                return;
            }

            // create a new request info for the redirected request
            // because .NET 7 does not emit a Redirect event, we need to create a new request info here
            // --> it means that the redirect url will be empty in .NET 7
            var root = GetRoot(e.ActivityId);
            if (_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                info.Redirect = new HttpRequestInfoBase(e.Timestamp, "", "", 0, "");

                // if you really want to have the duration of both original request + redirected request,
                // then do the following:
                //    info.ReqRespDuration = (e.Timestamp - info.ReqRespStartTime).TotalMilliseconds;
                // However, I prefer to show the duration of the redirected request only to more easily
                // compute the cost of the initial redirected request = total duration - other durations
            }
        }

        private void OnHttpResponseContentStop(object sender, EventPipeBaseArgs e)
        {
            var root = GetRoot(e.ActivityId);
            if (!_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                return;
            }

            if (info.Redirect == null)
            {
                info.ReqRespDuration = (e.Timestamp - info.ReqRespStartTime).TotalMilliseconds;
            }
            else
            {
                info.Redirect.ReqRespDuration = (e.Timestamp - info.Redirect.ReqRespStartTime).TotalMilliseconds;
            }
        }


        private string GetRoot(Guid activityId)
        {
            var key = ActivityHelpers.ActivityPathString(activityId);
            var root = key.Substring(0, key.LastIndexOf('/'));

            return root;
        }

        // each HTTP request has a path (extracted from ActivityID GUID) that is reused as root in other events
        private Dictionary<string, HttpRequestInfo> _requests = new Dictionary<string, HttpRequestInfo>();
        private bool _isLogging;

        private class HttpRequestInfo : HttpRequestInfoBase
        {
            public HttpRequestInfo(DateTime timestamp, string scheme, string host, uint port, string path)
                :
                base(timestamp, scheme, host, port, path)
            {
            }

            public HttpRequestInfoBase Redirect { get; set; }

            public UInt32 StatusCode { get; set; }

            // HTTPS
            public string HandshakeErrorMessage { get; set; }

            public string Error { get; set; }
        }

        private class HttpRequestInfoBase
        {
            public HttpRequestInfoBase(DateTime timestamp, string scheme, string host, uint port, string path)
            {
                StartTime = timestamp;
                if (scheme == string.Empty)
                {
                    Url = string.Empty;
                }
                else
                {
                    if (port != 0)
                    {
                        Url = $"{scheme}://{host}:{port}{path}";
                    }
                    else
                    {
                        Url = $"{scheme}://{host}:{path}";
                    }
                }
            }

            public string Url { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime ReqRespStartTime { get; set; }
            public double ReqRespDuration { get; set; }

            // DNS
            public double DnsWait { get; set; }
            public DateTime DnsStartTime { get; set; }
            public double DnsDuration { get; set; }

            // HTTPS
            public double HandshakeWait { get; set; }
            public DateTime HandshakeStartTime { get; set; }
            public double HandshakeDuration { get; set; }

            // socket connection
            public DateTime SocketConnectionStartTime { get; set; }
            public double SocketWait { get; set; }
            public double SocketDuration { get; set; }


            public DateTime QueueuingEndTime { get; set; }
            public double QueueingDuration { get; set; }
        }
    }
}
