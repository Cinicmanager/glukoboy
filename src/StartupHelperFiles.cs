using System;
using System.IO;
using System.Reflection;

namespace Bubliklyuk
{
    internal static class StartupHelperFiles
    {
        // Older manual updaters download only the exe. Deliver the optional helper
        // with that exe, but never enable startup or replace the user's files here.
        internal static void Ensure(string folder)
        {
            foreach (string name in new string[] { "Включить автозапуск.cmd", "startup.ps1" })
            {
                string temporary = null;
                try
                {
                    string destination = Path.Combine(folder, name);
                    if (File.Exists(destination)) continue;
                    using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                    {
                        if (resource == null) throw new InvalidDataException("Missing resource: " + name);
                        temporary = Path.Combine(folder, ".glukoboy-startup-" + Guid.NewGuid().ToString("N") + ".tmp");
                        using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                            resource.CopyTo(output);
                    }
                    // Move without overwrite also preserves files created during extraction.
                    File.Move(temporary, destination);
                    temporary = null;
                }
                catch (Exception error) { Diag.Log("StartupHelperFiles.Ensure " + name, error); }
                finally
                {
                    if (temporary != null)
                    {
                        try { File.Delete(temporary); }
                        catch (Exception error) { Diag.Log("StartupHelperFiles.Cleanup", error); }
                    }
                }
            }
        }
    }
}
