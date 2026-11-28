using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Bubliklyuk
{
    internal static class DiagnosticLauncherChecks
    {
        internal static void Run(Action<bool, string> check, string artifacts)
        {
            string local = Path.Combine(artifacts, "diagnostic-launcher");
            byte[] embedded;
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(DiagnosticLauncher.ResourceName))
            {
                check(resource != null, "EXE-only updates contain the diagnostics resource");
                using (MemoryStream buffer = new MemoryStream()) { resource.CopyTo(buffer); embedded = buffer.ToArray(); }
            }
            check(embedded.Length > 2 && embedded[0] == 'M' && embedded[1] == 'Z', "Embedded diagnostics is a Windows executable");
            Func<Stream> open = delegate { return new MemoryStream(embedded, false); };
            ProcessStartInfo captured = null;
            int launches = 0;
            Action<ProcessStartInfo> capture = delegate(ProcessStartInfo info) { captured = info; launches++; };
            check(DiagnosticLauncher.TryStart(local, open, capture), "Embedded diagnostics extracts and reaches injected launcher");
            string expected = Path.Combine(Path.Combine(Path.Combine(Path.Combine(local, "GlukoboyDiagnostics"), "bin"), Hash(embedded)), DiagnosticLauncher.ResourceName);
            check(captured != null && captured.FileName == expected, "Diagnostics is launched only from the content-addressed per-user cache");
            check(Hash(File.ReadAllBytes(expected)) == Hash(embedded), "Extracted diagnostics matches the exact embedded bytes");
            check(captured.Arguments == "--background" && !captured.UseShellExecute && captured.CreateNoWindow &&
                captured.WindowStyle == ProcessWindowStyle.Hidden && !captured.ErrorDialog,
                "Diagnostics uses a hidden background process without shell or dialog");
            check(captured.WorkingDirectory == Path.GetDirectoryName(expected), "Diagnostics working directory is independent of the game");
            check(Directory.GetFiles(local, "*.tmp", SearchOption.AllDirectories).Length == 0, "Successful extraction leaves no temporary files");

            DateTime unchanged = new DateTime(2020, 1, 2, 3, 4, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(expected, unchanged);
            check(DiagnosticLauncher.TryStart(local, open, capture) && launches == 2, "An identical embedded collector can be reused");
            check(File.GetLastWriteTimeUtc(expected) == unchanged, "Existing exact collector bytes are not rewritten");

            byte[] changed = (byte[])embedded.Clone();
            changed[changed.Length - 1] ^= 1;
            check(DiagnosticLauncher.TryStart(local, delegate { return new MemoryStream(changed, false); }, capture), "A new collector version uses a separate cache entry");
            check(captured.FileName != expected && File.GetLastWriteTimeUtc(expected) == unchanged &&
                Hash(File.ReadAllBytes(expected)) == Hash(embedded), "Updating the embedded collector preserves the earlier version");

            byte[] corrupt = (byte[])embedded.Clone();
            corrupt[corrupt.Length / 2] ^= 1;
            File.WriteAllBytes(expected, corrupt);
            int beforeFailure = launches;
            check(!DiagnosticLauncher.TryStart(local, open, capture) && launches == beforeFailure,
                "A same-length corrupted cache entry is never launched");
            check(Hash(File.ReadAllBytes(expected)) == Hash(corrupt), "An unverified existing cache entry is preserved");

            string missing = Path.Combine(artifacts, "diagnostic-missing-resource");
            check(!DiagnosticLauncher.TryStart(missing, delegate { return null; }, capture) && launches == beforeFailure,
                "A missing embedded resource safely skips launch");
            check(!Directory.Exists(missing), "Missing resource does not create cache folders");
            check(!DiagnosticLauncher.TryStart(missing, delegate { return new MemoryStream(new byte[0]); }, capture), "An empty resource safely skips launch");

            string failedStart = Path.Combine(artifacts, "diagnostic-failed-start");
            check(!DiagnosticLauncher.TryStart(failedStart, open, delegate(ProcessStartInfo info) { throw new IOException("Injected process failure"); }),
                "A process startup failure cannot escape to the game");
            check(Directory.GetFiles(failedStart, "*.tmp", SearchOption.AllDirectories).Length == 0, "Failed process start leaves no temporary extraction files");

            string blocked = Path.Combine(artifacts, "diagnostic-obstructed-cache");
            Directory.CreateDirectory(blocked);
            File.WriteAllText(Path.Combine(blocked, "GlukoboyDiagnostics"), "existing user file", Encoding.UTF8);
            check(!DiagnosticLauncher.TryStart(blocked, open, capture) && launches == beforeFailure,
                "A file obstructing the cache path safely skips launch");
            check(File.ReadAllText(Path.Combine(blocked, "GlukoboyDiagnostics")) == "existing user file", "Cache setup preserves obstructing user files");

            foreach (string invalid in new string[] { null, "", "relative-cache", "C:relative-cache", @"\\localhost\share\cache", @"\\?\C:\cache" })
                check(!DiagnosticLauncher.TryStart(invalid, open, capture) && launches == beforeFailure, "Unsafe cache root is rejected: " + invalid);
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
