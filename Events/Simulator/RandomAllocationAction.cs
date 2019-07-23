using System;

namespace EventTracing.Simulator
{
    public static class RandomAllocationAction
    {
        private static Random _r = new Random(Environment.TickCount);

        public static void Run()
        {
            var size = _r.Next(256, 86000);
            var buffer = new byte[size];
            buffer[size / 2] = 42;
        }
    }
}
