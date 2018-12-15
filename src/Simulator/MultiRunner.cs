using System;
using System.Threading;
using System.Threading.Tasks;

namespace EventTracing.Simulator
{
    public static class MultiRunner
    {
        private static readonly object _lock = new object();
        private static CancellationTokenSource _cancellationSource = new CancellationTokenSource();
        private static Task[] _workers;

        public static void Start(int taskCount, Action action, int repeatDelay)
        {
            if (_workers != null)
            {
                return;
            }

            _workers = new Task[taskCount];
            for (int i = 0; i < taskCount; i++)
            {
                _workers[i] = Task.Run(async () =>
                {
                    while (!_cancellationSource.IsCancellationRequested)
                    {
                        try
                        {
                            action();
                        }
                        catch (TaskCanceledException)
                        {
                        }

                        await Task.Delay(repeatDelay);
                    }
                });
            }
        }
        public static void Stop()
        {
            if (_workers == null)
            {
                return;
            }

            _cancellationSource.Cancel();
            Task.WhenAll(_workers).Wait();

            _cancellationSource = new CancellationTokenSource();
            _workers = null;
        }
    }
}
