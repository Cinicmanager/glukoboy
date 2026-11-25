using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

namespace Bubliklyuk
{
    internal static class NightStartupChecks
    {
        private static readonly DateTime Evening = new DateTime(2026, 11, 25, 21, 0, 0);
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static void Run(Action<bool, string> check)
        {
            foreach (bool testMode in new bool[] { false, true })
            {
                Boundaries(check, new Settings { TestMode = testMode });
                SessionLifetime(check, new Settings { TestMode = testMode });
                CustomSchedule(check, testMode);
            }
            LegacyState(check);
            LaunchSignals(check);
            HeartbeatDispatch(check);
            DisposedNightWindow(check);
        }

        private static bool Blocked(NightSession session, Settings settings, DateTime now)
        {
            DateTime start, end;
            return session.IsBlocked(settings, now, out start, out end);
        }

        private static void Boundaries(Action<bool, string> check, Settings settings)
        {
            NightSession session = new NightSession();
            DateTime morning = Evening.AddHours(10), start, end;
            check(!Blocked(session, settings, Evening.AddTicks(-1)), "Launch before 21:00 is daytime");
            foreach (DateTime now in new DateTime[] { Evening, Evening.AddHours(2), Evening.AddHours(3), morning.AddTicks(-1) })
            {
                check(session.IsBlocked(settings, now, out start, out end), "Launch enforces night at " + now.ToString("s"));
                check(start == Evening && end == morning, "Night boundaries survive midnight and TestMode");
            }
            check(!Blocked(session, settings, morning), "07:00 ends night exactly");
            check(!Blocked(session, settings, morning.AddTicks(1)), "Launch after 07:00 stays daytime");
        }

        private static void SessionLifetime(Action<bool, string> check, Settings settings)
        {
            DateTime end;
            NightSession session = new NightSession();
            check(!session.TrySkip(settings, Evening.AddHours(-1), out end), "Daytime skip cannot cancel the coming night");
            check(Blocked(session, settings, Evening), "Daytime skip leaves 21:00 blocked");
            check(session.TrySkip(settings, Evening.AddMinutes(10), out end) && end == Evening.AddHours(10),
                "Parent skip lasts until the current night's end");
            check(!Blocked(session, settings, Evening.AddHours(2)), "Parent skip applies within the same session");
            check(!Blocked(session, settings, Evening.AddHours(3)), "Parent skip survives midnight within the session");
            check(!session.TrySkip(settings, Evening.AddHours(4), out end), "Repeated skip does not create another exception");
            check(Blocked(new NightSession(), settings, Evening.AddHours(2)), "Fresh process does not inherit a parent's skip");
            check(Blocked(new NightSession(), settings, Evening.AddHours(4)), "Fresh process after midnight enforces the same night");
            session.ResetOnLaunch();
            check(Blocked(session, settings, Evening.AddHours(4)), "Repeated launch resets the running session's skip");
            check(session.TrySkip(settings, Evening.AddHours(4), out end), "Parent can grant a new skip after relaunch");
            check(!Blocked(session, settings, Evening.AddHours(10)), "A skipped night still ends at 07:00");
            check(Blocked(session, settings, Evening.AddDays(1)), "Previous skip cannot bypass the next evening");
            session.ResetOnLaunch();
            session.ResetOnLaunch();
            check(!Blocked(session, settings, Evening.AddHours(15)), "Repeated daytime launches do not invent a night");
            check(Blocked(session, settings, Evening.AddDays(1)), "Repeated launch reset is idempotent");
        }

        private static void CustomSchedule(Action<bool, string> check, bool testMode)
        {
            Settings settings = new Settings { TestMode = testMode, NightFrom = new TimeSpan(22, 30, 0), NightTo = new TimeSpan(6, 15, 0) };
            NightSession session = new NightSession();
            DateTime start = Evening.Date.Add(settings.NightFrom), end;
            check(!Blocked(session, settings, Evening), "Launch follows custom NightFrom instead of 21:00");
            check(Blocked(session, settings, start), "Custom 22:30 boundary is included");
            check(session.TrySkip(settings, start, out end) && end == Evening.Date.AddDays(1).Add(settings.NightTo),
                "Custom skip expires at configured 06:15");
            session.ResetOnLaunch();
            check(Blocked(session, settings, end.AddTicks(-1)), "Relaunch enforces custom night until its last instant");
            check(!Blocked(session, settings, end), "Custom 06:15 boundary is excluded");

            settings.NightFrom = new TimeSpan(0, 5, 0);
            settings.NightTo = new TimeSpan(0, 45, 0);
            start = Evening.Date.AddDays(1).Add(settings.NightFrom);
            check(!Blocked(session, settings, start.AddTicks(-1)), "Same-date custom window has a lower boundary");
            check(Blocked(session, settings, start), "00:05 custom night starts on the correct date");
            check(session.TrySkip(settings, start, out end) && end == start.Date.Add(settings.NightTo),
                "Same-date custom skip ends today");
            session.ResetOnLaunch();
            check(Blocked(session, settings, start.AddMinutes(1)), "Relaunch resets a same-date custom skip");
            check(!Blocked(session, settings, end), "Same-date custom window ends exactly");
        }

