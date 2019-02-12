using System;
using System.IO;

namespace GcLog
{
    public abstract class GcLogBase : IGcLog
    {
        protected string Filename;
        private StreamWriter _fileWriter;

        public void Start(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                throw new ArgumentNullException(nameof(filename));

            if (_fileWriter != null)
                throw new InvalidOperationException("Start can't be called twice: Stop must be called first.");

            _fileWriter = new StreamWriter(filename);
            Filename = filename;

            OnStart();
        }

        public void Stop()
        {
            if (string.IsNullOrEmpty(Filename))
                return;

            OnStop();
            Filename = null;
            _fileWriter.Flush();
            _fileWriter.Dispose();
            _fileWriter = null;
        }

        protected bool WriteLine(string line)
        {
            if (_fileWriter == null)
                return false;   // just in case the method is called AFTER Stop

            _fileWriter.WriteLine(line);

            return true;
        }

        protected abstract void OnStart();

        protected abstract void OnStop();
    }
}
