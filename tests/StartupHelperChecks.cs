using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace Bubliklyuk
{
    internal static class StartupHelperChecks
    {
        internal static void Run(Action<bool, string> check, string artifacts)
        {
            string folder = Path.Combine(artifacts, "startup-extracted");
            Directory.CreateDirectory(folder);
            StartupHelperFiles.Ensure(folder);
            foreach (string name in new string[] { "Включить автозапуск.cmd", "startup.ps1" })
            {
                string path = Path.Combine(folder, name);
                check(File.Exists(path), "EXE-only update delivers " + name);
                using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                using (Stream disk = File.OpenRead(path))
                using (SHA256 hash = SHA256.Create())
                    check(Convert.ToBase64String(hash.ComputeHash(resource)) == Convert.ToBase64String(hash.ComputeHash(disk)),
                        "Delivered helper matches embedded bytes: " + name);
                File.WriteAllText(path, "parent's existing file");
            }
            StartupHelperFiles.Ensure(folder);
            foreach (string name in new string[] { "Включить автозапуск.cmd", "startup.ps1" })
                check(File.ReadAllText(Path.Combine(folder, name)) == "parent's existing file", "Existing helper is preserved: " + name);
            check(Directory.GetFiles(folder, "*.tmp").Length == 0, "No unfinished helper files");

            string obstructed = Path.Combine(artifacts, "startup-obstructed");
            Directory.CreateDirectory(obstructed);
            Directory.CreateDirectory(Path.Combine(obstructed, "Включить автозапуск.cmd"));
            StartupHelperFiles.Ensure(obstructed);
            check(File.Exists(Path.Combine(obstructed, "startup.ps1")), "One failed helper does not block the other or the game");
            check(Directory.GetFiles(obstructed, "*.tmp").Length == 0, "Failed helper extraction cleans its temporary file");
            StartupHelperFiles.Ensure(Path.Combine(artifacts, "missing-parent", "missing-folder"));
        }
    }
}
