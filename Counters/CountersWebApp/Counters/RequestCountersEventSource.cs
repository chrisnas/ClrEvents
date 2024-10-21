using System;
using System.Diagnostics.Tracing;
using System.Threading;

namespace Counters.Request
{

    [EventSource(Name = RequestCountersEventSource.SourceName)]
    public class RequestCountersEventSource : EventSource
    {
        // this name will be used as "provider" name with dotnet-counters
        // ex: dotnet-counters monitor -p <pid> Sample.RequestCounters
        //
        const string SourceName = "Sample.RequestCounters";

        private int _requestCountValue;
        private int _noGcRequestCountValue;
        private int _withGcRequestsCountValue;


        // return ever increasing count
        private PollingCounter _requestCount;

        // compute the delta between current value and last value
        private IncrementingPollingCounter _requestCountDelta;

        // return ever incrementing count of requests
        // for which no garbage collection occurred during
        // their processing
        private PollingCounter _noGcRequestCount;

        // return ever incrementing count of requests
        // for which a garbage collection occurred during
        // their processing
        private PollingCounter _withGcRequestCount;

        // since min/max/mean are not computed by xxxCounter classes
        // we need to have one counter for each value
        private EventCounter _noGcRequestDuration;
        private EventCounter _withGcRequestDuration;


        private static RequestCountersEventSource s_Instance;

        public static void Initialize()
        {
            if (s_Instance != null)
                throw new InvalidOperationException($"{nameof(RequestCountersEventSource)} can't be initialized more than once");

            s_Instance = new RequestCountersEventSource();
        }

        public static RequestCountersEventSource Instance
        {
            get
            {
                return s_Instance;
            }
        }

        public RequestCountersEventSource()
            : base(RequestCountersEventSource.SourceName, EventSourceSettings.EtwSelfDescribingEventFormat)
        {
            // create the counters: they'll be bound to this event source + CounterGroup
            CreateCounters();
        }

        private void CreateCounters()
        {
            // the same request count can be used for two counters:
            // - raw request counter that will always increase
            // - increment counter that will automatically compute the delta
            //   between the current value and the value when the counter
            //   was previously sent
            _requestCount ??= new PollingCounter("request-count", this,
                () => _requestCountValue)
            { DisplayName = "Requests count" };
            _requestCountDelta ??= new IncrementingPollingCounter("request-count-delta", this,
                () => _requestCountValue)
            { DisplayName = "New requests", DisplayRateTimeScale = new TimeSpan(0, 0, 1) };

            // split the request counts between those for which a GC occurred or not
            // during their processing
            _noGcRequestCount ??= new PollingCounter("no-gc-request-count", this,
                () => _noGcRequestCountValue)
            { DisplayName = "Requests (processed without GC) count" };
            _withGcRequestCount ??= new PollingCounter("with-gc-request-count", this,
                () => _withGcRequestsCountValue)
            { DisplayName = "Requests (processed during a GC) count" };

            // request duration counters (with or without GC happening during the processing)
            _noGcRequestDuration ??= new EventCounter("no-gc-request-duration", this)
            { DisplayName = "Requests (processed without GC) duration in milli-seconds" };

            _withGcRequestDuration ??= new EventCounter("with-gc-request-duration", this)
            { DisplayName = "Requests (processed during a GC) duration in milli-seconds" };
        }


        internal void AddRequestWithoutGcDuration(long elapsedMilliseconds)
        {
            IncRequestCount();
            Interlocked.Increment(ref _noGcRequestCountValue);

            // compute min/max/mean
            _noGcRequestDuration?.WriteMetric(elapsedMilliseconds);
        }

        internal void AddRequestWithGcDuration(long elapsedMilliseconds)
        {
            IncRequestCount();
            Interlocked.Increment(ref _withGcRequestsCountValue);

            // compute min/max/mean
            _withGcRequestDuration?.WriteMetric(elapsedMilliseconds);
        }

        private void IncRequestCount()
        {
            Interlocked.Increment(ref _requestCountValue);
        }
    }
}
