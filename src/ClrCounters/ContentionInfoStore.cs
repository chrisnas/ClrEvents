using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ClrCounters
{
    internal class ContentionInfoStore
    {
        private readonly ConcurrentDictionary<int, ProcessContentionInfo> _perProcessContentionInfo = new ConcurrentDictionary<int, ProcessContentionInfo>();

        public void AddProcess(int processId)
        {
            ProcessContentionInfo info = new ProcessContentionInfo(processId);
            _perProcessContentionInfo.TryAdd(processId, info);
        }

        public void RemoveProcess(int processId)
        {
            ProcessContentionInfo info;
            _perProcessContentionInfo.TryRemove(processId, out info);
        }

        public ContentionInfo GetContentionInfo(int processId, int threadId)
        {
            ProcessContentionInfo processInfo;
            if (_perProcessContentionInfo.TryGetValue(processId, out processInfo))
            {
                return processInfo.GetContentionInfo(threadId);
            }

            return null;
        }
    }

    internal struct ProcessContentionInfo
    {
        private readonly int _processId;
        private readonly Dictionary<int, ContentionInfo> _perThreadContentionInfo;

        public ProcessContentionInfo(int processId)
        {
            _processId = processId;
            _perThreadContentionInfo = new Dictionary<int, ContentionInfo>();
        }

        public ContentionInfo GetContentionInfo(int threadId)
        {
            ContentionInfo contentionInfo;
            if (!_perThreadContentionInfo.TryGetValue(threadId, out contentionInfo))
            {
                contentionInfo = new ContentionInfo(_processId, threadId);
                _perThreadContentionInfo.Add(threadId, contentionInfo);
            }
            return contentionInfo;
        }
    }

}
