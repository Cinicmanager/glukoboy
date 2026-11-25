using System;
using System.Threading;

namespace Bubliklyuk
{
    // Opened before taking the instance mutex, so a second launch during startup
    // cannot lose its request. The UI thread consumes it without blocking.
    internal sealed class LaunchRequest : IDisposable
    {
        internal const string EventName = "Local\\Glukoboy.CheckSchedule.1";
        private readonly EventWaitHandle pending;

        internal LaunchRequest(string name)
        {
            pending = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        }
        internal void Request() { pending.Set(); }
        internal bool TakePending() { return pending.WaitOne(0); }
        public void Dispose() { pending.Dispose(); }
    }
}
