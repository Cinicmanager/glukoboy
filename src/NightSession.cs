using System;

namespace Bubliklyuk
{
    // A parent's exception belongs only to this running session. A fresh launch
    // always enforces the current schedule, even with a legacy night-state.txt.
    internal sealed class NightSession
    {
        private DateTime skipUntil = DateTime.MinValue;

        internal void ResetOnLaunch() { skipUntil = DateTime.MinValue; }
        internal bool IsBlocked(Settings settings, DateTime now, out DateTime start, out DateTime end)
        {
            return NightSchedule.Current(settings, now, out start, out end) && skipUntil != end;
        }
        internal bool TrySkip(Settings settings, DateTime now, out DateTime end)
        {
            DateTime start;
            if (!IsBlocked(settings, now, out start, out end)) return false;
            skipUntil = end;
            return true;
        }
    }
}
