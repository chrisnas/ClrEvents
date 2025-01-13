using System.Collections.Generic;

namespace Shared
{
    public class ProcessStackMapping
    {
        private readonly Dictionary<int, AddressStack> _perThreadStacks;

        public ProcessStackMapping(int processId)
        {
            ProcessId = processId;
            _perThreadStacks = new Dictionary<int, AddressStack>();
        }

        public int ProcessId { get; set; }

        public AddressStack this[int id]
        {
            get
            {
                if (!_perThreadStacks.ContainsKey(id))
                    return null;

                return _perThreadStacks[id];
            }
            set
            {
                _perThreadStacks[id] = value;
            }
        }

    }
}
