using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;

namespace Bubliklyuk
{
    internal static class DiagnosticLauncher
    {
        internal const string ResourceName = "Glukoboy.Diagnostics.exe";

        // Manual updates deliver only Glukoboy.exe. Keep its exact collector in
        // a content-addressed per-user cache, independent of the game's folder.
        internal static void StartInBackground()
        {
            try
            {
                ThreadPool.QueueUserWorkItem(delegate(object state)
                {
                    try
                    {
                        TryStart(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            delegate { return Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName); },
                            delegate(ProcessStartInfo info)
                            {
                                using (Process process = Process.Start(info))
                                {
                                    if (process == null) throw new IOException("Diagnostics did not start.");
                                }
                            });
                    }
                    catch (Exception error) { Diag.Log("DiagnosticLauncher.Worker", error); }
                });
            }
            catch (Exception error) { Diag.Log("DiagnosticLauncher.Queue", error); }
        }

        // The injected resource and launcher let regression checks exercise the
        // real extraction path without starting the game or the collector.
        internal static bool TryStart(string localApplicationData, Func<Stream> openResource,
            Action<ProcessStartInfo> launch)
        {
            string temporary = null;
            try
            {
                string local = ValidateLocalPath(localApplicationData);
                CheckPath(local);
                if (openResource == null || launch == null) throw new ArgumentNullException("diagnostic dependencies");
                byte[] payload;
                using (Stream resource = openResource())
                {
                    if (resource == null) throw new InvalidDataException("Missing resource: " + ResourceName);
                    using (MemoryStream buffer = new MemoryStream())
                    {
                        resource.CopyTo(buffer);
                        payload = buffer.ToArray();
                    }
                }
                if (payload.Length == 0) throw new InvalidDataException("Empty diagnostics resource.");
                string expectedHash;
                using (SHA256 hash = SHA256.Create()) expectedHash = Hex(hash.ComputeHash(payload));
                string folder = Path.Combine(Path.Combine(Path.Combine(local, "GlukoboyDiagnostics"), "bin"), expectedHash);
                EnsureDirectory(folder);
                string destination = Path.Combine(folder, ResourceName);
                CheckPath(destination);
                FileAttributes attributes;
                if (!TryGetAttributes(destination, out attributes))
                {
                    temporary = Path.Combine(folder, ".extract-" + Guid.NewGuid().ToString("N") + ".tmp");
                    CheckPath(temporary);
                    using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        output.Write(payload, 0, payload.Length);
                        output.Flush(true);
                    }
                    // Verify the written file before its atomic, no-overwrite move.
                    using (FileStream input = OpenVerified(temporary, payload.Length, expectedHash)) { }
                    CheckPath(destination);
                    CheckPath(temporary);
                    try
                    {
                        File.Move(temporary, destination);
                        temporary = null;
                    }
                    catch (IOException)
                    {
                        // A concurrent extraction may have won. Only the exact
                        // embedded bytes are acceptable; existing files stay intact.
                        if (!TryGetAttributes(destination, out attributes)) throw;
                    }
                }
                // Keep a non-writable/non-deletable read handle through launch.
                // Never execute an unverified or reparse-point cache entry.
                using (FileStream verified = OpenVerified(destination, payload.Length, expectedHash))
                {
                    CheckPath(destination);
                    ProcessStartInfo info = new ProcessStartInfo(destination, "--background");
                    info.UseShellExecute = false;
                    info.CreateNoWindow = true;
                    info.WindowStyle = ProcessWindowStyle.Hidden;
                    info.ErrorDialog = false;
                    info.WorkingDirectory = folder;
                    launch(info);
                }
                return true;
            }
            catch (Exception error)
            {
                Diag.Log("DiagnosticLauncher.Start", error);
                return false;
            }
            finally
            {
                if (temporary != null)
                {
                    try
                    {
                        CheckPath(temporary);
                        File.Delete(temporary);
                    }
                    catch (Exception error) { Diag.Log("DiagnosticLauncher.Cleanup", error); }
                }
            }
        }

        private static FileStream OpenVerified(string path, long expectedLength, string expectedHash)
        {
            CheckPath(path);
            FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                if (input.Length != expectedLength) throw new InvalidDataException("Diagnostics cache length mismatch.");
                string actualHash;
                using (SHA256 hash = SHA256.Create()) actualHash = Hex(hash.ComputeHash(input));
                if (!String.Equals(actualHash, expectedHash, StringComparison.Ordinal))
                    throw new InvalidDataException("Diagnostics cache hash mismatch.");
                return input;
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        private static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        private static string ValidateLocalPath(string path)
        {
            if (String.IsNullOrEmpty(path)) throw new IOException("Local diagnostics directory is unavailable.");
            string root = Path.GetPathRoot(path);
            // Reject UNC, device, drive-relative and current-drive-relative paths.
            if (root == null || root.Length != 3 || root[1] != ':' ||
                (root[2] != Path.DirectorySeparatorChar && root[2] != Path.AltDirectorySeparatorChar))
                throw new IOException("Diagnostics requires an absolute local path.");
            string full = Path.GetFullPath(path);
            DriveType type = new DriveInfo(Path.GetPathRoot(full)).DriveType;
            if (type != DriveType.Fixed && type != DriveType.Removable)
                throw new IOException("Diagnostics requires a local fixed or removable drive.");
            return full;
        }

        private static void CheckPath(string path)
        {
            string full = ValidateLocalPath(path);
            string current = Path.GetPathRoot(full);
            string[] parts = full.Substring(current.Length).Split(
                new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            for (int index = -1; index < parts.Length; index++)
            {
                if (index >= 0) current = Path.Combine(current, parts[index]);
                FileAttributes attributes;
                if (!TryGetAttributes(current, out attributes)) continue;
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Diagnostics cache cannot use reparse points.");
                if (index < parts.Length - 1 && (attributes & FileAttributes.Directory) == 0)
                    throw new IOException("Diagnostics cache parent is not a directory.");
            }
        }

        private static void EnsureDirectory(string path)
        {
            string full = ValidateLocalPath(path);
            string current = Path.GetPathRoot(full);
            string[] parts = full.Substring(current.Length).Split(
                new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                current = Path.Combine(current, part);
                CheckPath(current);
                FileAttributes attributes;
                if (!TryGetAttributes(current, out attributes))
                {
                    Directory.CreateDirectory(current);
                    CheckPath(current);
                    attributes = File.GetAttributes(current);
                }
                if ((attributes & FileAttributes.Directory) == 0)
                    throw new IOException("Diagnostics cache path is not a directory.");
            }
        }

        private static bool TryGetAttributes(string path, out FileAttributes attributes)
        {
            attributes = 0;
            try { attributes = File.GetAttributes(path); return true; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }
    }
}
