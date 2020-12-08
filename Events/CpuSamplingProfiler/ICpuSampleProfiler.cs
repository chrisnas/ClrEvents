namespace CpuSamplingProfiler
{
    public interface ICpuSampleProfiler
    {
        long TotalStackCount { get; }
        MergedSymbolicStacks Stacks { get; }

        // Don't forget to call Stop or the ETW session will be leaked
        bool Start(int pid);
        
        // TODO: should Stop return a string corresponding to the symbols log?
        void Stop();
    }
}
