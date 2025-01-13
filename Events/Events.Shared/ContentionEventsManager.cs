
using System;

namespace Shared
{
    public class ContentionEventsManager
    {
        private int _waitThreshold;

        public ContentionEventsManager(ClrEventsManager source, int waitThreshold)
        {
            _waitThreshold = waitThreshold;

            SetupListeners(source);
        }

        private void SetupListeners(ClrEventsManager source)
        {
            source.Contention += OnContention;
        }

        private void OnContention(object sender, ContentionArgs e)
        {
            if (e.IsManaged)
            {
                if (e.Duration.TotalMilliseconds > _waitThreshold)
                {
                    Console.WriteLine($"{e.ThreadId,7} | {e.Duration.TotalMilliseconds} ms");
                    if (e.Callstack != null)
                    {
                        // show the last frame at the top
                        for (int i = 0; i < e.Callstack.Count; i++)
                        //for (int i = e.Callstack.Count - 1; i > 0; i--)
                        {
                            Console.WriteLine($"    {e.Callstack[i]}");
                        }
                    }
                    Console.WriteLine();
                }
            }
        }
    }
}
