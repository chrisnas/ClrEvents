using System;
using System.Collections.Generic;
using System.IO;

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
            source.HttpResponseContentStop += OnHttpResponseContentStop;
        }

        private void OnHttpRequestStart(object sender, HttpRequestStartEventArgs e)
        {
            var requestInfo = new HttpRequestInfo(e.Timestamp, e.ActivityId, e.Scheme, e.Host, e.Port, e.Path);
            if (_requests.TryGetValue(requestInfo.Root, out HttpRequestInfo existingRequest))
            {
                Console.WriteLine($"      ? {existingRequest.Url}");
                return;
            }

            _requests.Add(requestInfo.Root, requestInfo);
        }


        private void DumpDurations(HttpRequestInfo info)
        {
            if (info.DnsDuration > 0)
            {
                Console.Write($"{(info.DnsStartTime-info.StartTime).TotalMilliseconds,9:F3} | {info.DnsDuration,9:F3} | ");
            }
            else
            {
                Console.Write($"          |           | ");
            }

            if (info.SocketDuration > 0)
            {
                double wait = 0;
                if (info.DnsDuration > 0)
                {
                    wait = (info.SocketConnectionStartTime - info.DnsStartTime).TotalMilliseconds - info.DnsDuration;
                }
                else
                {
                    wait = (info.SocketConnectionStartTime - info.StartTime).TotalMilliseconds;
                }
                Console.Write($"{wait,9:F3} | {info.SocketDuration,9:F3} | ");
            }
            else
            {
                Console.Write($"          |           | ");
            }

            if (info.HandshakeDuration > 0)
            {
                double wait = 0;
                if (info.SocketDuration > 0)
                {
                    wait = (info.HandshakeStartTime - info.SocketConnectionStartTime).TotalMilliseconds - info.SocketDuration;
                }
                if (info.DnsDuration > 0)
                {
                    wait = (info.HandshakeStartTime - info.DnsStartTime).TotalMilliseconds - info.DnsDuration;
                }
                else
                {
                    wait = (info.HandshakeStartTime - info.StartTime).TotalMilliseconds;
                }
                Console.Write($"{wait,9:F3} | {info.HandshakeDuration,9:F3} | ");
            }
            else
            {
                Console.Write($"          |           | ");
            }

            // TODO: try to understand what this duration means
            if (info.QueueingDuration > 0)
            {
                Console.Write($"{info.QueueingDuration,9:F3} | ");
            }
            else
            {
                Console.Write($"          | ");
            }

            if (info.ReqRespDuration > 0)
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

                Console.Write($"{info.ReqRespDuration,9:F3}");
            }
            else
            {
                //Console.Write($"          |           ");
                Console.Write($"           ");
            }
        }

        private void OnHttpRequestStop(object sender, HttpRequestStopEventArgs e)
        {
            var root = ActivityHelpers.ActivityPathString(e.ActivityId);
            if (_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                Console.Write($"   {e.StatusCode,3} | {(e.Timestamp - info.StartTime).TotalMilliseconds,9:F3} | ");
                DumpDurations(info);
                Console.WriteLine($" - {info.Url}");
            }
            else
            {
                // ???
            }

            _requests.Remove(root);
        }

        private void OnHttpRequestFailed(object sender, HttpRequestFailedEventArgs e)
        {
            var root = ActivityHelpers.ActivityPathString(e.ActivityId);
            if (_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                Console.Write($"     x | {(e.Timestamp - info.StartTime).TotalMilliseconds,9:F3} | ");
                DumpDurations(info);
                Console.WriteLine($" - {info.Url} ~ {e.Text}");
            }
            else
            {
                // ???
            }

            _requests.Remove(root);
        }

        private void OnDnsResolutionStart(object sender, ResolutionStartEventArgs e)
        {
            var root = GetRoot(e.ActivityId);
            if (_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                info.DnsStartTime = e.Timestamp;
            }
            else
            {
                // ???
            }
        }

        private void OnDnsResolutionStop(object sender, DnsEventArgs e)
        {
            var root = GetRoot(e.ActivityId);
            if (_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                info.DnsDuration = (e.Timestamp - info.DnsStartTime).TotalMilliseconds;
            }
            else
            {
                // ???
            }
        }

        private void OnSocketConnectStart(object sender, SocketEventArgs e)
        {
            var root = GetRoot(e.ActivityId);
            if (_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                info.SocketConnectionStartTime = e.Timestamp;
            }
            else
            {
                // ???
            }
        }

        private void OnSocketConnectStop(object sender, SocketEventArgs e)
        {
            var root = GetRoot(e.ActivityId);
            if (_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                info.SocketDuration = (e.Timestamp - info.SocketConnectionStartTime).TotalMilliseconds;
            }
            else
            {
                // ???
            }
        }

        private void OnHttpRequestLeftQueue(object sender, HttpRequestLeftQueueEventArgs e)
        {
            // since this is an Info event, the activityID is the root
            var root = ActivityHelpers.ActivityPathString(e.ActivityId);
            if (_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                info.QueueuingEndTime = e.Timestamp;
                info.QueueingDuration = e.TimeOnQueue;
            }
        }

        private void OnHandshakeStart(object sender, HandshakeStartEventArgs e)
        {
            var root = GetRoot(e.ActivityId);
            if (_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                info.HandshakeStartTime = e.Timestamp;
            }
            else
            {
                // ???
            }
        }

        private void OnHandshakeStop(object sender, HandshakeStopEventArgs e)
        {
            var root = GetRoot(e.ActivityId);
            if (_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                info.HandshakeDuration = (e.Timestamp - info.HandshakeStartTime).TotalMilliseconds;
            }
            else
            {
                // ???
            }
        }

        private void OnHandshakeFailed(object sender, HandshakeFailedEventArgs e)
        {
            var root = GetRoot(e.ActivityId);
            if (_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                info.HandshakeDuration = (e.Timestamp - info.HandshakeStartTime).TotalMilliseconds;
                info.HandshakeErrorMessage = e.Message;
            }
            else
            {
                // ???
            }
        }

        private void OnHttpRequestHeaderStart(object sender, EventPipeBaseArgs e)
        {
            var root = GetRoot(e.ActivityId);
            if (_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                info.ReqRespStartTime = e.Timestamp;
            }
            else
            {
                // ???
            }
        }

        private void OnHttpResponseContentStop(object sender, EventPipeBaseArgs e)
        {
            var root = GetRoot(e.ActivityId);
            if (_requests.TryGetValue(root, out HttpRequestInfo info))
            {
                info.ReqRespDuration = (e.Timestamp - info.ReqRespStartTime).TotalMilliseconds;
            }
            else
            {
                // ???
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

        private class HttpRequestInfo
        {
            public HttpRequestInfo(DateTime timestamp, Guid activityId, string scheme, string host, uint port, string path)
            {
                Root = ActivityHelpers.ActivityPathString(activityId);
                if (port != 0)
                {
                    Url = $"{scheme}://{host}:{port}{path}";
                }
                else
                {
                    Url = $"{scheme}://{host}:{path}";
                }
                StartTime = timestamp;
            }

            public string Root { get; set; }
            public string Url { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime ReqRespStartTime { get; set; }
            public double ReqRespDuration { get; set; }

            public UInt32 StatusCode { get; set; }

            // DNS
            public DateTime DnsStartTime { get; set; }
            public double DnsDuration { get; set; }

            // HTTPS
            public DateTime HandshakeStartTime { get; set; }
            public double HandshakeDuration { get; set; }
            public string HandshakeErrorMessage { get; set; }

            // socket connection
            public DateTime SocketConnectionStartTime { get; set; }
            public double SocketDuration { get; set; }


            public DateTime QueueuingEndTime { get; set; }
            public double QueueingDuration { get; set; }
        }
    }
}