        private static void LegacyState(Action<bool, string> check)
        {
            // run.ps1 places Regression.exe in a private temporary directory.
            // Never overwrite a real installation's saved state if run manually.
            string directory = Path.GetDirectoryName(Settings.FilePath("night-state.txt"));
            if (!Path.GetFileName(directory).StartsWith("glukoboy-tests-", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Night state checks must run through tests/run.ps1 in its isolated directory.");
            string path = Settings.FilePath("night-state.txt");
            byte[] previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            try
            {
                string saved = Evening.AddHours(10).ToString("s", CultureInfo.InvariantCulture);
                File.WriteAllText(path, saved, new UTF8Encoding(true));
                Settings settings = new Settings();
                NightSession session = new NightSession();
                DateTime end;
                check(Blocked(session, settings, Evening.AddHours(2)), "Legacy night-state.txt cannot bypass a fresh launch");
                session.TrySkip(settings, Evening.AddHours(2), out end);
                session.ResetOnLaunch();
                check(Blocked(session, settings, Evening.AddHours(4)), "Legacy night-state.txt cannot restore skip after relaunch");
                check(File.ReadAllText(path, Encoding.UTF8) == saved, "Night session does not modify legacy persisted state");
                File.Delete(path);
                session.TrySkip(settings, Evening.AddHours(4), out end);
                check(!File.Exists(path), "Parent night skip is not persisted to disk");
            }
            finally
            {
                if (previous != null) File.WriteAllBytes(path, previous);
                else if (File.Exists(path)) File.Delete(path);
            }
        }

        private static string UniqueEventName()
        {
            return "Local\\Glukoboy.Regression." + Guid.NewGuid().ToString("N");
        }

        private static void LaunchSignals(Action<bool, string> check)
        {
            string name = UniqueEventName();
            using (LaunchRequest owner = new LaunchRequest(name))
            {
                check(!owner.TakePending(), "New launch event starts empty");
                owner.Request();
                using (LaunchRequest anotherHandle = new LaunchRequest(name))
                {
                    check(anotherHandle.TakePending(), "Request survives until a listener attaches during startup");
                    check(!owner.TakePending(), "Auto-reset request is consumed once across handles");
                    anotherHandle.Request();
                    check(owner.TakePending(), "Second launch notifies the running instance");
                    anotherHandle.Request();
                    anotherHandle.Request();
                    check(owner.TakePending() && !owner.TakePending(), "Repeated pending launches coalesce into one schedule check");
                    anotherHandle.Request();
                }
                check(owner.TakePending(), "Request survives the requesting instance's disposal");
                owner.Request();
            }
            using (LaunchRequest fresh = new LaunchRequest(name))
                check(!fresh.TakePending(), "Disposed instance handles cannot leave a stale request");
        }

        private static void HeartbeatDispatch(Action<bool, string> check)
        {
            // Test actual event -> CheckOnLaunch -> reset routing without the
            // GameContext ctor (hotkeys) or active scheduling (audio/forms/focus).
            // exiting=true deliberately leaves window creation/restoration untested.
            using (LaunchRequest listener = new LaunchRequest(UniqueEventName()))
            {
                GameContext context = (GameContext)FormatterServices.GetUninitializedObject(typeof(GameContext));
                GC.SuppressFinalize(context);
                Settings settings = new Settings();
                NightSession session = new NightSession();
                typeof(GameContext).GetField("settings", PrivateInstance).SetValue(context, settings);
                typeof(GameContext).GetField("nightSession", PrivateInstance).SetValue(context, session);
                typeof(GameContext).GetField("launchRequest", PrivateInstance).SetValue(context, listener);
                typeof(GameContext).GetField("exiting", PrivateInstance).SetValue(context, true);
                MethodInfo heartbeat = typeof(GameContext).GetMethod("OnHeartbeat", PrivateInstance);
                DateTime end;
                check(session.TrySkip(settings, Evening, out end), "Heartbeat fixture starts with a parent skip");
                heartbeat.Invoke(context, new object[] { null, EventArgs.Empty });
                check(!Blocked(session, settings, Evening), "Ordinary heartbeat preserves the parent skip");
                listener.Request();
                heartbeat.Invoke(context, new object[] { null, EventArgs.Empty });
                check(Blocked(session, settings, Evening), "Actual heartbeat dispatch resets skip after a launch request");
                check(!listener.TakePending(), "Actual heartbeat consumes the launch request");
                check(session.TrySkip(settings, Evening, out end), "Parent can skip again after dispatched launch");
                heartbeat.Invoke(context, new object[] { null, EventArgs.Empty });
                check(!Blocked(session, settings, Evening), "Consumed launch does not clear a later parent skip");
            }
        }

        private static void DisposedNightWindow(Action<bool, string> check)
        {
            int failures = 0;
            using (NightForm form = new NightForm(Evening, Evening.AddHours(10), new Settings(), delegate(Exception ex) { failures++; }))
            {
                form.Dispose();
                form.EnsureVisible();
                check(form.IsDisposed && !form.Visible && !form.IsHandleCreated, "EnsureVisible cannot resurrect a disposed night window");
                check(failures == 0, "Disposed window visibility check is a harmless no-op");
            }
        }
    }
}
