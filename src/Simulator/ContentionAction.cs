using System.Threading;
using System.Threading.Tasks;

namespace EventTracing.Simulator
{
    public static class ContentionAction
    {
        private static readonly object _lock = new object();
        private static int _count;

        public static void Run()
        {
            lock (_lock)
            {
                Interlocked.Increment(ref _count);
                Thread.Sleep(20);
            }
        }
    }
}
