using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace EventTracing.Simulator
{
    static class ExceptionAction
    {
        public static void ThrowFirstChanceExceptions()
        {
            ThreadPool.QueueUserWorkItem((s) => ThrowException());
        }

        private static readonly Exception[] Exceptions = new Exception[]
        {
            new ArgumentException(),
            new NullReferenceException(),
            new Exception(),
            new FormatException(),
            new InvalidOperationException(),
            new ArgumentNullException()
        };

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowException()
        {
            FirstToCall();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void FirstToCall()
        {
            DispatchCall();
        }

        static volatile int _current;
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DispatchCall()
        {
            Interlocked.Increment(ref _current);
            if ((_current % 2) == 0)
                EventCase();
            else
                OddCase();
        }

        static Random _random = new Random(Environment.TickCount);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void OddCase()
        {
            var currentException = _random.Next(0, Exceptions.Length);
            try
            {
                throw Exceptions[currentException];
            }
            catch (Exception)
            {
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void EventCase()
        {
            var currentException = _random.Next(0, Exceptions.Length);
            try
            {
                throw Exceptions[currentException];
            }
            catch (Exception)
            {
            }
        }

        // does not seem enough to enforce inlining... so duplicate the code in previous methods
        // --> only the last method call seems to be kept by the first chance exception notification mechanism  :^(
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ThrowExceptionAtTheEnd()
        {
            var currentException = _random.Next(0, Exceptions.Length);
            try
            {
                throw Exceptions[currentException];
            }
            catch (Exception)
            {
            }
        }
    }
}
