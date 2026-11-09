using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Bubliklyuk
{
    internal static class Diag
    {
        private const int MaximumBytes = 100 * 1024;
        private const int RetainedBytes = 50 * 1024;
        private static readonly object Gate = new object();

        internal static void Log(string where, Exception ex)
        {
            try { Write(where, ex == null ? "" : ex.ToString()); }
            catch { }
        }

        internal static void Info(string text)
        {
            try { Write("info", text); }
            catch { }
        }

        private static void Write(string where, string detail)
        {
            try
            {
                lock (Gate)
                {
                    UTF8Encoding encoding = new UTF8Encoding(false);
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "errors.log");
                    // Сохраняем весь ex.ToString(), включая стек, в одной физической строке.
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                        " | " + OneLine(where) + " | " + OneLine(detail) + "\r\n";
                    byte[] entry = encoding.GetBytes(line);
                    // Даже необычно длинное исключение не может превысить лимит файла.
                    if (entry.Length > RetainedBytes)
                    {
                        int start = entry.Length - RetainedBytes;
                        while (start < entry.Length && (entry[start] & 0xC0) == 0x80) start++;
                        byte[] shortened = encoding.GetBytes(
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                            " | " + OneLine(where).Substring(0, Math.Min(OneLine(where).Length, 128)) +
                            " | [начало длинной записи обрезано] " +
                            encoding.GetString(entry, start, entry.Length - start));
                        entry = shortened;
                    }
                    long existing = File.Exists(path) ? new FileInfo(path).Length : 0;
                    if (existing + entry.Length > MaximumBytes)
                    {
                        byte[] retained = new byte[0];
                        if (existing > 0)
                        {
                            using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                int length = (int)Math.Min(RetainedBytes, input.Length);
                                bool partial = input.Length > length;
                                input.Seek(-length, SeekOrigin.End);
                                byte[] buffer = new byte[length];
                                int read = 0;
                                while (read < buffer.Length)
                                {
                                    int count = input.Read(buffer, read, buffer.Length - read);
                                    if (count == 0) break;
                                    read += count;
                                }
                                int start = 0;
                                if (partial)
                                {
                                    while (start < read && buffer[start] != (byte)'\n') start++;
                                    if (start < read) start++;
                                }
                                if (read > start)
                                {
                                    retained = new byte[read - start];
                                    Buffer.BlockCopy(buffer, start, retained, 0, retained.Length);
                                }
                            }
                        }
                        // При большой новой записи оставляем столько хвоста, сколько помещается.
                        int retainStart = 0;
                        while (retained.Length - retainStart + entry.Length > MaximumBytes &&
                            retainStart < retained.Length)
                        {
                            while (retainStart < retained.Length && retained[retainStart] != (byte)'\n') retainStart++;
                            if (retainStart < retained.Length) retainStart++;
                        }
                        using (FileStream output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                        {
                            output.Write(retained, retainStart, retained.Length - retainStart);
                            output.Write(entry, 0, entry.Length);
                        }
                    }
                    else
                    {
                        using (FileStream output = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                            output.Write(entry, 0, entry.Length);
                    }
                }
            }
            catch { }
        }

        private static string OneLine(string value)
        {
            return (value ?? "").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
