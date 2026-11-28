using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace GlukoboyDiagnostics
{
    // Construct, Sample and Dispose on one worker thread. Note and the status
    // properties can be used from other threads. Never writes to the game directory.
    internal sealed class Collector : IDisposable
    {
        private const int MaximumProcesses = 32;
        private const int MaximumSourceDirectories = 16;
        private const int MaximumAppLogBytes = 100 * 1024;
        private const int MaximumSettingsBytes = 32 * 1024;
        private const long MaximumJournalBytes = 20 * 1024 * 1024 - 64 * 1024;
        private readonly object gate = new object();
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private readonly UTF8Encoding utf8 = new UTF8Encoding(false);
        private readonly Stopwatch monotonic = Stopwatch.StartNew();
        private readonly NativeProbe native = new NativeProbe();
        private readonly string[] targetNames;
        private readonly Dictionary<int, TrackedProcess> tracked = new Dictionary<int, TrackedProcess>();
        private readonly Dictionary<string, string> sourceSignatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> sourceDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly DateTime sessionStartUtc = DateTime.UtcNow;
        private StreamWriter journal;
        private volatile string status = "Подготовка журнала";
        private volatile string lastError;
        private bool disposed;
        private volatile bool recordingLimitReached;
        private volatile bool outputFailed;
        private long journalBytes;
        private long recordsWritten;
        private long recordsDropped;
        private long samples;
        private long processArrivals;
        private long processDepartures;
        private long lastHeartbeat = -10000;
        private long lastAppSnapshot = -30000;
        private long lastSummary = -10000;
        private long lastClockMonotonic = -1;
        private DateTime lastClockUtc;
        private TimeSpan lastOffset;
        private string lastStateSignature;
        private string lastProcessErrorSignature;
        private string lastSampleError;
        private bool processEnumerationComplete = true;

        internal Collector(string directory) : this(directory, new string[] { "Glukoboy", "Bubliklyuk" }) { }

        internal Collector(string directory, string[] names)
        {
            if (String.IsNullOrEmpty(directory)) throw new ArgumentException("Не указана папка отчёта.", "directory");
            if (names == null || names.Length == 0 || names.Length > 8) throw new ArgumentException("Нужны от 1 до 8 точных имён процессов.", "names");
            List<string> normalized = new List<string>();
            foreach (string raw in names)
            {
                string name = (raw ?? "").Trim();
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                if (name.Length == 0 || name.Length > 100 || name.IndexOfAny(new char[] { '\\', '/', ':', '*', '?' }) >= 0)
                    throw new ArgumentException("Требуется точное имя процесса без пути.", "names");
                if (!normalized.Exists(delegate(string item) { return String.Equals(item, name, StringComparison.OrdinalIgnoreCase); })) normalized.Add(name);
            }
            targetNames = normalized.ToArray();
            DirectoryPath = Path.GetFullPath(directory);
            if (DirectoryPath.StartsWith("\\\\", StringComparison.Ordinal))
                throw new IOException("Папка отчёта должна находиться на локальном диске.");
            DriveInfo destinationDrive = new DriveInfo(Path.GetPathRoot(DirectoryPath));
            if ((destinationDrive.DriveType != DriveType.Fixed && destinationDrive.DriveType != DriveType.Removable) || HasReparseAncestor(DirectoryPath))
                throw new IOException("Папка отчёта должна находиться на локальном диске без перенаправляющих ссылок.");
            Directory.CreateDirectory(DirectoryPath);
            serializer.MaxJsonLength = 2 * 1024 * 1024;
            Dictionary<string, object> manifest = new Dictionary<string, object>();
            manifest["format"] = "glukoboy-readonly-diagnostics-v1";
            manifest["started_utc"] = sessionStartUtc.ToString("o", CultureInfo.InvariantCulture);
            manifest["target_process_names"] = targetNames;
            manifest["os_version"] = Environment.OSVersion.ToString();
            manifest["runtime_version"] = Environment.Version.ToString();
            manifest["is_64bit_os"] = Environment.Is64BitOperatingSystem;
            manifest["is_64bit_collector"] = Environment.Is64BitProcess;
            manifest["timezone_id"] = TimeZoneInfo.Local.Id;
            manifest["timezone_offset_minutes_at_start"] = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes;
            manifest["stopwatch_frequency"] = Stopwatch.Frequency;
            manifest["expected_poll_ms"] = 500;
            manifest["heartbeat_ms"] = 10000;
            manifest["journal_max_bytes"] = MaximumJournalBytes;
            manifest["maximum_target_processes"] = MaximumProcesses;
            manifest["maximum_target_windows"] = NativeProbe.MaximumWindows;
            manifest["maximum_error_log_tail_bytes"] = MaximumAppLogBytes;
            manifest["privacy"] = "No screenshots, keyboard, clipboard, microphone, network, unrelated window titles, command lines, registry or full settings. Foreground process name/PID/HWND and desktop ID only. Target executable paths and bounded errors.log tails may contain user paths.";
            manifest["observation_limits"] = "Only current session top-level windows are enumerated. Polling may miss transitions shorter than its interval. A null probe means unknown, not false. Virtual desktop ID of foreground is a current-desktop hint, not an undocumented global desktop API. File settings may differ from settings already loaded in the game.";
            WriteNewFile("manifest.json", serializer.Serialize(manifest));
            FileStream stream = new FileStream(Path.Combine(DirectoryPath, "events.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096);
            journal = new StreamWriter(stream, utf8);
            journal.AutoFlush = true;
            WriteRecord("session_started", new Dictionary<string, object> { { "targets", targetNames } });
            lock (gate) status = "Сбор запущен; ожидание Глюкобоя";
            WriteSummary(false);
        }

        internal string DirectoryPath { get; private set; }
        // UI reads must never wait behind a slow filesystem operation. Writes
        // stay serialized, while volatile snapshots keep the stop/export UI live.
        internal string Status { get { return status; } }
        internal string LastError { get { return lastError; } }
        internal bool IsRecordingLimitReached { get { return recordingLimitReached; } }
        internal bool IsOutputFailed { get { return outputFailed; } }

        internal void Note(string kind, string message)
        {
            WriteRecord("note", new Dictionary<string, object> { { "kind", Limit(kind, 100) }, { "message", Limit(message, 2000) } });
        }

        internal void Sample()
        {
            lock (gate) { if (disposed || outputFailed || recordingLimitReached) return; }
            samples++;
            try
            {
                ObserveClock();
                List<int> observedPids;
                List<Dictionary<string, object>> processes = CaptureProcesses(out observedPids);
                Dictionary<string, object> state = new Dictionary<string, object>();
                state["processes"] = processes;
                state["native"] = native.Capture(observedPids);
                string signature = Serialize(state);
                bool changed = !String.Equals(signature, lastStateSignature, StringComparison.Ordinal);
                long elapsed = monotonic.ElapsedMilliseconds;
                bool heartbeat = elapsed - lastHeartbeat >= 10000;
                if (changed || heartbeat)
                {
                    state["reason"] = changed ? "state_changed" : "heartbeat";
                    WriteRecord("state", state);
                    lastStateSignature = signature;
                    if (heartbeat) lastHeartbeat = elapsed;
                }
                if (elapsed - lastAppSnapshot >= 30000)
                {
                    foreach (string path in new List<string>(sourceDirectories)) CaptureAppFiles(path, "periodic");
                    lastAppSnapshot = elapsed;
                }
                lastSampleError = null;
                lock (gate)
                {
                    if (!recordingLimitReached && !outputFailed)
                        status = !processEnumerationComplete ? "Сбор идёт; перечень процессов получен не полностью" : (observedPids.Count == 0 ? "Сбор идёт; процесс Глюкобоя сейчас не обнаружен" : "Сбор идёт; процессов Глюкобоя: " + observedPids.Count.ToString(CultureInfo.InvariantCulture));
                }
                if (elapsed - lastSummary >= 10000) { WriteSummary(false); lastSummary = elapsed; }
            }
            catch (Exception ex)
            {
                string error = NativeProbe.ErrorText(ex);
                if (error != lastSampleError) { WriteRecord("sample_failed", new Dictionary<string, object> { { "error", error } }); lastSampleError = error; }
                lock (gate) if (!recordingLimitReached && !outputFailed) status = "Сбор продолжается; ошибка отдельной проверки";
            }
        }

        private void ObserveClock()
        {
            DateTime utc = DateTime.UtcNow;
            TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(utc);
            long ticks = monotonic.ElapsedTicks;
            if (lastClockMonotonic >= 0)
            {
                double clockMs = (utc - lastClockUtc).TotalMilliseconds;
                double elapsedMs = (ticks - lastClockMonotonic) * 1000.0 / Stopwatch.Frequency;
                if (Math.Abs(clockMs - elapsedMs) >= 2000 || offset != lastOffset)
                    WriteRecord("clock_discontinuity", new Dictionary<string, object> { { "utc_elapsed_ms", clockMs }, { "monotonic_elapsed_ms", elapsedMs }, { "previous_utc", lastClockUtc.ToString("o", CultureInfo.InvariantCulture) }, { "old_offset_minutes", lastOffset.TotalMinutes }, { "new_offset_minutes", offset.TotalMinutes } });
            }
            lastClockUtc = utc;
            lastClockMonotonic = ticks;
            lastOffset = offset;
        }

        private List<Dictionary<string, object>> CaptureProcesses(out List<int> observedPids)
        {
            observedPids = new List<int>();
            Dictionary<int, ProcessObservation> observed = new Dictionary<int, ProcessObservation>();
            HashSet<string> completeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<Dictionary<string, object>> errors = new List<Dictionary<string, object>>();
            foreach (string name in targetNames)
            {
                Process[] found = null;
                bool complete = true;
                try
                {
                    found = Process.GetProcessesByName(name);
                    foreach (Process process in found)
                    {
                        try
                        {
                            if (observed.Count >= MaximumProcesses) { complete = false; continue; }
                            int pid = process.Id;
                            string actualName = process.ProcessName;
                            if (!String.Equals(actualName, name, StringComparison.OrdinalIgnoreCase)) { complete = false; continue; }
                            ProcessObservation item = new ProcessObservation();
                            item.Pid = pid;
                            item.Name = actualName;
                            try { item.StartUtc = process.StartTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture); }
                            catch (Exception ex) { item.StartError = NativeProbe.ErrorText(ex); }
                            observed[pid] = item;
                        }
                        catch (Exception ex) { complete = false; errors.Add(new Dictionary<string, object> { { "name", name }, { "error", NativeProbe.ErrorText(ex) } }); }
                    }
                    if (!complete) errors.Add(new Dictionary<string, object> { { "name", name }, { "error", "Enumeration incomplete: process metadata race/access failure or process limit." } });
                    if (complete) completeNames.Add(name);
                }
                catch (Exception ex) { errors.Add(new Dictionary<string, object> { { "name", name }, { "error", NativeProbe.ErrorText(ex) } }); }
                finally { if (found != null) foreach (Process process in found) process.Dispose(); }
            }
            string errorSignature = Serialize(errors);
            processEnumerationComplete = errors.Count == 0;
            if (errors.Count > 0 && errorSignature != lastProcessErrorSignature)
                WriteRecord("process_enumeration_incomplete", new Dictionary<string, object> { { "errors", errors }, { "absence_is_not_exit", true } });
            if (errors.Count == 0 && lastProcessErrorSignature != null && lastProcessErrorSignature != "[]")
                WriteRecord("process_enumeration_recovered", new Dictionary<string, object>());
            lastProcessErrorSignature = errorSignature;
            foreach (int pid in new List<int>(tracked.Keys))
            {
                TrackedProcess old = tracked[pid];
                ProcessObservation present;
                bool exists = observed.TryGetValue(pid, out present);
                bool reused = exists && (!String.Equals(old.Name, present.Name, StringComparison.OrdinalIgnoreCase) || (old.StartUtc != null && present.StartUtc != null && old.StartUtc != present.StartUtc));
                if (reused || (!exists && completeNames.Contains(old.Name)))
                {
                    WriteRecord("process_disappeared", new Dictionary<string, object> { { "pid", pid }, { "name", old.Name }, { "start_utc", old.StartUtc }, { "reason", reused ? "pid_reused" : "absent_from_successful_enumeration" } });
                    processDepartures++;
                    if (old.Directory != null) CaptureAppFiles(old.Directory, "process_disappeared");
                    tracked.Remove(pid);
                }
            }
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            List<int> sorted = new List<int>(observed.Keys);
            sorted.Sort();
            foreach (int pid in sorted)
            {
                ProcessObservation current = observed[pid];
                observedPids.Add(pid);
                TrackedProcess item;
                if (!tracked.TryGetValue(pid, out item))
                {
                    if (tracked.Count >= MaximumProcesses)
                    {
                        result.Add(new Dictionary<string, object> { { "pid", pid }, { "name", current.Name }, { "metadata_status", "tracking_limit_reached" } });
                        continue;
                    }
                    item = new TrackedProcess();
                    item.Pid = pid;
                    item.Name = current.Name;
                    item.StartUtc = current.StartUtc;
                    tracked.Add(pid, item);
                    CaptureMetadata(item);
                    WriteRecord("process_discovered", ProcessData(item, current));
                    processArrivals++;
                    if (item.Directory != null) CaptureAppFiles(item.Directory, "process_discovered");
                }
                else
                {
                    if (item.StartUtc == null) item.StartUtc = current.StartUtc;
                    if (item.ExecutablePath == null && monotonic.ElapsedMilliseconds - item.LastMetadataAttempt >= 10000) CaptureMetadata(item);
                }
                result.Add(ProcessData(item, current));
            }
            // Previous entries are retained internally when enumeration failed. The
            // snapshot lists only this sample's positively matched target processes.
            if (errors.Count > 0) result.Add(new Dictionary<string, object> { { "enumeration_incomplete", true }, { "errors", errors } });
            return result;
        }

        private void CaptureMetadata(TrackedProcess item)
        {
            item.LastMetadataAttempt = monotonic.ElapsedMilliseconds;
            item.MetadataError = null;
            try
            {
                using (Process process = Process.GetProcessById(item.Pid))
                {
                    if (!String.Equals(process.ProcessName, item.Name, StringComparison.OrdinalIgnoreCase)) { item.MetadataError = "pid_changed_before_metadata"; return; }
                    string actualStart = process.StartTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                    if (item.StartUtc != null && actualStart != item.StartUtc) { item.MetadataError = "pid_reused_before_metadata"; return; }
                    item.StartUtc = actualStart;
                    item.ExecutablePath = process.MainModule.FileName;
                    string source = Path.GetDirectoryName(item.ExecutablePath);
                    // File reads are limited to local fixed/removable disks, never UNC or mapped network drives.
                    string root = Path.GetPathRoot(source);
                    if (source.StartsWith("\\\\", StringComparison.Ordinal))
                    { item.MetadataError = "version_and_app_files_skipped_nonlocal_volume"; return; }
                    DriveInfo drive = new DriveInfo(root);
                    if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)
                    { item.MetadataError = "version_and_app_files_skipped_nonlocal_volume"; return; }
                    if (HasReparseAncestor(item.ExecutablePath))
                    { item.MetadataError = "version_and_app_files_skipped_reparse_point"; return; }
                    if (sourceDirectories.Contains(source) || sourceDirectories.Count < MaximumSourceDirectories)
                    { item.Directory = source; sourceDirectories.Add(source); }
                    else item.MetadataError = "app_files_source_directory_limit";
                    FileVersionInfo version = FileVersionInfo.GetVersionInfo(item.ExecutablePath);
                    item.Version = Limit(version.FileVersion, 100);
                    item.ProductVersion = Limit(version.ProductVersion, 100);
                }
            }
            catch (Exception ex) { item.MetadataError = NativeProbe.ErrorText(ex); }
        }

        private static Dictionary<string, object> ProcessData(TrackedProcess item, ProcessObservation current)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["pid"] = item.Pid;
            result["name"] = item.Name;
            result["start_utc"] = item.StartUtc;
            result["start_read_error"] = current.StartError;
            result["executable_path"] = item.ExecutablePath;
            result["file_version"] = item.Version;
            result["product_version"] = item.ProductVersion;
            result["metadata_error"] = item.MetadataError;
            return result;
        }

        private void CaptureAppFiles(string directory, string reason)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data["source_directory"] = directory;
            data["errors_log"] = ReadBoundedFile(Path.Combine(directory, "errors.log"), MaximumAppLogBytes, true);
            Dictionary<string, object> settingsFile = ReadBoundedFile(Path.Combine(directory, "settings.ini"), MaximumSettingsBytes, false);
            object content;
            if (settingsFile.TryGetValue("text", out content))
            {
                Dictionary<string, object> selected = SelectSettings((string)content);
                settingsFile.Remove("text");
                settingsFile["allowlisted_values"] = selected;
                settingsFile["semantics"] = "Values on disk, not proof of currently loaded settings. Defaults are not inferred.";
            }
            data["schedule_settings"] = settingsFile;
            string signature = Hash(Serialize(data));
            string previous;
            if (!sourceSignatures.TryGetValue(directory, out previous) || previous != signature)
            {
                sourceSignatures[directory] = signature;
                data["reason"] = reason;
                WriteRecord("app_files_snapshot", data);
            }
        }

        private Dictionary<string, object> ReadBoundedFile(string path, int maximum, bool tail)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["file"] = Path.GetFileName(path);
            try
            {
                if (path.StartsWith("\\\\", StringComparison.Ordinal)) { result["status"] = "skipped_nonlocal_volume"; return result; }
                DriveInfo drive = new DriveInfo(Path.GetPathRoot(path));
                if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)
                { result["status"] = "skipped_nonlocal_volume"; return result; }
                if (HasReparseAncestor(path)) { result["status"] = "skipped_reparse_point"; return result; }
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long length = stream.Length;
                    int count = (int)Math.Min(length, maximum);
                    long offset = tail ? Math.Max(0, length - count) : 0;
                    stream.Seek(offset, SeekOrigin.Begin);
                    byte[] buffer = new byte[count];
                    int read = 0;
                    while (read < count)
                    {
                        int got = stream.Read(buffer, read, count - read);
                        if (got == 0) break;
                        read += got;
                    }
                    int start = 0;
                    if (offset > 0) while (start < read && (buffer[start] & 0xC0) == 0x80) start++;
                    string text = utf8.GetString(buffer, start, read - start);
                    if (!tail && length > count)
                    {
                        int newline = text.LastIndexOf('\n');
                        text = newline >= 0 ? text.Substring(0, newline + 1) : "";
                    }
                    result["status"] = "read";
                    result["size_at_open"] = length;
                    result["offset"] = offset;
                    result["bytes_read"] = read;
                    result["truncated"] = length > maximum;
                    result["text"] = text;
                }
            }
            catch (FileNotFoundException) { result["status"] = "missing"; }
            catch (DirectoryNotFoundException) { result["status"] = "missing_directory"; }
            catch (Exception ex) { result["status"] = "unknown"; result["error"] = NativeProbe.ErrorText(ex); }
            return result;
        }

        private static bool HasReparseAncestor(string path)
        {
            // Walk from root toward the file: checking the leaf first would
            // already traverse a junction and could contact a remote target.
            string full = Path.GetFullPath(path);
            string check = Path.GetPathRoot(full);
            if (String.IsNullOrEmpty(check) || check.StartsWith("\\\\", StringComparison.Ordinal)) return true;
            if ((File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0) return true;
            string[] components = full.Substring(check.Length).Split(new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string component in components)
            {
                check = Path.Combine(check, component);
                try { if ((File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0) return true; }
                catch (FileNotFoundException) { return false; }
                catch (DirectoryNotFoundException) { return false; }
            }
            return false;
        }

        private static Dictionary<string, object> SelectSettings(string text)
        {
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> times = new HashSet<string>(new string[] { "NightFrom", "NightTo", "FreeFrom", "FreeTo", "SchoolFrom", "SchoolTo" }, StringComparer.OrdinalIgnoreCase);
            HashSet<string> numbers = new HashSet<string>(new string[] { "IntervalMin", "IntervalMax", "IdleLeave", "TestMode", "AnimationLevel" }, StringComparer.OrdinalIgnoreCase);
            using (StringReader reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim().TrimStart('\uFEFF');
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[') continue;
                    int at = line.IndexOf('=');
                    if (at < 1) continue;
                    string key = line.Substring(0, at).Trim();
                    if (!times.Contains(key) && !numbers.Contains(key) && !key.Equals("SchoolDays", StringComparison.OrdinalIgnoreCase) && !key.Equals("NightModes", StringComparison.OrdinalIgnoreCase)) continue;
                    string value = line.Substring(at + 1);
                    int comment = value.IndexOfAny(new char[] { '#', ';' });
                    if (comment >= 0) value = value.Substring(0, comment);
                    value = value.Trim();
                    bool valid = false;
                    if (times.Contains(key)) valid = Regex.IsMatch(value, @"^\d{1,2}:\d{2}$", RegexOptions.CultureInvariant);
                    else if (numbers.Contains(key)) valid = Regex.IsMatch(value, @"^-?\d{1,6}$", RegexOptions.CultureInvariant);
                    else if (key.Equals("SchoolDays", StringComparison.OrdinalIgnoreCase)) valid = Regex.IsMatch(value, @"^[1-7]{0,14}$", RegexOptions.CultureInvariant);
                    else if (value.Length <= 100) valid = Regex.IsMatch(value, @"^(overheat|repair|cleanup|update)(,(overheat|repair|cleanup|update))*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    // Even allowlisted keys cannot carry arbitrary personal data in this report.
                    result[key] = valid ? value : "[invalid value omitted]";
                }
            }
            return result;
        }

        private void WriteRecord(string kind, Dictionary<string, object> data)
        {
            lock (gate)
            {
                if (disposed) return;
                if (recordingLimitReached || outputFailed) { recordsDropped++; return; }
                try
                {
                    Dictionary<string, object> entry = new Dictionary<string, object>();
                    entry["sequence"] = recordsWritten + 1;
                    entry["utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                    entry["local"] = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);
                    entry["monotonic_ms"] = monotonic.ElapsedMilliseconds;
                    entry["monotonic_ticks"] = monotonic.ElapsedTicks;
                    entry["event"] = kind;
                    entry["data"] = data;
                    string line = serializer.Serialize(entry);
                    int bytes = utf8.GetByteCount(line) + 2;
                    if (journalBytes + bytes > MaximumJournalBytes - 4096)
                    {
                        recordingLimitReached = true;
                        recordsDropped++;
                        status = "Сбор остановлен: достигнут предел отчёта 20 МБ";
                        lastError = "Достигнут предел размера. Новые события не записываются; сохраните отчёт и начните новый сбор.";
                        entry["event"] = "recording_size_limit_reached";
                        entry["data"] = new Dictionary<string, object> { { "maximum_journal_bytes", MaximumJournalBytes }, { "message", lastError } };
                        line = serializer.Serialize(entry);
                        bytes = utf8.GetByteCount(line) + 2;
                    }
                    journal.WriteLine(line);
                    journalBytes += bytes;
                    recordsWritten++;
                    if (recordingLimitReached) WriteSummary(false);
                }
                catch (Exception ex)
                {
                    outputFailed = true;
                    lastError = "Не удаётся записывать отчёт: " + NativeProbe.ErrorText(ex);
                    status = "Сбор остановлен: ошибка записи отчёта";
                }
            }
        }

        private string Serialize(object value) { lock (gate) return serializer.Serialize(value); }

        private void WriteSummary(bool ended)
        {
            lock (gate)
            {
                try
                {
                    Dictionary<string, object> summary = new Dictionary<string, object>();
                    summary["started_utc"] = sessionStartUtc.ToString("o", CultureInfo.InvariantCulture);
                    summary["updated_utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                    summary["elapsed_ms"] = monotonic.ElapsedMilliseconds;
                    summary["ended_cleanly"] = ended;
                    summary["status"] = status;
                    summary["last_error"] = lastError;
                    summary["sample_count"] = samples;
                    summary["journal_bytes"] = journalBytes;
                    summary["records_written"] = recordsWritten;
                    summary["records_dropped"] = recordsDropped;
                    summary["processes_discovered"] = processArrivals;
                    summary["processes_disappeared"] = processDepartures;
                    summary["recording_limit_reached"] = recordingLimitReached;
                    summary["output_failed"] = outputFailed;
                    byte[] data = utf8.GetBytes(serializer.Serialize(summary));
                    if (data.Length > 16384) return;
                    using (FileStream stream = new FileStream(Path.Combine(DirectoryPath, "summary.json"), FileMode.Create, FileAccess.Write, FileShare.Read))
                    { stream.Write(data, 0, data.Length); stream.Flush(); }
                }
                catch (Exception ex)
                {
                    lastError = "Не удаётся обновить итог отчёта: " + NativeProbe.ErrorText(ex);
                    if (!recordingLimitReached && !outputFailed) status = "Журнал пишется; ошибка итогового файла";
                }
            }
        }

        private void WriteNewFile(string file, string text)
        {
            byte[] data = utf8.GetBytes(text);
            if (data.Length > 16384) throw new InvalidOperationException("Слишком большой манифест отчёта.");
            using (FileStream stream = new FileStream(Path.Combine(DirectoryPath, file), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            { stream.Write(data, 0, data.Length); stream.Flush(); }
        }

        private static string Hash(string text)
        {
            using (SHA256 hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }
        private static string Limit(string text, int count) { return text == null ? "" : (text.Length <= count ? text : text.Substring(0, count)); }

        public void Dispose()
        {
            lock (gate) { if (disposed) return; }
            foreach (string directory in new List<string>(sourceDirectories)) CaptureAppFiles(directory, "session_stopping");
            WriteRecord("session_stopped", new Dictionary<string, object> { { "sample_count", samples } });
            lock (gate)
            {
                if (!outputFailed && !recordingLimitReached) status = "Сбор завершён";
                WriteSummary(true);
                disposed = true;
                if (journal != null) { try { journal.Dispose(); } catch { } journal = null; }
            }
            native.Dispose();
        }

        private sealed class ProcessObservation
        {
            internal int Pid;
            internal string Name, StartUtc, StartError;
        }
        private sealed class TrackedProcess
        {
            internal int Pid;
            internal string Name, StartUtc, ExecutablePath, Directory, Version, ProductVersion, MetadataError;
            internal long LastMetadataAttempt;
        }
    }
}
