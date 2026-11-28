using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GlukoboyDiagnostics
{
    // Dedicated automatic-capture root only. Never recursively delete: an
    // unexpected child, file, link or changed ownership marker makes us skip it.
    internal sealed class BackgroundCaptureStore
    {
        internal const int MaximumSessions = 8;
        internal const long MaximumSessionBytes = 20L * 1024L * 1024L;
        internal static readonly TimeSpan SessionDuration = TimeSpan.FromHours(6);
        private const string OwnerFile = "session.owner";
        private const string CompleteFile = "session.complete";
        private const string OwnerPrefix = "Glukoboy automatic diagnostics v1\r\n";
        private static readonly Regex SessionName = new Regex(@"^session-\d{8}T\d{13}Z-[0-9a-f]{32}$", RegexOptions.CultureInvariant);
        private static readonly HashSet<string> KnownFiles = new HashSet<string>(new string[] {
            OwnerFile, CompleteFile, "manifest.json", "events.jsonl", "summary.json"
        }, StringComparer.OrdinalIgnoreCase);

        internal BackgroundCaptureStore(string root)
        {
            Root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            ValidateRoot();
            Directory.CreateDirectory(Root);
        }

        internal string Root { get; private set; }

        internal static bool ShouldRotate(TimeSpan elapsed, bool sizeLimitReached)
        {
            return sizeLimitReached || elapsed >= SessionDuration;
        }

        internal string CreateSession()
        {
            // Make room before creating a ninth session, even after an interrupted
            // earlier run. A deletion failure stops recording instead of growing forever.
            ValidateRoot();
            bool ignored;
            List<string> sessions = GetSessions(out ignored);
            for (int i = MaximumSessions - 1; i < sessions.Count; i++)
            {
                if (!DeleteOwnedSession(sessions[i]))
                    throw new IOException("Не удалось освободить место для следующей записи диагностики. Сохраните доступный отчёт.");
            }
            string name = "session-" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfffffff'Z'", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
            string directory = Path.Combine(Root, name);
            Directory.CreateDirectory(directory);
            using (FileStream stream = new FileStream(Path.Combine(directory, OwnerFile), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(OwnerPrefix + name);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
            return directory;
        }

        internal void MarkComplete(string directory)
        {
            if (!IsOwnedSession(directory)) throw new IOException("Не удалось подтвердить папку записи диагностики.");
            using (FileStream stream = new FileStream(Path.Combine(directory, CompleteFile), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                byte[] bytes = Encoding.UTF8.GetBytes("complete\r\n");
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
        }

        // Newest first: export includes the incident before older context even if
        // a source has become unavailable or been altered outside the collector.
        internal List<string> GetSessions(out bool skipped)
        {
            ValidateRoot();
            skipped = false;
            List<string> sessions = new List<string>();
            if (!Directory.Exists(Root)) return sessions;
            foreach (string entry in Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.TopDirectoryOnly))
            {
                if (IsOwnedSession(entry)) sessions.Add(entry);
                else skipped = true;
            }
            sessions.Sort(delegate(string a, string b) { return StringComparer.Ordinal.Compare(Path.GetFileName(b), Path.GetFileName(a)); });
            return sessions;
        }

        internal bool IsComplete(string directory)
        {
            if (!IsOwnedSession(directory)) return false;
            string path = Path.Combine(directory, CompleteFile);
            try { return File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == "complete\r\n"; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal bool IsOwnedSession(string directory)
        {
            try
            {
                string full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!String.Equals(Path.GetDirectoryName(full), Root, StringComparison.OrdinalIgnoreCase) || !SessionName.IsMatch(Path.GetFileName(full))) return false;
                ReportPackage.RejectReparseAncestors(full);
                if ((File.GetAttributes(full) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory) return false;
                long total = 0;
                foreach (string entry in Directory.EnumerateFileSystemEntries(full, "*", SearchOption.TopDirectoryOnly))
                {
                    if (!KnownFiles.Contains(Path.GetFileName(entry))) return false;
                    if ((File.GetAttributes(entry) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return false;
                    long length = new FileInfo(entry).Length;
                    long limit = String.Equals(Path.GetFileName(entry), "events.jsonl", StringComparison.OrdinalIgnoreCase) ? MaximumSessionBytes : 16384L;
                    if (length > limit || (total += length) > MaximumSessionBytes) return false;
                }
                string owner = Path.Combine(full, OwnerFile);
                if (!File.Exists(owner) || new FileInfo(owner).Length > 256) return false;
                return File.ReadAllText(owner, Encoding.UTF8) == OwnerPrefix + Path.GetFileName(full);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private bool DeleteOwnedSession(string directory)
        {
            try
            {
                ValidateRoot();
                if (!IsOwnedSession(directory)) return false;
                string[] files = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly);
                // Keep the ownership marker until the last delete. If another
                // program adds anything, the directory remains for manual review.
                foreach (string file in files)
                {
                    if (String.Equals(Path.GetFileName(file), OwnerFile, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!IsOwnedSession(directory)) return false;
                    ReportPackage.RejectReparseAncestors(file, true);
                    File.Delete(file);
                }
                if (!IsOwnedSession(directory)) return false;
                string marker = Path.Combine(directory, OwnerFile);
                ReportPackage.RejectReparseAncestors(marker, true);
                File.Delete(marker);
                Directory.Delete(directory, false);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private void ValidateRoot()
        {
            ReportPackage.RequireLocalDrive(Root);
            ReportPackage.RejectReparseAncestors(Root);
        }
    }
}
