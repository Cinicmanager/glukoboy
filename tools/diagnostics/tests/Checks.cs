using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace GlukoboyDiagnostics.Tests
{
    internal static class Checks
    {
        private static int count;
        private static void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
        private static bool Find(object node, Predicate<Dictionary<string, object>> predicate)
        {
            Dictionary<string, object> map = node as Dictionary<string, object>;
            if (map != null)
            {
                if (predicate(map)) return true;
                foreach (object child in map.Values) if (Find(child, predicate)) return true;
            }
            else if (node is IEnumerable && !(node is string))
                foreach (object child in (IEnumerable)node) if (Find(child, predicate)) return true;
            return false;
        }
        private static bool Field(Dictionary<string, object> map, string key, object expected)
        {
            object value; return map.TryGetValue(key, out value) && Object.Equals(value, expected);
        }
        private static void Command(string path, string value)
        {
            File.WriteAllText(path, value);
            Stopwatch limit = Stopwatch.StartNew();
            while (limit.ElapsedMilliseconds < 3000)
            {
                try { if (File.Exists(path + ".ack") && File.ReadAllText(path + ".ack") == value) return; }
                catch (IOException) { }
                Thread.Sleep(15);
            }
            throw new Exception("Fixture did not acknowledge " + value);
        }
        private static List<object> ReadEvents(string directory)
        {
            JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
            List<object> records = new List<object>();
            using (FileStream file = new FileStream(Path.Combine(directory, "events.jsonl"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader text = new StreamReader(file, Encoding.UTF8))
            {
                string line; while ((line = text.ReadLine()) != null) if (line.Length > 0) records.Add(json.DeserializeObject(line));
            }
            return records;
        }
        [STAThread] private static int Main(string[] args)
        {
            Process fixture = null;
            try
            {
                string root = Path.GetFullPath(args[0]), fixtureExe = Path.GetFullPath(args[1]);
                Directory.CreateDirectory(root);
                string fixtureFolder = Path.GetDirectoryName(fixtureExe);
                File.WriteAllText(Path.Combine(fixtureFolder, "settings.ini"), "NightFrom=21:00\r\nNightTo=07:00\r\nSchoolDays=12345\r\nParentPIN=FAKE_PRIVATE_PIN\r\nApiKey=FAKE_SECRET_TOKEN\r\n", new UTF8Encoding(true));
                File.WriteAllText(Path.Combine(fixtureFolder, "errors.log"), new string('x', 160 * 1024) + "\r\nTEST_NIGHT_APPEARED\r\n", new UTF8Encoding(true));
                string control = Path.Combine(root, "fixture-control.txt"), report = Path.Combine(root, "report");
                string fixtureName = Path.GetFileNameWithoutExtension(fixtureExe);
                using (Collector collector = new Collector(report, new string[] { fixtureName }))
                {
                    collector.Sample();
                    fixture = Process.Start(new ProcessStartInfo(fixtureExe, "\"" + control + "\"")
                    { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                    Stopwatch ready = Stopwatch.StartNew();
                    while (!File.Exists(control + ".ready") && ready.ElapsedMilliseconds < 5000) Thread.Sleep(25);
                    Check(File.Exists(control + ".ready"), "Own offscreen fixture started without activation");
                    collector.Sample();
                    Command(control, "minimize"); collector.Sample();
                    Command(control, "hide"); collector.Sample();
                    Command(control, "show"); collector.Sample();
                    Command(control, "hang");
                    Stopwatch probe = Stopwatch.StartNew(); collector.Sample(); probe.Stop();
                    Check(probe.ElapsedMilliseconds < 1200, "Unresponsive target is probed with a bounded wait");
                    Thread.Sleep(1700); collector.Sample();
                    Command(control, "quit");
                    Check(fixture.WaitForExit(3000), "Own fixture exited normally");
                    collector.Sample();
                    collector.Note("test_complete", "Controlled fixture only; no game launched");
                }
                List<object> events = ReadEvents(report);
                Check(events.Count >= 5, "Journal contains structured observations");
                int pid = fixture.Id;
                Predicate<Dictionary<string, object>> target = delegate(Dictionary<string, object> map)
                { object value; return map.ContainsKey("hwnd") && map.TryGetValue("pid", out value) && Convert.ToInt32(value) == pid; };
                Check(Find(events, delegate(Dictionary<string, object> map) { return target(map) && Field(map, "visible", true); }), "Visible target window captured");
                Check(Find(events, delegate(Dictionary<string, object> map) { return target(map) && Field(map, "minimized", true); }), "Minimized target window captured");
                Check(Find(events, delegate(Dictionary<string, object> map) { return target(map) && Field(map, "visible", false); }), "Hidden target window captured");
                Check(Find(events, delegate(Dictionary<string, object> map) { return target(map) && Field(map, "responsiveness_status", "timeout_or_no_result"); }), "Unresponsive window recorded distinctly from success");
                Check(Find(events, delegate(Dictionary<string, object> map) { return target(map) && map.ContainsKey("on_current_virtual_desktop") && map.ContainsKey("dwm_cloaked"); }), "Desktop and cloaking probe data included");
                string journal = File.ReadAllText(Path.Combine(report, "events.jsonl"), Encoding.UTF8);
                Check(journal.Contains("TEST_NIGHT_APPEARED"), "Own application log tail included");
                Check(!journal.Contains("FAKE_PRIVATE_PIN") && !journal.Contains("FAKE_SECRET_TOKEN"), "Unrelated and secret settings omitted");
                Check(journal.Contains("21:00") && journal.Contains("SchoolDays"), "Allowlisted schedule settings included");
                Check(File.Exists(Path.Combine(report, "manifest.json")) && File.Exists(Path.Combine(report, "summary.json")), "Manifest and summary available after stop");
                Dictionary<string, object> summary = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(report, "summary.json"))) as Dictionary<string, object>;
                Check(Convert.ToInt32(summary["processes_discovered"]) == 1 && Convert.ToInt32(summary["processes_disappeared"]) == 1,
                    "Process start and disappearance survive after target exit");
                Check(Find(events, delegate(Dictionary<string, object> map) { return map.ContainsKey("utc") && map.ContainsKey("monotonic_ms"); }),
                    "Wall clock and monotonic time are present together");
                Check(!Find(events, delegate(Dictionary<string, object> map) {
                    object foreground;
                    Dictionary<string, object> value = map.TryGetValue("foreground", out foreground) ? foreground as Dictionary<string, object> : null;
                    return value != null && value.ContainsKey("title");
                }), "Other foreground window titles are never collected");
                string output = Path.Combine(root, "archives"); Directory.CreateDirectory(output);
                string archive = ReportPackage.Create(report, output, false);
                string partial = ReportPackage.Create(report, output, true);
                Check(archive != partial && File.Exists(archive) && File.Exists(partial), "Archives use unique paths and preserve previous report");
                using (ZipArchive zip = ZipFile.OpenRead(archive))
                {
                    Check(zip.GetEntry("events.jsonl") != null && zip.GetEntry("manifest.json") != null && zip.GetEntry("summary.json") != null, "ZIP contains all report data");
                    foreach (ZipArchiveEntry entry in zip.Entries)
                        Check(entry.FullName.IndexOf('/') < 0 && entry.FullName.IndexOf('\\') < 0, "ZIP entries are flat and cannot escape extraction directory");
                }
                bool recursiveRejected = false;
                try { ReportPackage.Create(report, Path.Combine(report, "nested-output"), false); }
                catch (IOException) { recursiveRejected = true; }
                Check(recursiveRejected, "Archive cannot include itself inside source data");
                using (FileStream shared = new FileStream(Path.Combine(report, "events.jsonl"), FileMode.Open, FileAccess.Write, FileShare.Read))
                {
                    string sharedArchive = ReportPackage.Create(report, output, true);
                    Check(File.Exists(sharedArchive), "Partial export can read data while the writer is still open");
                }
                string budgetReport = Path.Combine(root, "budget-report");
                using (Collector budget = new Collector(budgetReport, new string[] { fixtureName }))
                {
                    string message = new string('x', 3000);
                    for (int i = 0; i < 20000 && !budget.IsRecordingLimitReached; i++) budget.Note("budget_test", message);
                    Check(budget.IsRecordingLimitReached, "Recording stops at its finite size budget");
                    long before = new FileInfo(Path.Combine(budgetReport, "events.jsonl")).Length;
                    budget.Note("after_limit", "must not grow the log"); budget.Sample();
                    Check(new FileInfo(Path.Combine(budgetReport, "events.jsonl")).Length == before, "Size limit cannot be bypassed by later samples or notes");
                }
                long total = 0;
                foreach (string file in Directory.GetFiles(budgetReport)) total += new FileInfo(file).Length;
                Check(total <= 20L * 1024 * 1024, "Whole report remains within 20 MiB budget");
                Console.WriteLine("PASS: " + count + " diagnostics integration assertions; " + root);
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally
            {
                if (fixture != null)
                {
                    try { if (!fixture.HasExited) { fixture.Kill(); fixture.WaitForExit(2000); } } catch { }
                    fixture.Dispose();
                }
            }
        }
    }
}
