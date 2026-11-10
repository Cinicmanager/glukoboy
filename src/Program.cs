using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyVersion("1.5.0.0")]
[assembly: AssemblyFileVersion("1.5.0.0")]
[assembly: AssemblyTitle("Глюкобой")]
[assembly: AssemblyProduct("Глюкобой")]

namespace Bubliklyuk
{
    internal static class Program
    {
        private static GameContext context;
        [STAThread]
        private static void Main()
        {
            bool owner = false;
            Mutex mutex = null;
            try
            {
                mutex = new Mutex(false, "Local\\Glukoboy.1");
                try { owner = mutex.WaitOne(0); }
                catch (AbandonedMutexException ex) { Diag.Log("Program.Main.WaitOne", ex); owner = true; }
                try { Native.SetProcessDPIAware(); }
                catch (Exception ex) { Diag.Log("Program.Main.SetProcessDPIAware", ex); }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (!owner)
                {
                    Diag.Info("startup: another instance already owns the mutex");
                    using (StartupNotice notice = new StartupNotice("Глюкобой уже работает", 3000))
                        Application.Run(notice);
                    return;
                }
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += OnThreadException;
                AppDomain.CurrentDomain.UnhandledException += OnFatalException;
                Settings settings = Settings.Load();
                LogStartup(settings);
                context = new GameContext(settings);
                Application.Run(context);
            }
            catch (Exception error)
            {
                Diag.Log("Program.Main", error);
                if (context != null) context.Shutdown();
                MessageBox.Show("Не удалось запустить Глюкобой.\r\n" + error.Message,
                    "Глюкобой", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                try { if (context != null) context.Dispose(); }
                finally
                {
                    if (mutex != null)
                    {
                        if (owner) { try { mutex.ReleaseMutex(); } catch { } }
                        mutex.Dispose();
                    }
                }
            }
        }
        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Diag.Log("Program.OnThreadException", e.Exception);
            if (context != null) context.RecoverUi(e.Exception);
        }
        private static void OnFatalException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            if (ex != null) Diag.Log("Program.OnFatalException", ex);
            else Diag.Info("Program.OnFatalException: " + Convert.ToString(e.ExceptionObject, CultureInfo.InvariantCulture));
            if (context != null) context.RestoreAudio();
        }
        private static void LogStartup(Settings settings)
        {
            try
            {
                Diag.Info("startup: version=" + Assembly.GetExecutingAssembly().GetName().Version.ToString() +
                    "; exe=" + Assembly.GetExecutingAssembly().Location +
                    "; Grade=" + settings.Grade.ToString(CultureInfo.InvariantCulture) +
                    "; Level=" + settings.Level.ToString(CultureInfo.InvariantCulture) +
                    "; TestMode=" + (settings.TestMode ? "1" : "0"));
                Diag.Info("screen: " + Screen.PrimaryScreen.Bounds.ToString());
                using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
                    Diag.Info("DPI: " + graphics.DpiX.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { Diag.Log("Program.LogStartup", ex); }
        }
    }

    internal sealed class Settings
    {
        internal int IntervalMin = 15;
        internal int IntervalMax = 20;
        internal int TaskMin = 3;
        internal int TaskMax = 4;
        internal int Grade = 1;
        internal int Level = 1;
        internal int MathMax = 10;
        internal TimeSpan NightFrom = new TimeSpan(21, 0, 0);
        internal TimeSpan NightTo = new TimeSpan(7, 0, 0);
        internal TimeSpan FreeFrom = new TimeSpan(7, 0, 0);
        internal TimeSpan FreeTo = new TimeSpan(9, 0, 0);
        internal TimeSpan SchoolFrom = new TimeSpan(8, 20, 0);
        internal TimeSpan SchoolTo = new TimeSpan(9, 0, 0);
        internal string SchoolDays = "12345";
        internal int IdleLeave = 60;
        internal bool TestMode;

        internal const string DefaultText =
            "; Глюкобой. UTF-8. После изменений перезапустите программу.\r\n" +
            "; Случайный интервал после атаки: от 1 до 1440 минут.\r\n" +
            "IntervalMin=15\r\nIntervalMax=20\r\n" +
            "; Число заданий за атаку: 3 или 4. Можно указать одинаковые границы.\r\n" +
            "TaskMin=3\r\nTaskMax=4\r\n" +
            "; Grade — класс 1..3. Задания 2–3 класса включаются здесь.\r\n" +
            "Grade=1\r\n" +
            "; Level — уровень 1..3 внутри выбранного класса. Родитель повышает вручную.\r\n" +
            "; Для 1 класса Level = четверть: 1 — начало года, 2 — вторая четверть (короткие слова и картинки), 3 — третья и дальше.\r\n" +
            "Level=1\r\n" +
            "; MathMax действует только в 1 классе, от 3 до 10; шаги 1–3.\r\n" +
            "MathMax=10\r\n" +
            "; Ночь по местному времени, формат ЧЧ:ММ.\r\n" +
            "NightFrom=21:00\r\nNightTo=07:00\r\n" +
            "; Свободное утро: атак по таймеру нет, компьютер работает как обычно.\r\n" +
            "; Одинаковое время (например 07:00 и 07:00) — выключить.\r\n" +
            "FreeFrom=07:00\r\nFreeTo=09:00\r\n" +
            "; Пора в школу: экран-блокировка по будням. Одинаковое время — выключить.\r\n" +
            "; SchoolDays — дни недели цифрами: 1 — понедельник … 7 — воскресенье.\r\n" +
            "SchoolFrom=08:20\r\nSchoolTo=09:00\r\nSchoolDays=12345\r\n" +
            "; Атака уходит сама, если столько минут не трогать клавиатуру и мышь. 0 — выключить.\r\n" +
            "IdleLeave=60\r\n" +
            "; 1 — первая дневная атака через 10 секунд, следующие через минуту.\r\n" +
            "; Ночь и свободное утро действуют и в тестовом режиме.\r\n" +
            "TestMode=0\r\n";

        internal static string FilePath(string name)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
        }
        internal static Settings Load()
        {
            Settings s = new Settings();
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = FilePath("settings.ini");
                if (!File.Exists(path)) File.WriteAllText(path, DefaultText, new UTF8Encoding(true));
                foreach (string original in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = original.Trim().TrimStart('\uFEFF');
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[') continue;
                    int at = line.IndexOf('=');
                    if (at < 1) continue;
                    string value = line.Substring(at + 1);
                    int comment = value.IndexOfAny(new char[] { '#', ';' });
                    if (comment >= 0) value = value.Substring(0, comment);
                    values[line.Substring(0, at).Trim()] = value.Trim();
                }
            }
            catch (Exception ex) { Diag.Log("Settings.Load", ex); }
            // Legacy keys are deliberately ignored.
            s.IntervalMin = ReadInt(values, "IntervalMin", 15, 1, 1440);
            s.IntervalMax = ReadInt(values, "IntervalMax", 20, 1, 1440);
            if (s.IntervalMin > s.IntervalMax) { int v = s.IntervalMin; s.IntervalMin = s.IntervalMax; s.IntervalMax = v; }
            s.TaskMin = ReadInt(values, "TaskMin", 3, 3, 4);
            s.TaskMax = ReadInt(values, "TaskMax", 4, 3, 4);
            if (s.TaskMin > s.TaskMax) { int v = s.TaskMin; s.TaskMin = s.TaskMax; s.TaskMax = v; }
            s.Grade = ReadInt(values, "Grade", 1, 1, 3);
            s.Level = ReadInt(values, "Level", 1, 1, 3);
            s.MathMax = ReadInt(values, "MathMax", 10, 3, 10);
            s.IdleLeave = ReadInt(values, "IdleLeave", 60, 0, 1440);
            s.TestMode = ReadInt(values, "TestMode", 0, 0, 1) == 1;
            s.NightFrom = ReadTime(values, "NightFrom", new TimeSpan(21, 0, 0));
            s.NightTo = ReadTime(values, "NightTo", new TimeSpan(7, 0, 0));
            if (s.NightFrom == s.NightTo)
            {
                s.NightFrom = new TimeSpan(21, 0, 0);
                s.NightTo = new TimeSpan(7, 0, 0);
            }
            s.FreeFrom = ReadTime(values, "FreeFrom", new TimeSpan(7, 0, 0));
            s.FreeTo = ReadTime(values, "FreeTo", new TimeSpan(9, 0, 0));
            s.SchoolFrom = ReadTime(values, "SchoolFrom", new TimeSpan(8, 20, 0));
            s.SchoolTo = ReadTime(values, "SchoolTo", new TimeSpan(9, 0, 0));
            string schoolDays;
            if (values.TryGetValue("SchoolDays", out schoolDays))
            {
                StringBuilder days = new StringBuilder();
                foreach (char day in schoolDays)
                    if (day >= '1' && day <= '7' && days.ToString().IndexOf(day) < 0) days.Append(day);
                if (days.Length > 0) s.SchoolDays = days.ToString();
            }
            return s;
        }
        private static int ReadInt(Dictionary<string, string> values, string key, int fallback, int min, int max)
        {
            string text;
            int value;
            if (!values.TryGetValue(key, out text) ||
                !Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ||
                value < min || value > max) return fallback;
            return value;
        }
        private static TimeSpan ReadTime(Dictionary<string, string> values, string key, TimeSpan fallback)
        {
            string text;
            if (!values.TryGetValue(key, out text) || text.Length != 5 || text[2] != ':') return fallback;
            int hours, minutes;
            if (!Int32.TryParse(text.Substring(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out hours) ||
                !Int32.TryParse(text.Substring(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out minutes) ||
                hours > 23 || minutes > 59) return fallback;
            return new TimeSpan(hours, minutes, 0);
        }
    }

    internal static class TimeWindow
    {
        internal static bool Current(TimeSpan from, TimeSpan to, DateTime now, out DateTime start, out DateTime end)
        {
            start = now.Date.Add(from);
            end = now.Date.Add(to);
            if (from == to) return false;
            if (from > to)
            {
                if (now.TimeOfDay >= from) end = end.AddDays(1);
                else if (now.TimeOfDay < to) start = start.AddDays(-1);
                else return false;
            }
            else if (now.TimeOfDay < from || now.TimeOfDay >= to) return false;
            return now >= start && now < end;
        }
    }

    internal static class NightSchedule
    {
        internal static bool Current(Settings s, DateTime now, out DateTime start, out DateTime end)
        {
            return TimeWindow.Current(s.NightFrom, s.NightTo, now, out start, out end);
        }
        internal static DateTime NextStart(Settings s, DateTime now)
        {
            DateTime next = now.Date.Add(s.NightFrom);
            return next <= now ? next.AddDays(1) : next;
        }
    }

    internal sealed class GameContext : ApplicationContext
    {
        private readonly Settings settings;
        private readonly TaskGenerator generator;
        private readonly Random random = new Random();
        private readonly System.Windows.Forms.Timer cycleTimer;
        private readonly System.Windows.Forms.Timer heartbeat;
        private readonly HotKeyWindow hotKey;
        private readonly AudioMute audio = new AudioMute();
        private readonly List<StartupNotice> notices = new List<StartupNotice>();
        private readonly string bankWarning;
        private AttackForm attack;
        private NightForm night;
        private SchoolForm school;
        private DateTime schoolSkipUntil = DateTime.MinValue;
        private bool waitingForSchool;
        private DateTime skipUntil = DateTime.MinValue;
        private DateTime warnedStart = DateTime.MinValue;
        private bool waitingForMorning;
        private bool waitingForInput;
        private bool initialized;
        private bool exiting;
        private bool recovering;
        private bool openingAttack;
        private bool showingFailureNotice;
        private long lastFailureNotice;

        internal GameContext(Settings settings)
        {
            this.settings = settings;
            ReadingBank bank = ReadingBank.Load();
            bankWarning = bank.Warning;
            generator = new TaskGenerator(settings, bank);
            cycleTimer = new System.Windows.Forms.Timer();
            cycleTimer.Tick += OnCycle;
            heartbeat = new System.Windows.Forms.Timer();
            heartbeat.Interval = 1000;
            heartbeat.Tick += OnHeartbeat;
            try { hotKey = new HotKeyWindow(AttackNow, Shutdown, SkipNight); }
            catch (Exception ex) { Diag.Log("GameContext.ctor.HotKeyWindow", ex); cycleTimer.Dispose(); heartbeat.Dispose(); throw; }
            if (hotKey.Unavailable.Count > 0)
                Diag.Info("unavailable hotkeys: " + String.Join(", ", hotKey.Unavailable.ToArray()));
            try
            {
                DateTime saved;
                if (DateTime.TryParseExact(File.ReadAllText(Settings.FilePath("night-state.txt"), Encoding.UTF8).Trim(),
                    "s", CultureInfo.InvariantCulture, DateTimeStyles.None, out saved)) skipUntil = saved;
            }
            catch (Exception ex) { Diag.Log("GameContext.ctor.LoadNightState", ex); }
            try
            {
                DateTime saved;
                if (DateTime.TryParseExact(File.ReadAllText(Settings.FilePath("school-state.txt"), Encoding.UTF8).Trim(),
                    "s", CultureInfo.InvariantCulture, DateTimeStyles.None, out saved)) schoolSkipUntil = saved;
            }
            catch (Exception ex) { Diag.Log("GameContext.ctor.LoadSchoolState", ex); }
            // First callback runs only after Application.Run starts its message loop.
            cycleTimer.Interval = 1;
            cycleTimer.Start();
        }
        private bool IsBlocked(DateTime now, out DateTime start, out DateTime end)
        {
            return NightSchedule.Current(settings, now, out start, out end) && skipUntil != end;
        }
        private bool IsSchoolWindow(DateTime now, out DateTime start, out DateTime end)
        {
            if (!TimeWindow.Current(settings.SchoolFrom, settings.SchoolTo, now, out start, out end)) return false;
            int day = start.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)start.DayOfWeek;
            return settings.SchoolDays.IndexOf((char)('0' + day)) >= 0;
        }
        private bool IsSchoolBlocked(DateTime now, out DateTime start, out DateTime end)
        {
            return IsSchoolWindow(now, out start, out end) && schoolSkipUntil != end;
        }
        private bool IsWarningWindow(DateTime now)
        {
            double remaining = (NightSchedule.NextStart(settings, now) - now).TotalSeconds;
            return remaining > 0 && remaining <= 300;
        }
        private void ScheduleDay(bool first)
        {
            cycleTimer.Stop();
            if (exiting || waitingForInput || night != null || school != null || attack != null) return;
            DateTime start, end;
            DateTime now = DateTime.Now;
            if (IsBlocked(now, out start, out end)) { EnterNight(start, end); return; }
            if (IsSchoolBlocked(now, out start, out end)) { EnterSchool(start, end); return; }
            waitingForSchool = IsSchoolWindow(now, out start, out end);
            if (waitingForSchool) { waitingForMorning = false; return; }
            if (IsWarningWindow(now)) { waitingForMorning = true; return; }
            waitingForMorning = false;
            int interval = settings.TestMode ? (first ? 10000 : 60000) :
                random.Next(settings.IntervalMin, settings.IntervalMax + 1) * 60000;
            DateTime moment = now.AddMilliseconds(interval);
            if (TimeWindow.Current(settings.FreeFrom, settings.FreeTo, now, out start, out end) ||
                TimeWindow.Current(settings.FreeFrom, settings.FreeTo, moment, out start, out end))
                moment = end.AddSeconds(settings.TestMode ? 60 :
                    random.Next(settings.IntervalMin, settings.IntervalMax + 1) * 60);
            cycleTimer.Interval = Math.Max(1000, (int)(moment - now).TotalMilliseconds);
            cycleTimer.Start();
        }
        private void OnCycle(object sender, EventArgs e)
        {
            cycleTimer.Stop();
            if (exiting) return;
            if (!initialized)
            {
                initialized = true;
                heartbeat.Start();
                try
                {
                    CheckSchedule();
                    if (night == null && school == null && !waitingForMorning) ScheduleDay(true);
                    DateTime freeStart, freeEnd;
                    string message = night != null ? "Глюкобой запущен. Ночной режим до " + settings.NightTo.ToString(@"hh\:mm") :
                        waitingForMorning ? "Глюкобой запущен. Скоро пора остывать." :
                        TimeWindow.Current(settings.FreeFrom, settings.FreeTo, DateTime.Now, out freeStart, out freeEnd) ?
                        "Глюкобой запущен. Свободное утро до " + settings.FreeTo.ToString(@"hh\:mm") + "." :
                        "Глюкобой запущен. Атака через " + (settings.TestMode ? "10 секунд." :
                        (cycleTimer.Interval / 60000).ToString(CultureInfo.InvariantCulture) + " мин.");
                    if (night == null && school == null && !waitingForMorning && !waitingForSchool)
                        ShowNotice(message + "\r\nCtrl+Alt+B — атака, Ctrl+Alt+Q — выход", 3000);
                    string warning = bankWarning;
                    if (hotKey.Unavailable.Count > 0)
                        warning += (warning.Length > 0 ? "\r\n\r\n" : "") +
                            "Сочетания заняты или недоступны:\r\n" + String.Join("\r\n", hotKey.Unavailable.ToArray());
                    if (warning.Length > 0)
                        ShowInformation(warning + "\r\n\r\nПрограмма продолжит работу.");
                }
                catch (Exception ex) { Diag.Log("GameContext.OnCycle", ex); RecoverUi(ex); }
                return;
            }
            if (waitingForInput) return;
            DateTime now = DateTime.Now;
            DateTime start, end;
            if (!IsBlocked(now, out start, out end) && !IsSchoolWindow(now, out start, out end) &&
                !IsWarningWindow(now) &&
                TimeWindow.Current(settings.FreeFrom, settings.FreeTo, now, out start, out end))
            {
                Diag.Info("attack: free morning");
                ScheduleDay(false);
                return;
            }
            AttackNow();
        }
        private void OnHeartbeat(object sender, EventArgs e)
        {
            try
            {
                CheckSchedule();
                CheckIdle();
            }
            catch (Exception ex) { Diag.Log("GameContext.OnHeartbeat", ex); RecoverUi(ex); }
        }
        private void CheckIdle()
        {
            if (exiting || (!waitingForInput && (attack == null || settings.IdleLeave <= 0))) return;
            TimeSpan idle = Native.IdleTime();
            DateTime now = DateTime.Now;
            DateTime start, end;
            if (attack != null && settings.IdleLeave > 0)
            {
                if (!IsBlocked(now, out start, out end) && !IsSchoolWindow(now, out start, out end) &&
                    idle >= TimeSpan.FromMinutes(settings.IdleLeave))
                {
                    Diag.Info("attack: idle leave");
                    cycleTimer.Stop();
                    waitingForInput = true;
                    AttackForm old = attack;
                    attack = null;
                    old.FormClosed -= OnAttackClosed;
                    try { old.Abort(); }
                    finally { audio.Restore(); }
                    return;
                }
                attack.UpdateIdleCountdown(idle);
            }
            if (waitingForInput && night == null && school == null && idle < TimeSpan.FromSeconds(5))
            {
                waitingForInput = false;
                ScheduleDay(false);
                string message = "БублиГлюк заскучал и ушёл.";
                if (cycleTimer.Enabled)
                    message += "\r\nСледующая атака через " +
                        Math.Max(1, cycleTimer.Interval / 60000).ToString(CultureInfo.InvariantCulture) + " мин.";
                ShowNotice(message, 5000);
            }
        }
        private void CheckSchedule()
        {
            if (exiting) return;
            DateTime now = DateTime.Now;
            DateTime start, end;
            if (IsBlocked(now, out start, out end))
            {
                if (night == null || night.EndTime != end) EnterNight(start, end);
                return;
            }
            if (night != null)
            {
                CloseNight();
                audio.Restore();
                ScheduleDay(false);
            }
            if (IsSchoolBlocked(now, out start, out end))
            {
                if (school == null || school.EndTime != end) EnterSchool(start, end);
                return;
            }
            if (school != null)
            {
                CloseSchool();
                audio.Restore();
                ScheduleDay(false);
            }
            if (IsSchoolWindow(now, out start, out end))
            {
                waitingForSchool = true;
                cycleTimer.Stop();
                return;
            }
            if (waitingForSchool)
            {
                waitingForSchool = false;
                if (attack == null) ScheduleDay(false);
            }
            if (IsWarningWindow(now))
            {
                waitingForMorning = true;
                cycleTimer.Stop();
                DateTime next = NightSchedule.NextStart(settings, now);
                if (warnedStart != next)
                {
                    warnedStart = next;
                    int minutes = Math.Max(1, (int)Math.Ceiling((next - now).TotalMinutes));
                    ShowNotice("Через " + minutes.ToString(CultureInfo.InvariantCulture) +
                        " мин. ноутбук пойдёт остывать.\r\nПора заканчивать.", 5000);
                }
            }
            else if (waitingForMorning)
            {
                waitingForMorning = false;
                if (attack == null) ScheduleDay(false);
            }
        }
        private void AttackNow()
        {
            if (exiting) { Diag.Info("attack: exiting"); return; }
            if (!initialized) { Diag.Info("attack: not initialized"); return; }
            waitingForInput = false;
            if (night != null) { Diag.Info("attack: night window open"); return; }
            DateTime now = DateTime.Now;
            DateTime start, end;
            if (IsBlocked(now, out start, out end))
            {
                Diag.Info("attack: night schedule");
                EnterNight(start, end);
                return;
            }
            if (school != null || IsSchoolWindow(now, out start, out end))
            {
                Diag.Info("attack: school window");
                CheckSchedule();
                return;
            }
            if (attack != null) { Diag.Info("attack: already open"); return; }
            if (IsWarningWindow(now)) { Diag.Info("attack: night warning window"); CheckSchedule(); return; }
            cycleTimer.Stop();
            openingAttack = true;
            try
            {
                audio.Begin();
                int count = random.Next(settings.TaskMin, settings.TaskMax + 1);
                generator.BeginAttack(count);
                attack = new AttackForm(settings, generator, count, RecoverUi);
                attack.FormClosed += OnAttackClosed;
                attack.Show();
            }
            catch (Exception ex) { Diag.Log("GameContext.AttackNow", ex); RecoverUi(ex); }
            finally { openingAttack = false; }
        }
        private void OnAttackClosed(object sender, FormClosedEventArgs e)
        {
            if (!Object.ReferenceEquals(sender, attack)) return;
            attack.FormClosed -= OnAttackClosed;
            attack = null;
            // Check the clock before restoration, so 21:00 transfers the audio owner.
            DateTime start, end;
            if (!exiting && IsBlocked(DateTime.Now, out start, out end)) EnterNight(start, end);
            else if (!exiting && IsSchoolBlocked(DateTime.Now, out start, out end)) EnterSchool(start, end);
            else { audio.Restore(); ScheduleDay(false); }
        }
        private void EnterNight(DateTime start, DateTime end)
        {
            cycleTimer.Stop();
            waitingForMorning = false;
            audio.Begin(); // Idempotent: attack -> night retains the original mute.
            waitingForSchool = false;
            CloseSchool();
            AttackForm old = attack;
            attack = null;
            if (old != null)
            {
                old.FormClosed -= OnAttackClosed;
                old.Abort();
            }
            CloseNight();
            night = new NightForm(start, end, RecoverUi);
            night.FormClosed += OnNightClosed;
            night.Show();
        }
        private void OnNightClosed(object sender, FormClosedEventArgs e)
        {
            if (!Object.ReferenceEquals(sender, night)) return;
            night.FormClosed -= OnNightClosed;
            night = null;
            audio.Restore();
            if (e.CloseReason == CloseReason.WindowsShutDown) Shutdown();
            // Unexpected disposal is repaired by the heartbeat.
        }
        private void CloseNight()
        {
            NightForm old = night;
            night = null;
            if (old == null) return;
            old.FormClosed -= OnNightClosed;
            old.Abort();
        }
        private void EnterSchool(DateTime start, DateTime end)
        {
            cycleTimer.Stop();
            waitingForMorning = false;
            waitingForSchool = true;
            audio.Begin();
            AttackForm old = attack;
            attack = null;
            if (old != null)
            {
                old.FormClosed -= OnAttackClosed;
                old.Abort();
            }
            CloseNight();
            CloseSchool();
            school = new SchoolForm(start, end, SkipSchool, RecoverUi);
            school.FormClosed += OnSchoolClosed;
            school.Show();
        }
        private void OnSchoolClosed(object sender, FormClosedEventArgs e)
        {
            if (!Object.ReferenceEquals(sender, school)) return;
            school.FormClosed -= OnSchoolClosed;
            school = null;
            audio.Restore();
            if (e.CloseReason == CloseReason.WindowsShutDown) Shutdown();
            // Unexpected disposal is repaired by the heartbeat.
        }
        private void CloseSchool()
        {
            SchoolForm old = school;
            school = null;
            if (old == null) return;
            old.FormClosed -= OnSchoolClosed;
            old.Abort();
        }
        private void SkipSchool()
        {
            if (exiting || !initialized || school == null) return;
            DateTime start, end;
            DateTime now = DateTime.Now;
            if (IsBlocked(now, out start, out end)) { CheckSchedule(); return; }
            if (!IsSchoolWindow(now, out start, out end) || school.EndTime != end)
            {
                CheckSchedule();
                return;
            }
            schoolSkipUntil = end;
            try
            {
                File.WriteAllText(Settings.FilePath("school-state.txt"), end.ToString("s", CultureInfo.InvariantCulture),
                    new UTF8Encoding(true));
            }
            catch (Exception ex) { Diag.Log("GameContext.SkipSchool.SaveState", ex); }
            CloseSchool();
            audio.Restore();
            ScheduleDay(false);
        }
        private void SkipNight()
        {
            if (exiting || !initialized) return;
            DateTime start, end;
            if (!NightSchedule.Current(settings, DateTime.Now, out start, out end) || skipUntil == end) return;
            skipUntil = end;
            string warning = "";
            try
            {
                File.WriteAllText(Settings.FilePath("night-state.txt"), end.ToString("s", CultureInfo.InvariantCulture),
                    new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                Diag.Log("GameContext.SkipNight.SaveState", ex);
                warning = "Пропуск ночи действует до перезапуска: срок не удалось сохранить.";
            }
            // A hotkey can arrive just after NightFrom but before the next heartbeat.
            // End a still-visible daytime attack before releasing its audio owner.
            AttackForm oldAttack = attack;
            attack = null;
            if (oldAttack != null)
            {
                oldAttack.FormClosed -= OnAttackClosed;
                oldAttack.Abort();
            }
            CloseNight();
            CloseSchool();
            audio.Restore();
            ScheduleDay(false);
            ShowNotice("Ночь пропущена до " + end.ToString("HH:mm", CultureInfo.InvariantCulture), 3000);
            if (warning.Length > 0) ShowInformation(warning);
        }
        internal void RecoverUi() { RecoverUi(null); }
        internal void RecoverUi(Exception error)
        {
            if (exiting || recovering || showingFailureNotice) return;
            bool attackFailed = error != null && (attack != null || openingAttack);
            recovering = true;
            try
            {
                audio.Restore();
                AttackForm old = attack;
                attack = null;
                if (old != null)
                {
                    old.FormClosed -= OnAttackClosed;
                    try { old.Abort(); }
                    catch (Exception ex) { Diag.Log("GameContext.RecoverUi.AbortAttack", ex); }
                }
                try { CloseNight(); }
                catch (Exception ex) { Diag.Log("GameContext.RecoverUi.CloseNight", ex); }
                try { CloseSchool(); }
                catch (Exception ex) { Diag.Log("GameContext.RecoverUi.CloseSchool", ex); }
            }
            finally
            {
                recovering = false;
                // Night and school recovery is retried by heartbeat; never recursively rebuild a broken form.
                try
                {
                    DateTime start, end;
                    DateTime now = DateTime.Now;
                    if (!IsBlocked(now, out start, out end) && !IsSchoolBlocked(now, out start, out end)) ScheduleDay(false);
                }
                finally { if (attackFailed) ShowAttackFailure(error); }
            }
        }
        private void ShowAttackFailure(Exception error)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (showingFailureNotice || (lastFailureNotice != 0 &&
                (now - lastFailureNotice) / (double)System.Diagnostics.Stopwatch.Frequency < 60)) return;
            lastFailureNotice = now;
            showingFailureNotice = true;
            try
            {
                ShowNotice("Атака сорвалась: " + error.Message + ". Подробности в errors.log", 8000);
            }
            catch (Exception ex) { Diag.Log("GameContext.ShowAttackFailure", ex); }
            finally { showingFailureNotice = false; }
        }
        internal void RestoreAudio() { audio.Restore(); }
        private void ShowInformation(string text)
        {
            MessageBox.Show(night != null ? (Form)night : school, text, "Глюкобой", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        private void ShowNotice(string text, int duration)
        {
            for (int i = notices.Count - 1; i >= 0; i--)
            {
                if (!notices[i].IsDisposed) { notices[i].Close(); notices[i].Dispose(); }
                notices.RemoveAt(i);
            }
            StartupNotice notice = new StartupNotice(text, duration);
            notice.Owner = night != null ? (Form)night : school != null ? (Form)school : attack;
            notices.Add(notice);
            notice.Show();
        }
        internal void Shutdown()
        {
            if (exiting) return;
            exiting = true;
            try
            {
                cycleTimer.Stop();
                heartbeat.Stop();
                audio.Restore();
                if (attack != null)
                {
                    AttackForm old = attack;
                    attack = null;
                    old.FormClosed -= OnAttackClosed;
                    try { old.Abort(); }
                    catch (Exception ex) { Diag.Log("GameContext.Shutdown.AbortAttack", ex); }
                }
                try { CloseNight(); }
                catch (Exception ex) { Diag.Log("GameContext.Shutdown.CloseNight", ex); }
                try { CloseSchool(); }
                catch (Exception ex) { Diag.Log("GameContext.Shutdown.CloseSchool", ex); }
                foreach (StartupNotice notice in notices)
                {
                    try { if (!notice.IsDisposed) { notice.Close(); notice.Dispose(); } }
                    catch (Exception ex) { Diag.Log("GameContext.Shutdown.CloseNotice", ex); }
                }
                notices.Clear();
            }
            finally
            {
                try { audio.Restore(); }
                catch (Exception ex) { Diag.Log("GameContext.Shutdown.RestoreAudio", ex); }
                try { hotKey.Dispose(); }
                catch (Exception ex) { Diag.Log("GameContext.Shutdown.DisposeHotKeys", ex); }
                ExitThread();
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Shutdown();
                cycleTimer.Dispose();
                heartbeat.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class HotKeyWindow : NativeWindow, IDisposable
    {
        private readonly Action attackNow, exit, skipNight;
        private readonly List<int> registered = new List<int>();
        internal readonly List<string> Unavailable = new List<string>();
        internal HotKeyWindow(Action attackNow, Action exit, Action skipNight)
        {
            this.attackNow = attackNow;
            this.exit = exit;
            this.skipNight = skipNight;
            CreateParams cp = new CreateParams();
            cp.Caption = "Glukoboy.MessageOnly";
            cp.Parent = new IntPtr(-3);
            CreateHandle(cp);
            Register(17040, 3, 0x42, "Ctrl+Alt+B");
            Register(17041, 3, 0x51, "Ctrl+Alt+Q");
            Register(17042, 6, 0x7B, "Ctrl+Shift+F12");
            Register(17043, 3, 0x4E, "Ctrl+Alt+N");
        }
        private void Register(int id, uint modifiers, uint key, string name)
        {
            bool success = false;
            try { success = Native.RegisterHotKey(Handle, id, modifiers, key); }
            catch (Exception ex) { Diag.Log("HotKeyWindow.Register " + name, ex); }
            if (success) registered.Add(id); else Unavailable.Add(name);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                int id = m.WParam.ToInt32();
                if (id == 17040) { Diag.Info("hotkey Ctrl+Alt+B"); attackNow(); return; }
                if (id == 17041 || id == 17042)
                {
                    Diag.Info(id == 17041 ? "hotkey Ctrl+Alt+Q" : "hotkey Ctrl+Shift+F12");
                    exit(); return;
                }
                if (id == 17043) { Diag.Info("hotkey Ctrl+Alt+N"); skipNight(); return; }
            }
            base.WndProc(ref m);
        }
        public void Dispose()
        {
            foreach (int id in registered) Native.UnregisterHotKey(Handle, id);
            registered.Clear();
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }

    internal sealed class StartupNotice : Form
    {
        private readonly System.Windows.Forms.Timer lifetime;
        private readonly Font noticeFont;
        internal StartupNotice(string text, int milliseconds)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(0x17, 0x1B, 0x35);
            Padding = new Padding(20);
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int width = Math.Min(620, Math.Max(1, area.Width - 32));
            noticeFont = new Font("Segoe UI", 18, FontStyle.Regular, GraphicsUnit.Pixel);
            Size measured = TextRenderer.MeasureText(text, noticeFont, new Size(Math.Max(1, width - 40), 2000),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int height = Math.Min(Math.Max(110, measured.Height + 40), Math.Max(1, area.Height - 32));
            Bounds = new Rectangle(area.Right - width - 16, area.Bottom - height - 16, width, height);
            Label message = new Label();
            message.Dock = DockStyle.Fill;
            message.Font = noticeFont;
            message.ForeColor = Color.FromArgb(0xA0, 0xF0, 0x00);
            message.TextAlign = ContentAlignment.MiddleLeft;
            message.UseMnemonic = false;
            message.Text = text;
            Controls.Add(message);
            lifetime = new System.Windows.Forms.Timer();
            lifetime.Interval = milliseconds;
            lifetime.Tick += Expire;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080; return cp; }
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); lifetime.Start(); }
        private void Expire(object sender, EventArgs e) { lifetime.Stop(); Close(); }
        protected override void OnFormClosed(FormClosedEventArgs e) { lifetime.Stop(); base.OnFormClosed(e); }
        protected override void Dispose(bool disposing)
        {
            if (disposing && lifetime != null) lifetime.Dispose();
            base.Dispose(disposing);
            if (disposing && noticeFont != null) noticeFont.Dispose();
        }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            internal uint cbSize;
            internal uint dwTime;
        }
        private static bool idleFailureLogged;
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        internal static TimeSpan IdleTime()
        {
            try
            {
                LASTINPUTINFO info = new LASTINPUTINFO();
                info.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
                if (!GetLastInputInfo(ref info))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
            }
            catch (Exception ex)
            {
                if (!idleFailureLogged)
                {
                    idleFailureLogged = true;
                    Diag.Log("Native.IdleTime", ex);
                }
                return TimeSpan.Zero;
            }
        }
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr window);
    }
}
