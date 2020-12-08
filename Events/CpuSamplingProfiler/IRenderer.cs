
namespace CpuSamplingProfiler
{
    /// <summary>
    /// The method of this interface are called to render each part of the merged call stacks
    /// </summary>
    /// <remarks>
    /// Each method is responsible for adding color, tags or decoration on each element of the merged stacks
    /// </remarks>
    public interface IRenderer
    {
        /// <summary>
        /// Render empty line
        /// </summary>
        /// <param name="text"></param>
        void Write(string text);

        /// <summary>
        /// Render count at the beginning of each line
        /// </summary>
        /// <param name="count"></param>
        void WriteCount(string count);

        /// <summary>
        /// Render separators such as ( and .
        /// </summary>
        /// <param name="separator"></param>
        void WriteSeparator(string separator);

        /// <summary>
        /// Render method name
        /// </summary>
        /// <param name="method"></param>
        void WriteMethod(string method);

        /// <summary>
        /// Render separator between different stack frame blocks
        /// </summary>
        /// <param name="text"></param>
        void WriteFrameSeparator(string text);
    }

}
