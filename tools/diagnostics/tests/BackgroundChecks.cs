using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Windows.Forms;

namespace GlukoboyDiagnostics.Tests
{
    internal static class BackgroundChecks
    {
        private static int assertions;
        private static void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
        private static string Text(ZipArchive archive, string name)
        { using (StreamReader reader = new StreamReader(archive.GetEntry(name).Open())) return reader.ReadToEnd(); }
        private static void Seed(string directory, string marker, long length)
        {
            File.WriteAllText(Path.Combine(directory, "manifest.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "summary.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "events.jsonl"), marker);
            using (FileStream file = new FileStream(Path.Combine(directory, "events.jsonl"), FileMode.Open)) file.SetLength(length);
        }

        [STAThread] private static int Main(string[] args)
        {
            try
            {
                string root = Path.GetFullPath(args[0]);
                Directory.CreateDirectory(root);
                BackgroundCaptureStore store = new BackgroundCaptureStore(Path.Combine(root, "AutomaticCapture"));
                Check(!BackgroundCaptureStore.ShouldRotate(TimeSpan.FromHours(5.99), false), "Morning session remains active until rotation");
                Check(BackgroundCaptureStore.ShouldRotate(TimeSpan.FromHours(6), false), "Six hours rotates instead of ending background capture");
                Check(BackgroundCaptureStore.ShouldRotate(TimeSpan.FromSeconds(1), true), "Size bound also rotates");
                string unrelated = Path.Combine(store.Root, "my-notes"); Directory.CreateDirectory(unrelated);
                File.WriteAllText(Path.Combine(unrelated, "keep.txt"), "untouched");
                List<string> created = new List<string>();
                for (int i = 0; i < 11; i++)
                {
                    string session = store.CreateSession(); created.Add(session);
                    Seed(session, "session-" + i, 5L * 1024L * 1024L);
                    if (i != 10) store.MarkComplete(session);
                }
                bool skipped;
                List<string> sessions = store.GetSessions(out skipped);
                Check(sessions.Count == 8 && skipped, "Keep eight owned sessions and report unknown directory");
                Check(!Directory.Exists(created[0]) && Directory.Exists(created[10]), "Oldest owned sessions pruned, latest preserved");
                Check(File.ReadAllText(Path.Combine(unrelated, "keep.txt")) == "untouched", "Unknown contents never deleted");
                string archives = Path.Combine(root, "archives");
                string zipPath = ReportPackage.CreateBackground(store.Root, archives, false);
                using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                {
                    long bytes = 0;
                    int journals = 0;
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        Check(entry.FullName.IndexOf('/') < 0 && entry.FullName.IndexOf('\\') < 0, "Archive entries are flat");
                        if (entry.FullName.EndsWith("__events.jsonl")) journals++;
                        bytes += entry.Length;
                    }
                    Check(journals == 8 && bytes > 32L * 1024L * 1024L, "All eight sessions survive crossing the old 32 MiB package bound");
                    Check(bytes < BackgroundCaptureStore.MaximumSessions * BackgroundCaptureStore.MaximumSessionBytes + 65536, "Export source bytes bounded");
                    Check(zip.GetEntry(Path.GetFileName(created[9]) + "__events.jsonl") != null && zip.GetEntry(Path.GetFileName(created[10]) + "__events.jsonl") != null, "Both sides of rotation boundary exported");
                    Check(Text(zip, "collection-state.txt").Contains("partial=true"), "Interrupted prior/current session marked partial");
                }
                // A locked journal cannot block the other files or conceal a partial export.
                using (FileStream held = new FileStream(Path.Combine(created[10], "events.jsonl"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                using (ZipArchive zip = ZipFile.OpenRead(ReportPackage.CreateBackground(store.Root, archives, true)))
                    Check(Text(zip, "collection-state.txt").Contains("partial=true"), "Locked writer yields marked partial export");
                string tainted = created[3];
                File.WriteAllText(Path.Combine(tainted, "user-file.txt"), "keep this too");
                store.CreateSession();
                Check(File.Exists(Path.Combine(tainted, "user-file.txt")), "Owned-looking session with unknown file is preserved");

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (DiagnosticForm form = new DiagnosticForm(true, true, null))
                using (Timer timer = new Timer())
                {
                    int shown = 0, activated = 0;
                    bool hiddenAtTick = false;
                    form.Shown += delegate { shown++; };
                    form.Activated += delegate { activated++; };
                    timer.Interval = 100;
                    timer.Tick += delegate { hiddenAtTick = !form.Visible; timer.Stop(); Application.ExitThread(); };
                    timer.Start();
                    Program.RunForm(form, true);
                    Check(hiddenAtTick && shown == 0 && activated == 0, "Actual background message loop never shows or activates the form");
                    Check(!form.CaptureStarted, "Fixture never starts Collector");
                }
                Console.WriteLine("PASS: " + assertions + " background fixture assertions; " + root);
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
