using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace GlukoboyDiagnostics
{
    // Observational APIs only. WM_NULL is timeout-bounded; no focus changes,
    // desktop switching, input capture, or window manipulation.
    internal sealed class NativeProbe : IDisposable
    {
        internal const int MaximumWindows = 16;
        private IVirtualDesktopManager desktopManager;
        private bool desktopAttempted;
        private string desktopFailure;
        private string dwmFailure;

        internal Dictionary<string, object> Capture(ICollection<int> targetPids)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            List<Dictionary<string, object>> windows = new List<Dictionary<string, object>>();
            HashSet<int> pids = new HashSet<int>(targetPids);
            bool truncated = false;
            string callbackFailure = null;
            Stopwatch probeTime = Stopwatch.StartNew();
            int responsivenessChecks = 0;
            EnumWindowsCallback callback = delegate(IntPtr window, IntPtr unused)
            {
                try
                {
                    uint pid;
                    if (GetWindowThreadProcessId(window, out pid) == 0 || !pids.Contains(unchecked((int)pid))) return true;
                    if (windows.Count >= MaximumWindows) { truncated = true; return false; }
                    Dictionary<string, object> item = new Dictionary<string, object>();
                    item["hwnd"] = HandleText(window);
                    item["pid"] = pid;
                    item["visible"] = IsWindowVisible(window);
                    item["minimized"] = IsIconic(window);
                    RECT rect;
                    if (GetWindowRect(window, out rect)) item["rect"] = new int[] { rect.Left, rect.Top, rect.Right, rect.Bottom };
                    else { item["rect"] = null; item["rect_error"] = Win32Error(); }
                    // Captions are read only after exact target PID filtering.
                    StringBuilder title = new StringBuilder(257);
                    SetLastError(0);
                    int titleLength = GetWindowText(window, title, title.Capacity);
                    int titleError = Marshal.GetLastWin32Error();
                    item["title"] = titleLength == 0 && titleError != 0 ? null : title.ToString();
                    if (titleLength == 0 && titleError != 0) item["title_error"] = titleError;
                    CaptureDesktop(window, item);
                    CaptureCloak(window, item);
                    if (responsivenessChecks < 8 && probeTime.ElapsedMilliseconds < 200)
                    {
                        responsivenessChecks++;
                        UIntPtr response;
                        SetLastError(0);
                        IntPtr sent = SendMessageTimeout(window, 0, UIntPtr.Zero, IntPtr.Zero, 0x0002 | 0x0020, 25, out response);
                        if (sent != IntPtr.Zero) item["responsive"] = true;
                        else
                        {
                            int error = Marshal.GetLastWin32Error();
                            // A timeout, UIPI denial, and a disappearing window are distinct from confirmed success.
                            item["responsive"] = null;
                            item["responsiveness_error"] = error;
                            item["responsiveness_status"] = error == 1460 || error == 0 ? "timeout_or_no_result" : "probe_failed";
                        }
                    }
                    else { item["responsive"] = null; item["responsiveness_status"] = "sample_budget_exhausted"; }
                    item["exists_at_end"] = IsWindow(window);
                    uint endPid;
                    bool sameOwner = GetWindowThreadProcessId(window, out endPid) != 0 && endPid == pid;
                    item["same_pid_at_end"] = sameOwner;
                    if (!(bool)item["exists_at_end"] || !sameOwner)
                    {
                        item["visible"] = null;
                        item["minimized"] = null;
                        item["title"] = null;
                        item["observation_status"] = "window_disappeared_or_reused_during_probe";
                    }
                    windows.Add(item);
                    return true;
                }
                catch (Exception ex) { callbackFailure = ErrorText(ex); return false; }
            };
            try
            {
                SetLastError(0);
                bool ok = EnumWindows(callback, IntPtr.Zero);
                int error = Marshal.GetLastWin32Error();
                result["enumeration_complete"] = ok && !truncated && callbackFailure == null;
                result["truncated"] = truncated;
                if (!ok && !truncated && callbackFailure == null) result["enumeration_error"] = error;
                if (callbackFailure != null) result["enumeration_exception"] = callbackFailure;
            }
            catch (Exception ex) { result["enumeration_complete"] = false; result["enumeration_exception"] = ErrorText(ex); }
            windows.Sort(delegate(Dictionary<string, object> a, Dictionary<string, object> b)
            { return StringComparer.Ordinal.Compare((string)a["hwnd"], (string)b["hwnd"]); });
            result["windows"] = windows;
            result["foreground"] = CaptureForeground();
            result["monitors"] = CaptureMonitors();
            return result;
        }

        private Dictionary<string, object> CaptureMonitors()
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            List<Dictionary<string, object>> monitors = new List<Dictionary<string, object>>();
            bool truncated = false;
            MonitorCallback callback = delegate(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr unused)
            {
                if (monitors.Count >= 16) { truncated = true; return false; }
                Dictionary<string, object> item = new Dictionary<string, object>();
                MONITORINFO info = new MONITORINFO();
                info.Size = Marshal.SizeOf(typeof(MONITORINFO));
                if (GetMonitorInfo(monitor, ref info))
                {
                    item["bounds"] = new int[] { info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom };
                    item["work_area"] = new int[] { info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom };
                    item["primary"] = (info.Flags & 1) != 0;
                }
                else item["error"] = Win32Error();
                monitors.Add(item);
                return true;
            };
            try
            {
                SetLastError(0);
                bool ok = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
                int error = Marshal.GetLastWin32Error();
                result["enumeration_complete"] = ok && !truncated;
                result["truncated"] = truncated;
                if (!ok && !truncated) result["error"] = error;
            }
            catch (Exception ex) { result["enumeration_complete"] = false; result["error"] = ErrorText(ex); }
            result["items"] = monitors;
            return result;
        }

        private Dictionary<string, object> CaptureForeground()
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            try
            {
                IntPtr foreground = GetForegroundWindow();
                result["hwnd"] = HandleText(foreground);
                if (foreground == IntPtr.Zero) { result["status"] = "no_foreground_window"; return result; }
                uint pid;
                if (GetWindowThreadProcessId(foreground, out pid) == 0)
                { result["pid"] = null; result["process_error"] = Win32Error(); }
                else
                {
                    result["pid"] = pid;
                    try { using (Process process = Process.GetProcessById(unchecked((int)pid))) result["process_name"] = process.ProcessName; }
                    catch (Exception ex) { result["process_name"] = null; result["process_error"] = ErrorText(ex); }
                }
                CaptureDesktop(foreground, result);
                result["exists_at_end"] = IsWindow(foreground);
                result["still_foreground_at_end"] = GetForegroundWindow() == foreground;
                // No title, executable path, or command line for unrelated foreground programs.
            }
            catch (Exception ex) { result["error"] = ErrorText(ex); }
            return result;
        }

        private void CaptureCloak(IntPtr window, Dictionary<string, object> result)
        {
            result["dwm_cloaked"] = null;
            if (dwmFailure != null) { result["dwm_error"] = dwmFailure; return; }
            try
            {
                uint cloaked;
                int hr = DwmGetWindowAttribute(window, 14, out cloaked, 4);
                if (hr >= 0) { result["dwm_cloaked"] = cloaked != 0; result["dwm_cloak_flags"] = cloaked; }
                else result["dwm_error"] = HResultText(hr);
            }
            catch (Exception ex) { dwmFailure = ErrorText(ex); result["dwm_error"] = dwmFailure; }
        }

        private void CaptureDesktop(IntPtr window, Dictionary<string, object> result)
        {
            result["desktop_id"] = null;
            result["on_current_virtual_desktop"] = null;
            if (!desktopAttempted)
            {
                desktopAttempted = true;
                try
                {
                    Type type = Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"), true);
                    desktopManager = (IVirtualDesktopManager)Activator.CreateInstance(type);
                }
                catch (Exception ex) { desktopFailure = ErrorText(ex); }
            }
            if (desktopManager == null) { result["desktop_error"] = desktopFailure ?? "manager_unavailable"; return; }
            try
            {
                Guid id;
                int idHr = desktopManager.GetWindowDesktopId(window, out id);
                if (idHr >= 0) result["desktop_id"] = id.ToString("D");
                else result["desktop_id_error"] = HResultText(idHr);
            }
            catch (Exception ex) { result["desktop_id_error"] = ErrorText(ex); }
            try
            {
                bool current;
                int currentHr = desktopManager.IsWindowOnCurrentVirtualDesktop(window, out current);
                if (currentHr >= 0) result["on_current_virtual_desktop"] = current;
                else result["current_desktop_error"] = HResultText(currentHr);
            }
            catch (Exception ex) { result["current_desktop_error"] = ErrorText(ex); }
        }

        internal static string ErrorText(Exception ex)
        {
            string value = ex.GetType().Name + ": " + ex.Message;
            return value.Length <= 500 ? value : value.Substring(0, 500);
        }
        private static string HandleText(IntPtr value) { return "0x" + value.ToInt64().ToString("X", CultureInfo.InvariantCulture); }
        private static string HResultText(int value) { return "HRESULT 0x" + value.ToString("X8", CultureInfo.InvariantCulture); }
        private static string Win32Error() { return "Win32 " + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture); }
        public void Dispose()
        {
            if (desktopManager != null)
            {
                try { Marshal.ReleaseComObject(desktopManager); } catch { }
                desktopManager = null;
            }
        }

        [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IVirtualDesktopManager
        {
            [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr window, [MarshalAs(UnmanagedType.Bool)] out bool current);
            [PreserveSig] int GetWindowDesktopId(IntPtr window, out Guid desktopId);
            // The mutating third COM method is intentionally not declared or called.
        }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { internal int Size; internal RECT Monitor, Work; internal uint Flags; }
        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr unused);
        private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr unused);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr unused);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out UIntPtr result);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out uint value, int size);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr unused);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("kernel32.dll")] private static extern void SetLastError(uint error);
    }
}
