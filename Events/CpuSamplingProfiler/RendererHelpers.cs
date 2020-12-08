using System;
using System.Linq;

namespace CpuSamplingProfiler
{
    public static class RendererHelpers
    {
        public static void Render(this MergedSymbolicStacks stacks, IRenderer visitor)
        {
            RenderStack(stacks, visitor, true, 0);
        }

        private const int Padding = 5;
        private static void RenderStack(MergedSymbolicStacks stack, IRenderer visitor, bool isRoot, int increment)
        {
            var alignment = new string(' ', Padding * increment);
            var padding = new string(' ', Padding);
            var currentFrame = stack.Frame;

            // special root case
            if (isRoot)
                visitor.WriteCount($"{Environment.NewLine}{alignment}{stack.CountAsNode, Padding} ");
            else
                visitor.WriteCount($"{Environment.NewLine}{alignment}{stack.CountAsLeaf + stack.CountAsNode, Padding} ");

            visitor.WriteMethod(stack.Symbol);

            var childrenCount = stack.Stacks.Count;
            if (childrenCount == 0)
            {
                visitor.WriteFrameSeparator("");
                return;
            }
            foreach (var nextStackFrame in stack.Stacks.OrderByDescending(s => s.CountAsNode + s.CountAsLeaf))
            {
                // increment when more than 1 children
                var childIncrement = (childrenCount == 1) ? increment : increment + 1;
                RenderStack(nextStackFrame, visitor, false, childIncrement);
                if (increment != childIncrement)
                {
                    visitor.WriteFrameSeparator($"{Environment.NewLine}{alignment}{padding}{nextStackFrame.CountAsNode + nextStackFrame.CountAsLeaf, Padding} ");
                    visitor.WriteFrameSeparator($"~~~~ ");
                }
            }
        }
    }
}
