using System;

namespace EventTracing.Simulator
{
    public class Generic<T>
    {
        public T _field;

        public Generic(T instance)
        {
            _field = instance;
        }
    }

    public static class RandomAllocationAction
    {
        private static Random _r = new Random(Environment.TickCount);

        public static void Run()
        {
            var size = _r.Next(256, 86000);
            var buffer = new Generic<int>[size];
            buffer[size / 2] = new Generic<int>(42);
        }
    }
}
