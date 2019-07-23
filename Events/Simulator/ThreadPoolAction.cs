using System.Threading;
using System.Threading.Tasks;

namespace Simulator
{
    public static class ThreadPoolAction
    {
        public static void Run()
        {
            //ThreadPool.QueueUserWorkItem((parameter) =>
            //{
            //    Thread.Sleep(10000);
            //});


            // Task equivalent
            Task.Run(() =>
            {
                Thread.Sleep(1000000);
            });
        }
    }
}
