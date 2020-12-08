using System.Collections.Generic;
using System.Linq;

namespace CpuSamplingProfiler
{
    public struct SymbolicFrame
    {
        public readonly ulong Address;
        public readonly string Symbol;

        public SymbolicFrame(ulong address, string symbol)
        {
            Address = address;

            // TODO: could be interesting to intern the string
            Symbol = string.IsNullOrEmpty(symbol) ? $"0x{Address:x}" : symbol;
        }

        public override string ToString()
        {
            return Symbol;
        }
    }

    public class MergedSymbolicStacks
    {
        private int _countAsNode;
        private int _countAsLeaf;

        public ulong Frame { get; private set; }
        public string Symbol { get; private set; }
        public int CountAsNode => _countAsNode;
        public int CountAsLeaf => _countAsLeaf;

        public List<MergedSymbolicStacks> Stacks { get; set; }

        public MergedSymbolicStacks() : this(0, string.Empty)
        {
            // this will be the root of all stacks
        }

        private MergedSymbolicStacks(ulong frame, string symbol)
        {
            Frame = frame;
            Symbol = symbol;
            _countAsNode = 0;
            _countAsLeaf = 0;
            Stacks = new List<MergedSymbolicStacks>();
        }

        public void AddStack(SymbolicFrame[] frames, int index = 0)
        {
            _countAsNode++;

            var firstFrame = frames[index];

            // search if the frame to add has already been seen
            var callstack = Stacks.FirstOrDefault(s => string.CompareOrdinal(s.Symbol, firstFrame.Symbol) == 0);

            // if not, we are starting a new branch
            if (callstack == null)
            {
                callstack = new MergedSymbolicStacks(frames[index].Address, frames[index].Symbol);
                Stacks.Add(callstack);
            }

            // it was the last frame of the stack
            if (index == frames.Length - 1)
            {
                callstack._countAsLeaf++;
                return;
            }

            callstack.AddStack(frames, index + 1);
        }

        public override string ToString()
        {
            return (_countAsLeaf > 0)
                ? $"{_countAsNode} x '{Symbol}' - {_countAsLeaf} ({Stacks.Count})"
                : $"{_countAsNode} x '{Symbol}' ({Stacks.Count})"
                ;
        }
    }

}
