using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace GlukoboyDiagnostics
{
    internal static class ReportPackage
    {
        private const int MaximumEntries = 100;
        private const long MaximumBytes = 32L * 1024L * 1024L;
        private const long MetadataReserveBytes = 64L * 1024L;

        internal static string Create(string reportDirectory, string outputDirectory, bool partial)
        {
            return CreateCore(reportDirectory, outputDirectory, partial, null);
        }

        internal static string CreateBackground(string captureRoot, string outputDirectory, bool partial)
        {
            return CreateCore(captureRoot, outputDirectory, partial, new BackgroundCaptureStore(captureRoot));
        }

        private static string CreateCore(string reportDirectory, string outputDirectory, bool partial, BackgroundCaptureStore automatic)
        {
            string sourceRoot = NormalizeDirectory(reportDirectory);
            string outputRoot = NormalizeDirectory(outputDirectory);
            RequireLocalDrive(sourceRoot);
            RequireLocalDrive(outputRoot);
            if (IsSameOrChild(outputRoot, sourceRoot)) throw new IOException("ZIP должен сохраняться вне папки собранных данных.");
            RejectReparseAncestors(sourceRoot);
            RejectReparseAncestors(outputRoot);
            RejectKnownSyncDestination(outputRoot);
            if (!Directory.Exists(sourceRoot)) throw new DirectoryNotFoundException("Папка собранных данных не найдена.");
            Directory.CreateDirectory(outputRoot);

            string name = "Bubliklyuk-report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".zip";
            string destination = Path.Combine(outputRoot, name);
            string temporary = Path.Combine(outputRoot, "." + name + "." + Guid.NewGuid().ToString("N") + ".tmp");
            bool ownTemporary = false;
            int included = 0;
            int skipped = 0;
            long copiedBytes = 0;
            long maximumSourceBytes = automatic == null ? MaximumBytes - MetadataReserveBytes : BackgroundCaptureStore.MaximumSessions * BackgroundCaptureStore.MaximumSessionBytes;
            List<string> warnings = new List<string>();
            if (partial) warnings.Add("Сборщик не завершил все проверки либо сообщил об ошибке. Сохранён доступный снимок файлов.");
            List<SourceFile> sources = new List<SourceFile>();
            int sessionCount = 0;
            if (automatic == null)
            {
                foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.TopDirectoryOnly))
                    sources.Add(new SourceFile(file, Path.GetFileName(file)));
            }
            else
            {
                bool ignored;
                List<string> sessions = automatic.GetSessions(out ignored);
                if (ignored || sessions.Count > BackgroundCaptureStore.MaximumSessions)
                {
                    partial = true;
                    warnings.Add("Часть папок пропущена: неподтверждённое происхождение, неизвестное содержимое, ссылки либо предел восьми сессий.");
                }
                foreach (string session in sessions)
                {
                    if (sessionCount >= BackgroundCaptureStore.MaximumSessions) break;
                    sessionCount++;
                    if (!automatic.IsComplete(session))
                    {
                        partial = true;
                        warnings.Add(Path.GetFileName(session) + ": нет отметки корректного завершения; последние события этой сессии могут отсутствовать.");
                    }
                    // Flat, unique names keep adjacent sessions distinguishable.
                    foreach (string sourceName in new string[] { "events.jsonl", "manifest.json", "summary.json" })
                    {
                        string file = Path.Combine(session, sourceName);
                        if (File.Exists(file)) sources.Add(new SourceFile(file, Path.GetFileName(session) + "__" + sourceName));
                        else { partial = true; skipped++; }
                    }
                }
                if (sessionCount == 0) { partial = true; warnings.Add("Подтверждённых записей автоматической диагностики пока нет."); }
            }
            try
            {
                using (FileStream archiveFile = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    ownTemporary = true;
                    using (ZipArchive archive = new ZipArchive(archiveFile, ZipArchiveMode.Create, false))
                    {
                        foreach (SourceFile source in sources)
                        {
                            string file = source.Path;
                            string entryName = source.EntryName;
                            if (String.Equals(entryName, "README.txt", StringComparison.OrdinalIgnoreCase) ||
                                String.Equals(entryName, "collection-state.txt", StringComparison.OrdinalIgnoreCase))
                            {
                                skipped++;
                                partial = true;
                                continue;
                            }
                            if (included >= MaximumEntries - 2)
                            {
                                skipped++;
                                partial = true;
                                continue;
                            }
                            FileStream input = null;
                            long initialLength = 0;
                            try
                            {
                                RejectReparseAncestors(file, true);
                                FileAttributes attributes = File.GetAttributes(file);
                                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                                {
                                    skipped++;
                                    partial = true;
                                    continue;
                                }
                                input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                                initialLength = input.Length;
                            }
                            catch (IOException) { skipped++; partial = true; if (input != null) input.Dispose(); continue; }
                            catch (UnauthorizedAccessException) { skipped++; partial = true; if (input != null) input.Dispose(); continue; }

                            using (input)
                            {
                                if (initialLength > maximumSourceBytes - copiedBytes)
                                {
                                    skipped++;
                                    partial = true;
                                    continue;
                                }
                                ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                                included++;
                                using (Stream output = entry.Open())
                                {
                                    byte[] buffer = new byte[64 * 1024];
                                    long remaining = initialLength;
                                    while (remaining > 0)
                                    {
                                        int read;
                                        try { read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining)); }
                                        catch (IOException) { partial = true; break; }
                                        if (read == 0) { partial = true; break; }
                                        // Output failures must abort the archive rather than masquerade as a source read warning.
                                        output.Write(buffer, 0, read);
                                        copiedBytes += read;
                                        remaining -= read;
                                    }
                                }
                            }
                        }
                        if (skipped > 0) warnings.Add("Некоторые файлы пропущены: недоступны, являются ссылками либо превышают предел количества или размера.");
                        if (partial && warnings.Count == 0) warnings.Add("Как минимум один файл изменился или стал недоступен во время чтения; сохранены доступные данные.");
                        WriteText(archive, "README.txt", ReadmeText);
                        StringBuilder state = new StringBuilder();
                        state.AppendLine("Glukoboy Diagnostics — состояние пакета");
                        state.AppendLine("partial=" + (partial ? "true" : "false"));
                        state.AppendLine("created_utc=" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                        state.AppendLine("source_files_included=" + included.ToString(CultureInfo.InvariantCulture));
                        state.AppendLine("source_files_skipped=" + skipped.ToString(CultureInfo.InvariantCulture));
                        state.AppendLine("source_bytes_included=" + copiedBytes.ToString(CultureInfo.InvariantCulture));
                        state.AppendLine("capture_mode=" + (automatic == null ? "standalone" : "automatic_background"));
                        state.AppendLine("source_sessions_included=" + sessionCount.ToString(CultureInfo.InvariantCulture));
                        if (automatic != null) state.AppendLine("Записи расположены от новых к старым, до восьми сессий по шесть часов / 20 МиБ. Более старые сессии удаляются автоматически; при частых перезапусках или большом потоке событий история будет короче 48 часов.");
                        state.AppendLine(partial ? "НЕПОЛНЫЙ ОТЧЁТ: используйте как доступный снимок записи; последние события могут отсутствовать." : "Сборщик завершил запись перед упаковкой отчёта.");
                        state.AppendLine("Чтение каждого файла ограничено его размером на момент открытия. В неполном отчёте последняя строка журнала может быть незавершённой.");
                        foreach (string warning in warnings) state.AppendLine(warning);
                        WriteText(archive, "collection-state.txt", state.ToString());
                    }
                }
                File.Move(temporary, destination);
                ownTemporary = false;
                return destination;
            }
            finally
            {
                if (ownTemporary)
                {
                    try { File.Delete(temporary); } catch { }
                }
            }
        }

        private static bool IsSameOrChild(string path, string directory)
        {
            string prefix = directory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? directory : directory + Path.DirectorySeparatorChar;
            return String.Equals(path, directory, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDirectory(string directory)
        {
            string path = Path.GetFullPath(directory);
            string root = Path.GetPathRoot(path);
            return path.Length == root.Length ? root : path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        internal static void RequireLocalDrive(string path)
        {
            string root = Path.GetPathRoot(path);
            // Reject UNC, device paths and mapped network drives before any filesystem access.
            if (String.IsNullOrEmpty(root) || root.Length != 3 || root[1] != ':' || !Char.IsLetter(root[0]))
                throw new IOException("Отчёт можно читать и сохранять только на локальном диске.");
            DriveType type = new DriveInfo(root).DriveType;
            if (type != DriveType.Fixed && type != DriveType.Removable)
                throw new IOException("Для отчёта нужен локальный или съёмный диск; сетевые папки не используются.");
        }

        private static void RejectKnownSyncDestination(string path)
        {
            string[] variables = new string[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" };
            foreach (string variable in variables)
            {
                string configured = Environment.GetEnvironmentVariable(variable);
                if (String.IsNullOrEmpty(configured)) continue;
                string syncRoot;
                try { syncRoot = NormalizeDirectory(configured); }
                catch (ArgumentException) { continue; }
                catch (NotSupportedException) { continue; }
                if (IsSameOrChild(path, syncRoot))
                    throw new IOException("Папка OneDrive пропущена, чтобы готовый отчёт не попал в автоматическую синхронизацию.");
            }
        }

        internal static void RejectReparseAncestors(string path)
        {
            RejectReparseAncestors(path, false);
        }

        internal static void RejectReparseAncestors(string path, bool allowLeafFile)
        {
            string current = Path.GetPathRoot(path);
            string remainder = path.Substring(current.Length);
            string[] segments = remainder.Split(new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            for (int index = -1; index < segments.Length; index++)
            {
                if (index >= 0) current = Path.Combine(current, segments[index]);
                FileAttributes attributes;
                try { attributes = File.GetAttributes(current); }
                catch (FileNotFoundException) { return; }
                catch (DirectoryNotFoundException) { return; }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Папка отчёта или сохранения не должна проходить через символическую ссылку.");
                if ((attributes & FileAttributes.Directory) == 0 && !(allowLeafFile && index == segments.Length - 1))
                    throw new IOException("Один из компонентов пути отчёта не является папкой.");
            }
        }

        private static void WriteText(ZipArchive archive, string name, string text)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using (Stream stream = entry.Open())
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(true))) writer.Write(text);
        }

        private sealed class SourceFile
        {
            internal readonly string Path;
            internal readonly string EntryName;
            internal SourceFile(string path, string entryName) { Path = path; EntryName = entryName; }
        }

        private const string ReadmeText =
            "ОТЧЁТ ДИАГНОСТИКИ ГЛЮКОБОЯ\r\n\r\n" +
            "Пришлите этот ZIP в чат, в котором разбираете проблему. Сам сборщик ничего не отправляет.\r\n" +
            "Сначала проверьте collection-state.txt: в нём указано, удалось ли завершить запись.\r\n\r\n" +
            "В отчёте: состояния процессов и окон, название активной программы, сообщения об ошибках Глюкобоя и его настройки расписания.\r\n" +
            "Нажатия клавиш, снимки экрана и звук не записываются. Игра не запускается и не закрывается сборщиком.\r\n" +
            "В автоматическом режиме включаются до восьми последних сессий, до 160 МиБ исходных данных. Имена файлов содержат имя сессии; свежие сессии идут первыми. При обычном отдельном запуске предел — 32 МиБ. До 100 файлов, включая служебный текст.\r\n" +
            "Неизвестные подпапки и символические ссылки не включаются. Отсутствие корректного завершения любой включённой сессии отмечено как неполный отчёт.\r\n";
    }
}
