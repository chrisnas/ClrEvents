using Counters.Runtime;
using Microsoft.Diagnostics.Tools.RuntimeClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Counters.RuntimeClient
{
    public class CsvCounterListener
    {
        private readonly string _filename;
        private readonly int _pid;
        private CounterMonitor _counterMonitor;
        private List<(string name, double value)> _countersValue;

        public CsvCounterListener(string filename, int pid)
        {
            _filename = filename;
            _pid = pid;
            _countersValue = new List<(string name, double value)>();
        }

        public void Start()
        {
            if (_counterMonitor != null)
                throw new InvalidOperationException($"Start can't be called multiple times");

            _counterMonitor = new CounterMonitor(_pid, GetProviders());
            _counterMonitor.CounterUpdate += OnCounterUpdate;

            Task monitorTask = new Task(() => {
                try
                {
                    _counterMonitor.Start();
                }
                catch (Exception x)
                {
                    Environment.FailFast("Error while listening to counters", x);
                }
            });
            monitorTask.Start();
        }

        private void OnCounterUpdate(CounterEventArgs args)
        {
            _countersValue.Add((args.DisplayName, args.Value));

            // we "know" that the last CLR counter is "assembly-count"
            // NOTE: this is a flaky way to detect the last counter event:
            //       -> could get the list of counters the first time they are received
            if (args.Counter == "assembly-count")
            {
                SaveLine();
                _countersValue.Clear();
            }
        }

        bool isHeaderSaved = false;
        private void SaveLine()
        {
            if (!isHeaderSaved)
            {
                File.AppendAllText(_filename, GetHeaderLine());
                isHeaderSaved = true;
            }

            File.AppendAllText(_filename, GetCurrentLine());
        }

        private string GetHeaderLine()
        {
            StringBuilder buffer = new StringBuilder();
            foreach (var counter in _countersValue)
            {
                buffer.AppendFormat("{0}\t", counter.name);
            }

            // remove last tab
            buffer.Remove(buffer.Length - 1, 1);

            // add Windows-like new line because will be used in Excel
            buffer.Append("\r\n");

            return buffer.ToString();
        }

        private string GetCurrentLine()
        {
            StringBuilder buffer = new StringBuilder();
            foreach (var counter in _countersValue)
            {
                buffer.AppendFormat("{0}\t", counter.value.ToString());
            }

            // remove last tab
            buffer.Remove(buffer.Length - 1, 1);

            // add Windows-like new line because will be used in Excel
            buffer.Append("\r\n");

            return buffer.ToString();
        }

        public void Stop()
        {
            if (_counterMonitor == null)
                throw new InvalidOperationException($"Stop can't be called before Start");

            _counterMonitor.Stop();
            _counterMonitor = null;

            _countersValue.Clear();
        }

        private IReadOnlyCollection<Provider> GetProviders()
        {
            var providers = new List<Provider>();

            // create default "System.Runtime" provider
            var provider = CounterHelpers.MakeProvider("System.Runtime", 1);
            providers.Add(provider);

            return providers;
        }
    }
}
