using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Bubliklyuk
{
    // Сеть используется только для метаданных релиза, контрольной суммы и exe.
    internal sealed class Updater : IDisposable
    {
        internal const string Owner = "Cinicmanager";
        internal const string Repository = "glukoboy";
        internal const string ExeName = "Glukoboy.exe";
        private const int TimeoutMilliseconds = 15000;
        private const int MaximumExeBytes = 100 * 1024 * 1024;
        private readonly Control dispatcher;
        private readonly Action<string> notify;
        private readonly Func<bool> mayApply;
        private readonly Action exitApplication;
        private readonly string directory;
        private readonly string executable;
        private readonly string download;
        private readonly string backup;
        private readonly object stateLock = new object();
        private System.Threading.Timer timer;
        private int busy;
        private int stopped;
        private bool pending;
        private bool pendingManual;
        private string pendingHash;
        private Version pendingVersion;

        internal Updater(Control dispatcher, Action<string> notify,
            Func<bool> mayApply, Action exitApplication, bool automatic)
        {
            this.dispatcher = dispatcher;
            this.notify = notify;
            this.mayApply = mayApply;
            this.exitApplication = exitApplication;
            directory = AppDomain.CurrentDomain.BaseDirectory;
            executable = Path.Combine(directory, ExeName);
            download = executable + ".new";
            backup = executable + ".old";
            if (automatic)
                timer = new System.Threading.Timer(OnTimer, null, 60000, 6 * 60 * 60 * 1000);
        }

        internal static Version CurrentVersion
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version; }
        }

        internal static void CleanupOld()
        {
            try
            {
                // Удаляем только резервную копию нашей программы.
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ExeName + ".old");
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception error) { Log("Очистка резервной копии", error); }
        }

        private void OnTimer(object unused) { CheckNow(false); }

        internal void CheckNow(bool manual)
        {
            if (Interlocked.CompareExchange(ref stopped, 0, 0) != 0) return;
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                if (manual) PostNotice("Глюкобой: проверка обновления уже идёт.");
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate(object unused)
            {
                try
                {
                    bool waiting;
                    lock (stateLock) waiting = pending;
                    if (waiting)
                    {
                        if (manual) { lock (stateLock) pendingManual = true; }
                        if (manual) PostNotice("Обновление готово. Будет установлено после атаки.");
                        Post(TryApplyPending);
                        return;
                    }
                    string message = DownloadLatest(manual);
                    if (manual && message.Length != 0) PostNotice(message);
                    Post(TryApplyPending);
                }
                catch (Exception error)
                {
                    Log("Проверка обновления", error);
                    // Не трогаем готовый файл другого, уже завершённого запроса.
                    bool ready;
                    lock (stateLock) ready = pending;
                    if (!ready) DeleteDownload();
                    if (manual) PostNotice("Обновление проверить не удалось. Глюкобой продолжает работать.");
                }
                finally { Interlocked.Exchange(ref busy, 0); }
            });
        }

        private string DownloadLatest(bool manual)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            string url = "https://api.github.com/repos/" + Owner + "/" + Repository + "/releases/latest";
            string json = GetText(url, 2 * 1024 * 1024);
            string tag = JsonProperty(json, "tag_name");
            Match versionMatch = Regex.Match(tag, @"\Av([0-9]+)\.([0-9]+)\.([0-9]+)\z",
                RegexOptions.CultureInvariant);
            if (!versionMatch.Success) throw new InvalidDataException("Некорректный tag_name.");
            Version version = new Version(
                Int32.Parse(versionMatch.Groups[1].Value, CultureInfo.InvariantCulture),
                Int32.Parse(versionMatch.Groups[2].Value, CultureInfo.InvariantCulture),
                Int32.Parse(versionMatch.Groups[3].Value, CultureInfo.InvariantCulture), 0);
            if (version.CompareTo(CurrentVersion) <= 0)
                return "Глюкобой " + CurrentVersion.ToString(3) + ": установлена актуальная версия.";

            string assets = JsonArray(json, "assets");
            string exeUrl = null;
            string hashUrl = null;
            // Балансирующая группа выделяет объект ассета вместе с вложенным uploader.
            MatchCollection objects = Regex.Matches(assets,
                @"\{(?>[^{}""]+|""(?:\\.|[^""\\])*""|\{(?<D>)|\}(?<-D>))*(?(D)(?!))\}",
                RegexOptions.CultureInvariant);
            foreach (Match item in objects)
            {
                string name = JsonProperty(item.Value, "name");
                if (name == ExeName) exeUrl = JsonProperty(item.Value, "browser_download_url");
                if (name == ExeName + ".sha256")
                    hashUrl = JsonProperty(item.Value, "browser_download_url");
            }
            if (exeUrl == null || hashUrl == null)
                throw new InvalidDataException("В релизе нет exe или sha256.");
            ValidateAssetUrl(exeUrl);
            ValidateAssetUrl(hashUrl);
            string checksum = GetText(hashUrl, 4096).TrimStart('\uFEFF');
            Match hashMatch = Regex.Match(checksum,
                @"\A\s*([a-fA-F0-9]{64})(?:[ \t]+[^\r\n]*)?\s*\z",
                RegexOptions.CultureInvariant);
            if (!hashMatch.Success) throw new InvalidDataException("Некорректный файл sha256.");
            string hash = hashMatch.Groups[1].Value.ToLowerInvariant();
            string ownPath = Assembly.GetExecutingAssembly().Location;
            if (!String.Equals(Path.GetFullPath(ownPath), Path.GetFullPath(executable),
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Для обновления exe должен называться Glukoboy.exe.");

            DownloadExe(exeUrl);
            ValidateDownloadedFile(download, hash);
            if (Interlocked.CompareExchange(ref stopped, 0, 0) != 0)
            {
                DeleteDownload();
                return "";
            }
            lock (stateLock)
            {
                pendingHash = hash;
                pendingVersion = version;
                pendingManual = manual;
                pending = true;
            }
            return "Глюкобой " + version.ToString(3) +
                " скачан. Установка начнётся после завершения атаки.";
        }

        private static HttpWebRequest Request(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "Glukoboy-Updater";
            request.Accept = "application/vnd.github+json";
            request.Timeout = TimeoutMilliseconds;
            request.ReadWriteTimeout = TimeoutMilliseconds;
            request.AllowAutoRedirect = true;
            request.MaximumAutomaticRedirections = 5;
            request.KeepAlive = false;
            return request;
        }

        private static string GetText(string url, int limit)
        {
            HttpWebRequest request = Request(url);
            using (System.Threading.Timer deadline = new System.Threading.Timer(
                delegate(object unused) { try { request.Abort(); } catch { } },
                null, TimeoutMilliseconds, Timeout.Infinite))
            using (WebResponse response = request.GetResponse())
            using (Stream input = response.GetResponseStream())
            using (MemoryStream output = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + count > limit)
                        throw new InvalidDataException("Ответ сервера слишком большой.");
                    output.Write(buffer, 0, count);
                }
                return new UTF8Encoding(false, true).GetString(output.ToArray());
            }
        }

        private void DownloadExe(string url)
        {
            HttpWebRequest request = Request(url);
            using (System.Threading.Timer deadline = new System.Threading.Timer(
                delegate(object unused) { try { request.Abort(); } catch { } },
                null, TimeoutMilliseconds, Timeout.Infinite))
            using (WebResponse response = request.GetResponse())
            using (Stream input = response.GetResponseStream())
            using (FileStream output = new FileStream(download, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[32768];
                int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (Interlocked.CompareExchange(ref stopped, 0, 0) != 0)
                        throw new OperationCanceledException();
                    if (output.Length + count > MaximumExeBytes)
                        throw new InvalidDataException("Слишком большой exe.");
                    output.Write(buffer, 0, count);
                }
                output.Flush();
            }
        }

        private static void ValidateDownloadedFile(string path, string expectedHash)
        {
            using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length < 2 || input.ReadByte() != 'M' || input.ReadByte() != 'Z')
                    throw new InvalidDataException("У файла нет заголовка MZ.");
                input.Position = 0;
                using (SHA256 sha = SHA256.Create())
                {
                    string actual = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                    if (!String.Equals(actual, expectedHash, StringComparison.Ordinal))
                        throw new InvalidDataException("SHA-256 не совпал.");
                }
            }
        }

        // Вызывается в UI-потоке после проверки, а затем из фонового heartbeat приложения.
        internal void TryApplyPending()
        {
            if (Interlocked.CompareExchange(ref stopped, 0, 0) != 0) return;
            string hash;
            Version version;
            bool notifyFailure;
            lock (stateLock)
            {
                if (!pending) return;
                hash = pendingHash;
                version = pendingVersion;
                notifyFailure = pendingManual;
            }
            if (!mayApply()) return;
            bool movedOld = false;
            bool installedNew = false;
            bool launched = false;
            try
            {
                ValidateDownloadedFile(download, hash);
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(executable, backup);
                movedOld = true;
                File.Move(download, executable);
                installedNew = true;
                ProcessStartInfo start = new ProcessStartInfo(executable, "--updated");
                start.UseShellExecute = false;
                start.WorkingDirectory = directory;
                using (Process process = Process.Start(start))
                {
                    if (process == null) throw new IOException("Новый процесс не запущен.");
                    if (process.WaitForExit(250))
                        throw new IOException("Новый процесс завершился сразу после запуска.");
                }
                launched = true;
                lock (stateLock) pending = false;
                Log("Установлена версия " + version.ToString(3), null);
                // Новый процесс ждёт освобождения мьютекса, старый сначала возвращает звук.
                exitApplication();
            }
            catch (Exception error)
            {
                Log("Применение обновления", error);
                if (!launched)
                {
                    try
                    {
                        if (installedNew && File.Exists(executable))
                            File.Move(executable, download);
                        if (movedOld && File.Exists(backup))
                            File.Move(backup, executable);
                    }
                    catch (Exception rollbackError) { Log("Откат обновления", rollbackError); }
                    lock (stateLock) pending = false;
                    DeleteDownload();
                    if (notifyFailure)
                        PostNotice("Обновление отложено: не удалось заменить или запустить exe.");
                }
            }
        }

        private static void ValidateAssetUrl(string text)
        {
            Uri uri;
            if (!Uri.TryCreate(text, UriKind.Absolute, out uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !String.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                !uri.AbsolutePath.StartsWith("/" + Owner + "/" + Repository + "/releases/download/",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Недопустимый адрес ассета.");
        }

        private static string JsonProperty(string json, string name)
        {
            Match match = Regex.Match(json, "\"" + Regex.Escape(name) +
                "\"\\s*:\\s*\"(?<v>(?:\\\\.|[^\"\\\\])*)\"",
                RegexOptions.CultureInvariant);
            if (!match.Success) return "";
            return Regex.Replace(match.Groups["v"].Value, @"\\(?:u[0-9a-fA-F]{4}|[""\\/bfnrt])",
                delegate(Match escape)
                {
                    string value = escape.Value;
                    if (value[1] == 'u')
                        return ((char)Int32.Parse(value.Substring(2), NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture)).ToString();
                    switch (value[1])
                    {
                        case 'b': return "\b";
                        case 'f': return "\f";
                        case 'n': return "\n";
                        case 'r': return "\r";
                        case 't': return "\t";
                        default: return value.Substring(1);
                    }
                });
        }

        private static string JsonArray(string json, string property)
        {
            Match match = Regex.Match(json, "\"" + Regex.Escape(property) + "\"\\s*:\\s*\\[",
                RegexOptions.CultureInvariant);
            if (!match.Success) throw new InvalidDataException("Нет списка assets.");
            int start = match.Index + match.Length;
            int depth = 1;
            bool quoted = false;
            bool escaped = false;
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                    continue;
                }
                if (c == '"') quoted = true;
                else if (c == '[') depth++;
                else if (c == ']' && --depth == 0) return json.Substring(start, i - start);
            }
            throw new InvalidDataException("Незаконченный список assets.");
        }

        private void PostNotice(string text) { Post(delegate { notify(text); }); }

        private void Post(Action action)
        {
            if (Interlocked.CompareExchange(ref stopped, 0, 0) != 0) return;
            try
            {
                if (!dispatcher.IsDisposed && dispatcher.IsHandleCreated)
                    dispatcher.BeginInvoke((MethodInvoker)delegate
                    {
                        if (Interlocked.CompareExchange(ref stopped, 0, 0) == 0)
                        {
                            try { action(); }
                            catch (Exception error) { Log("UI обновления", error); }
                        }
                    });
            }
            catch (Exception error) { Log("Передача результата", error); }
        }

        private void DeleteDownload()
        {
            try { if (File.Exists(download)) File.Delete(download); }
            catch (Exception error) { Log("Очистка загрузки", error); }
        }

        private static readonly object LogLock = new object();
        private static void Log(string operation, Exception error)
        {
            try
            {
                lock (LogLock)
                {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update.log");
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                        " " + operation;
                    if (error != null) line += ": " + error.GetType().Name + " " + error.Message;
                    line = line.Replace("\r", " ").Replace("\n", " ");
                    if (line.Length > 2000) line = line.Substring(0, 2000);
                    line += "\r\n";
                    UTF8Encoding encoding = new UTF8Encoding(false);
                    byte[] tail = encoding.GetBytes(line);
                    if (File.Exists(path) && new FileInfo(path).Length + tail.Length > 50 * 1024)
                    {
                        byte[] old;
                        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite))
                        {
                            int length = (int)Math.Min(32 * 1024, stream.Length);
                            stream.Seek(-length, SeekOrigin.End);
                            old = new byte[length];
                            int read = 0;
                            while (read < length)
                            {
                                int count = stream.Read(old, read, length - read);
                                if (count == 0) break;
                                read += count;
                            }
                        }
                        int start = 0;
                        while (start < old.Length && old[start] != (byte)'\n') start++;
                        string kept = start < old.Length
                            ? encoding.GetString(old, start + 1, old.Length - start - 1) : "";
                        File.WriteAllText(path, kept + line, encoding);
                    }
                    else File.AppendAllText(path, line, encoding);
                }
            }
            catch { }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref stopped, 1);
            System.Threading.Timer old = Interlocked.Exchange(ref timer, null);
            if (old != null) old.Dispose();
        }
    }
}
