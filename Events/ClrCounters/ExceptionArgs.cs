using System;

namespace ClrCounters
{
    public class ExceptionArgs : ClrEventArgs
    {
        public ExceptionArgs(DateTime timeStamp, int processId, string typeName, string message)
        : base(timeStamp, processId)
        {
            TypeName = typeName;
            Message = message;
        }

        public string TypeName { get; }

        public string Message { get; }
    }
}
