using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Bubliklyuk
{
    // Called only on the UI thread; the Forms timer keeps COM calls/disposal in
    // the same apartment. Dependencies let tests cover restoration without audio.
    internal sealed class AudioMute
    {
        private readonly IAttackAudioDevices devices;
        private readonly bool useTimer;
        private readonly Dictionary<string, DeviceState> saved = new Dictionary<string, DeviceState>(StringComparer.Ordinal);
        private System.Windows.Forms.Timer timer;
        private bool active, attack;

        internal AudioMute() : this(new CoreAudioDevices(), true) { }
        internal AudioMute(IAttackAudioDevices devices, bool useTimer) { this.devices = devices; this.useTimer = useTimer; }
        internal void Begin() { SetMode(false); }
        internal void BeginAttack() { SetMode(true); }

        private void SetMode(bool isAttack)
        {
            // Attack -> night retains the ORIGINAL snapshot until Restore().
            active = true;
            attack = isAttack;
            Refresh();
            if (useTimer && timer == null)
            {
                timer = new System.Windows.Forms.Timer { Interval = 500 };
                timer.Tick += delegate { Refresh(); };
                timer.Start();
            }
        }

        internal void Refresh()
        {
            if (!active) return;
            IList<IAttackAudioDevice> current;
            try { current = devices.OpenActiveDevices(); }
            catch (Exception ex) { Diag.Log("AudioMute.EnumerateDevices", ex); return; }
            foreach (IAttackAudioDevice device in current)
            {
                bool retained = false;
                try
                {
                    DeviceState state;
                    if (!saved.TryGetValue(device.Id, out state))
                    {
                        state = new DeviceState(device);
                        saved.Add(device.Id, state);
                    }
                    else { state.Device.Dispose(); state.Device = device; }
                    retained = true;
                    if (attack)
                    {
                        // Silence other sessions BEFORE unmuting the master.
                        UpdateSessions(state);
                        if (device.IsDefault)
                        {
                            if (device.Volume <= 0.0001f) { state.VolumeChanged = true; device.Volume = 0.25f; }
                            state.MuteChanged = true;
                            device.Mute = false;
                        }
                        else if (state.MuteChanged)
                        {
                            device.Mute = state.Mute;
                            if (state.VolumeChanged) device.Volume = state.Volume;
                        }
                    }
                    else { state.MuteChanged = true; device.Mute = true; }
                }
                catch (Exception ex) { Diag.Log("AudioMute.Refresh", ex); }
                finally { if (!retained) device.Dispose(); }
            }
        }

        private static void UpdateSessions(DeviceState state)
        {
            IList<IAttackAudioSession> current = state.Device.OpenSessions();
            foreach (IAttackAudioSession session in current)
            {
                bool retained = false;
                try
                {
                    SessionState snapshot;
                    if (!state.Sessions.TryGetValue(session.Id, out snapshot))
                    {
                        snapshot = new SessionState(session);
                        state.Sessions.Add(session.Id, snapshot);
                    }
                    else { snapshot.Session.Dispose(); snapshot.Session = session; }
                    retained = true;
                    if (session.IsOwn && session.Volume <= 0.0001f) { snapshot.VolumeChanged = true; session.Volume = 1.0f; }
                    snapshot.MuteChanged = true;
                    session.Mute = !session.IsOwn;
                }
                catch (Exception ex) { Diag.Log("AudioMute.Session", ex); }
                finally { if (!retained) session.Dispose(); }
            }
        }

        internal void Restore()
        {
            active = false;
            if (timer != null) { timer.Stop(); timer.Dispose(); timer = null; }
            foreach (DeviceState state in saved.Values)
            {
                // Restore original volume/mute before releasing the other sessions.
                if (state.VolumeChanged) TryRestore(delegate { state.Device.Volume = state.Volume; });
                if (state.MuteChanged) TryRestore(delegate { state.Device.Mute = state.Mute; });
                foreach (SessionState session in state.Sessions.Values)
                {
                    if (session.VolumeChanged) TryRestore(delegate { session.Session.Volume = session.Volume; });
                    if (session.MuteChanged) TryRestore(delegate { session.Session.Mute = session.Mute; });
                    session.Session.Dispose();
                }
                state.Device.Dispose();
            }
            saved.Clear();
        }

        private static void TryRestore(Action restore)
        {
            try { restore(); }
            catch (Exception first)
            {
                Diag.Log("AudioMute.Restore", first);
                try { restore(); }
                catch (Exception retry) { Diag.Log("AudioMute.Restore.Retry", retry); }
            }
        }

        private sealed class DeviceState
        {
            internal IAttackAudioDevice Device;
            internal readonly bool Mute;
            internal readonly float Volume;
            internal bool MuteChanged, VolumeChanged;
            internal readonly Dictionary<string, SessionState> Sessions = new Dictionary<string, SessionState>(StringComparer.Ordinal);
            internal DeviceState(IAttackAudioDevice device) { Mute = device.Mute; Volume = device.Volume; Device = device; }
        }

        private sealed class SessionState
        {
            internal IAttackAudioSession Session;
            internal readonly bool Mute;
            internal readonly float Volume;
            internal bool MuteChanged, VolumeChanged;
            internal SessionState(IAttackAudioSession session)
            {
                Mute = session.Mute; Volume = session.IsOwn ? session.Volume : 0; Session = session;
            }
        }
    }

    internal interface IAttackAudioDevices { IList<IAttackAudioDevice> OpenActiveDevices(); }
    internal interface IAttackAudioDevice : IDisposable
    {
        string Id { get; }
        bool IsDefault { get; }
        bool Mute { get; set; }
        float Volume { get; set; }
        IList<IAttackAudioSession> OpenSessions();
    }
    internal interface IAttackAudioSession : IDisposable
    {
        string Id { get; }
        bool IsOwn { get; }
        bool Mute { get; set; }
        float Volume { get; set; }
    }

    internal sealed class CoreAudioDevices : IAttackAudioDevices
    {
        public IList<IAttackAudioDevice> OpenActiveDevices()
        {
            List<IAttackAudioDevice> result = new List<IAttackAudioDevice>();
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection collection = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
                HashSet<string> defaults = new HashSet<string>(StringComparer.Ordinal);
                for (int role = 0; role <= 1; role++)
                {
                    IMMDevice device = null;
                    try
                    {
                        if (enumerator.GetDefaultAudioEndpoint(0, role, out device) >= 0)
                        {
                            string id;
                            Marshal.ThrowExceptionForHR(device.GetId(out id));
                            defaults.Add(id);
                        }
                    }
                    finally { Release(device); }
                }
                Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0, 1, out collection));
                uint count;
                Marshal.ThrowExceptionForHR(collection.GetCount(out count));
                for (uint index = 0; index < count; index++)
                {
                    IMMDevice device = null;
                    try
                    {
                        Marshal.ThrowExceptionForHR(collection.Item(index, out device));
                        string id;
                        Marshal.ThrowExceptionForHR(device.GetId(out id));
                        result.Add(new CoreAudioDevice(device, id, defaults.Contains(id)));
                    }
                    catch (Exception ex) { Diag.Log("AudioMute.OpenDevice", ex); }
                    finally { Release(device); }
                }
                return result;
            }
            catch
            {
                foreach (IAttackAudioDevice device in result) device.Dispose();
                throw;
            }
            finally { Release(collection); Release(enumerator); }
        }

        internal static void Release(object value)
        {
            if (value == null) return;
            try { if (Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
            catch (Exception ex) { Diag.Log("AudioMute.Release", ex); }
        }
    }

    internal sealed class CoreAudioDevice : IAttackAudioDevice
    {
        private IAudioEndpointVolume endpoint;
        private IAudioSessionManager2 sessions;
        public string Id { get; private set; }
        public bool IsDefault { get; private set; }

        internal CoreAudioDevice(IMMDevice device, string id, bool isDefault)
        {
            Id = id; IsDefault = isDefault;
            object volume = null, manager = null;
            try
            {
                Guid volumeId = typeof(IAudioEndpointVolume).GUID, managerId = typeof(IAudioSessionManager2).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref volumeId, 23, IntPtr.Zero, out volume));
                endpoint = (IAudioEndpointVolume)volume;
                Marshal.ThrowExceptionForHR(device.Activate(ref managerId, 23, IntPtr.Zero, out manager));
                sessions = (IAudioSessionManager2)manager;
            }
            catch { CoreAudioDevices.Release(volume); CoreAudioDevices.Release(manager); throw; }
        }

        public bool Mute
        {
            get { bool value; Marshal.ThrowExceptionForHR(endpoint.GetMute(out value)); return value; }
            set { Guid context = Guid.Empty; Marshal.ThrowExceptionForHR(endpoint.SetMute(value, ref context)); }
        }
        public float Volume
        {
            get { float value; Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out value)); return value; }
            set { Guid context = Guid.Empty; Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar(value, ref context)); }
        }

        public IList<IAttackAudioSession> OpenSessions()
        {
            List<IAttackAudioSession> result = new List<IAttackAudioSession>();
            IAudioSessionEnumerator list = null;
            try
            {
                // Create this process's default session before playback, so persisted
                // per-app mute/zero volume cannot swallow the start of the introduction.
                IAudioSessionControl own = null;
                try
                {
                    Marshal.ThrowExceptionForHR(sessions.GetAudioSessionControl(IntPtr.Zero, 0, out own));
                    result.Add(new CoreAudioSession(own, true));
                    own = null;
                }
                finally { CoreAudioDevices.Release(own); }
                string ownId = result[0].Id;
                Marshal.ThrowExceptionForHR(sessions.GetSessionEnumerator(out list));
                int count;
                Marshal.ThrowExceptionForHR(list.GetCount(out count));
                for (int index = 0; index < count; index++)
                {
                    IAudioSessionControl control = null;
                    try
                    {
                        Marshal.ThrowExceptionForHR(list.GetSession(index, out control));
                        CoreAudioSession session = new CoreAudioSession(control, false);
                        control = null;
                        if (session.Id == ownId) session.Dispose();
                        else result.Add(session);
                    }
                    catch (Exception ex) { Diag.Log("AudioMute.OpenSession", ex); }
                    finally { CoreAudioDevices.Release(control); }
                }
                return result;
            }
            catch
            {
                foreach (IAttackAudioSession session in result) session.Dispose();
                throw;
            }
            finally { CoreAudioDevices.Release(list); }
        }

        public void Dispose()
        {
            CoreAudioDevices.Release(sessions); sessions = null;
            CoreAudioDevices.Release(endpoint); endpoint = null;
        }
    }

    internal sealed class CoreAudioSession : IAttackAudioSession
    {
        private IAudioSessionControl control;
        private ISimpleAudioVolume volume;
        private static readonly uint ProcessId = (uint)Process.GetCurrentProcess().Id;
        public string Id { get; private set; }
        public bool IsOwn { get; private set; }
        internal CoreAudioSession(IAudioSessionControl control, bool own)
        {
            IAudioSessionControl2 details = (IAudioSessionControl2)control;
            uint processId; string id;
            Marshal.ThrowExceptionForHR(details.GetProcessId(out processId));
            Marshal.ThrowExceptionForHR(details.GetSessionInstanceIdentifier(out id));
            volume = (ISimpleAudioVolume)control;
            this.control = control;
            Id = id;
            IsOwn = own || processId == ProcessId;
        }
        public bool Mute
        {
            get { bool value; Marshal.ThrowExceptionForHR(volume.GetMute(out value)); return value; }
            set { Guid context = Guid.Empty; Marshal.ThrowExceptionForHR(volume.SetMute(value, ref context)); }
        }
        public float Volume
        {
            get { float value; Marshal.ThrowExceptionForHR(volume.GetMasterVolume(out value)); return value; }
            set { Guid context = Guid.Empty; Marshal.ThrowExceptionForHR(volume.SetMasterVolume(value, ref context)); }
        }
        public void Dispose()
        {
            // Both interfaces belong to the SAME RCW; release its owned reference once.
            volume = null;
            CoreAudioDevices.Release(control); control = null;
        }
    }

    // Exact Windows SDK vtable order, including inherited slots before new methods:
    // https://raw.githubusercontent.com/microsoft/win32metadata/main/generation/WinSDK/RecompiledIdlHeaders/um/audiopolicy.h
    // https://raw.githubusercontent.com/microsoft/win32metadata/main/generation/WinSDK/RecompiledIdlHeaders/um/mmdeviceapi.h
    // https://raw.githubusercontent.com/microsoft/win32metadata/main/generation/WinSDK/RecompiledIdlHeaders/um/Audioclient.h
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint flags, out IAudioSessionControl control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint flags, out ISimpleAudioVolume volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
        [PreserveSig] int RegisterSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterSessionNotification(IntPtr notification);
        [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr notification);
        [PreserveSig] int UnregisterDuckNotification(IntPtr notification);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl control);
    }
    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string value);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string value);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid value);
        [PreserveSig] int SetGroupingParam(ref Guid value, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string value);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string value);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid value);
        [PreserveSig] int SetGroupingParam(ref Guid value, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string value);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string value);
        [PreserveSig] int GetProcessId(out uint processId);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float volume, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float volume);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
