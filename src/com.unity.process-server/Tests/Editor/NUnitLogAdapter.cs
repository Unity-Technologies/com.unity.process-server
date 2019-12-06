using System;
using System.Threading;

namespace BaseTests
{
    public class NUnitLogger : ILogging
    {
        private readonly string context;

        public bool TracingEnabled { get; set; }

        public NUnitLogger(string context)
        {
            this.context = context;
        }

        public void Info(string message, params object[] args)
        {
            WriteLine(context, message, args);
        }

        public void Warn(string message, params object[] args)
        {
            WriteLine(context, message, args);
        }

        public void Error(string message, params object[] args)
        {
            WriteLine(context, message, args);
        }

        public void Trace(string message, params object[] args)
        {
            if (!TracingEnabled) return;
            WriteLine(context, message, args);
        }

        private void WriteLine(string context, string message, params object[] args)
        {
            NUnit.Framework.TestContext.Progress.WriteLine(GetMessage(context, message, args));
        }

        private string GetMessage(string context, string message, params object[] args)
        {
            if (args != null) message = string.Format(message, args);
            var time = DateTime.Now.ToString("HH:mm:ss.fff tt");
            var threadId = Thread.CurrentThread.ManagedThreadId;
            return string.Format("{0} [{1,2}] {2} {3}", time, threadId, context, message);
        }
    }
}
