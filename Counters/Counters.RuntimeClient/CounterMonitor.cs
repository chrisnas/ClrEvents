using Microsoft.Diagnostics.Tools.RuntimeClient;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;

namespace Counters.Runtime
{
    public class CounterMonitor
    {
        private const ulong EmptySession = 0xffffffff;
        private readonly int _pid;
        private readonly IReadOnlyCollection<Provider> _providers;

        private ulong _sessionId = EmptySession;

        public event Action<CounterEventArgs> CounterUpdate;

        public CounterMonitor(int pid, IReadOnlyCollection<Provider> providers)
        {
            _pid = pid;
            _providers = providers;
        }

        public void Start()
        {
            var configuration = new SessionConfiguration(
                circularBufferSizeMB: 1000,
                outputPath: "",
                providers: _providers
                );

            var binaryReader = EventPipeClient.CollectTracing(_pid, configuration, out _sessionId);
            EventPipeEventSource source = new EventPipeEventSource(binaryReader);
            source.Dynamic.All += ProcessEvents;

            // this is a blocking call
            source.Process();
        }

        public void Stop()
        {
            if (_sessionId == EmptySession)
                throw new InvalidOperationException("Start() must be called to start the session");

            EventPipeClient.StopTracing(_pid, _sessionId);
        }

        private void ProcessEvents(TraceEvent data)
        {
            if (data.EventName.Equals("EventCounters"))
            {
                IDictionary<string, object> countersPayload = (IDictionary<string, object>)(data.PayloadValue(0));
                IDictionary<string, object> kvPairs = (IDictionary<string, object>)(countersPayload["Payload"]);
                // the TraceEvent implementation throws not implemented exception if you try
                // to get the list of the dictionary keys: it is needed to iterate on the dictionary
                // and get each key/value pair.

                var name = string.Intern(kvPairs["Name"].ToString());
                var displayName = string.Intern(kvPairs["DisplayName"].ToString());

                var counterType = kvPairs["CounterType"];
                if (counterType.Equals("Sum"))
                {
                    OnSumCounter(name, displayName, kvPairs);
                }
                else
                if (counterType.Equals("Mean"))
                {
                    OnMeanCounter(name, displayName, kvPairs);
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported counter type '{counterType}'");
                }
            }
        }

        private void OnSumCounter(string name, string displayName, IDictionary<string, object> kvPairs)
        {
            double value = double.Parse(kvPairs["Increment"].ToString());

            // send the information to your metrics pipeline
            CounterUpdate(new CounterEventArgs(name, displayName, CounterType.Sum, value));
        }

        private void OnMeanCounter(string name, string displayName, IDictionary<string, object> kvPairs)
        {
            double value = double.Parse(kvPairs["Mean"].ToString());

            // send the information to your metrics pipeline
            CounterUpdate(new CounterEventArgs(name, displayName, CounterType.Mean, value));
        }


        //Name = cpu-usage
        //DisplayName = CPU Usage
        //Mean = 0
        //StandardDeviation = 0
        //Count = 1
        //Min = 0
        //Max = 0
        //IntervalSec = 3.24E-05
        //Series = Interval=1000
        //CounterType = Mean
        //Metadata =

        //Name = gen-0-gc-count
        //DisplayName = Gen 0 GC Count
        //DisplayRateTimeScale = 00:01:00
        //Increment = 0
        //IntervalSec = 1.88E-05
        //Metadata =
        //Series = Interval=1000
        //CounterType = Sum

    }

    public class CounterEventArgs : EventArgs
    {
        internal CounterEventArgs(string name, string displayName, CounterType type, double value)
        {
            Counter = name;
            DisplayName = displayName;
            Type = type;
            Value = value;
        }

        public string Counter { get; set; }
        public string DisplayName { get; set; }
        public CounterType Type { get; set; }
        public double Value { get; set; }
    }

    public enum CounterType
    {
        Sum = 0,
        Mean = 1,
    }
}
