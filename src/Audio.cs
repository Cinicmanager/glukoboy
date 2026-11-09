using System;
using System.Runtime.InteropServices;

namespace Bubliklyuk
{
    internal sealed class AudioMute
    {
        private IAudioEndpointVolume endpoint;
        private bool savedMute;
        private bool mustRestore;

        internal void Begin()
        {
            if (endpoint != null && mustRestore)
            {
                try
                {
                    Guid eventContext = Guid.Empty;
                    int hr = endpoint.SetMute(true, ref eventContext);
                    if (hr < 0) Diag.Log("AudioMute.Begin.ReapplyMute", Marshal.GetExceptionForHR(hr));
                }
                catch (Exception ex) { Diag.Log("AudioMute.Begin.ReapplyMute", ex); }
                return;
            }
            Restore();
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            object activated = null;
            try
            {
                Type type = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
                enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type);
                // eRender = 0, eMultimedia = 1. Keep THIS endpoint until restoration,
                // even if the default output device changes during the attack.
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out device));
                Guid iid = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out activated));
                endpoint = (IAudioEndpointVolume)activated;
                activated = null; // Ownership of the RCW transferred to endpoint.
                Marshal.ThrowExceptionForHR(endpoint.GetMute(out savedMute));
                mustRestore = true; // Set before SetMute: also restore after partial failure.
                Guid eventContext = Guid.Empty;
                Marshal.ThrowExceptionForHR(endpoint.SetMute(true, ref eventContext));
            }
            catch (Exception ex) { Diag.Log("AudioMute.Begin", ex); Restore(); }
            finally
            {
                Release(activated);
                Release(device);
                Release(enumerator);
            }
        }

        internal void Restore()
        {
            IAudioEndpointVolume original = endpoint;
            endpoint = null;
            bool restore = mustRestore;
            mustRestore = false;
            if (original == null) return;
            try
            {
                if (restore)
                {
                    Guid eventContext = Guid.Empty;
                    int hr = original.SetMute(savedMute, ref eventContext);
                    if (hr < 0)
                    {
                        Diag.Log("AudioMute.Restore.SetMute", Marshal.GetExceptionForHR(hr));
                        int retryHr = original.SetMute(savedMute, ref eventContext);
                        if (retryHr < 0) Diag.Log("AudioMute.Restore.SetMuteRetry", Marshal.GetExceptionForHR(retryHr));
                    }
                }
            }
            catch (Exception ex) { Diag.Log("AudioMute.Restore", ex); }
            finally { Release(original); }
        }

        private static void Release(object value)
        {
            if (value == null) return;
            try { if (Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
            catch (Exception ex) { Diag.Log("AudioMute.Release", ex); }
        }
    }

    // Method declaration order is the native vtable order, NOT alphabetical order.
    // IUnknown slots are supplied by InterfaceIsIUnknown; do not declare them here.
    // https://learn.microsoft.com/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immdeviceenumerator
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    // Verified against the Microsoft Windows SDK endpointvolume.h declaration.
    // https://raw.githubusercontent.com/microsoft/win32metadata/main/generation/WinSDK/RecompiledIdlHeaders/um/endpointvolume.h
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint count);
        [PreserveSig] int VolumeStepUp(ref Guid context);
        [PreserveSig] int VolumeStepDown(ref Guid context);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float min, out float max, out float increment);
    }
}
