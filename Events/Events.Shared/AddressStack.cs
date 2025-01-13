using System.Collections.Generic;

namespace Shared
{
#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
    public class AddressStack
    {
        // the first frame is the address of the last called method
        private readonly List<ulong> _stack;

        public AddressStack(int capacity)
        {
            _stack = new List<ulong>(capacity);
        }

        // No need to override GetHashCode because we don't want to use it as a key in a dictionary
        public override bool Equals(object obj)
        {
            if (obj == null) return false;

            var stack = obj as AddressStack;
            if (stack == null) return false;

            var frameCount = _stack.Count;
            if (frameCount != stack._stack.Count) return false;

            for (int i = 0; i < frameCount; i++)
            {
                if (_stack[i] != stack._stack[i]) return false;
            }

            return true;
        }

        public IReadOnlyList<ulong> Stack => _stack;

        public void AddFrame(ulong address)
        {
            _stack.Add(address);
        }
    }
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
}
