using System;

namespace CpuSamplingProfiler
{
    public class ConsoleRenderer : IRenderer
    {
        public void Write(string text)
        {
            Console.Write(text);
        }

        public void WriteCount(string count)
        {
            WriteWithColor(count, ConsoleColor.Gray);
        }

        public void WriteSeparator(string separator)
        {
            WriteWithColor(separator, ConsoleColor.White);
        }

        public void WriteMethod(string method)
        {
            WriteWithColor(method, ConsoleColor.Cyan);
        }

        public void WriteFrameSeparator(string text)
        {
            WriteWithColor(text, ConsoleColor.Yellow);
        }

        private void WriteWithColor(string text, ConsoleColor color)
        {
            var current = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ForegroundColor = current;
        }
    }

}
