using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Bubliklyuk
{
    internal static class AudioChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            Playback(check);
            Failure(check);
            Restoration(check);
        }

        private static void Playback(Action<bool, string> check)
        {
            FakePlayback fake = new FakePlayback();
            int failures = 0;
            DictationPlayer player = new DictationPlayer(delegate { Interlocked.Increment(ref failures); }, Resource, fake);
            try
            {
                player.Start("one", true);
                check(fake.WaitCalls(1) && fake.At(0) == 0, "Dictation starts with introduction");
                fake.Advance.Set();
                check(fake.WaitCalls(2) && fake.At(1) == 1, "Introduction is followed by selected phrase");
                fake.Advance.Set(); check(fake.WaitCalls(3), "Second automatic dictation loop");
                fake.Advance.Set(); check(fake.WaitCalls(4), "Third automatic dictation loop");
                player.Repeat();
                check(fake.WaitCalls(5) && fake.At(4) == 1, "Repeat restarts phrase without introduction");
                player.Stop();
                check(SpinWait.SpinUntil(delegate { return fake.Active == 0; }, 2000), "Stop cancels active phrase");
                int stoppedCount = fake.Count;
                Thread.Sleep(25);
                check(fake.Count == stoppedCount, "Stop prevents another loop");
                player.Start("one", true);
                check(fake.WaitCalls(6) && fake.At(5) == 0, "New introduction begins");
                player.Start("two", false);
                check(fake.WaitCalls(7) && fake.At(6) == 2, "Replacing request during introduction plays only new phrase");
                player.Start("one", true);
                check(fake.WaitCalls(8) && fake.At(7) == 0, "Introduction can be cancelled");
                player.Stop();
                check(SpinWait.SpinUntil(delegate { return fake.Active == 0; }, 2000), "Stop cancels introduction");
                Thread.Sleep(25);
                check(fake.Count == 8, "Cancelled introduction cannot launch phrase");
                player.Start("two", false);
                check(fake.WaitCalls(9), "Start remains available after Stop");
                player.Dispose();
                check(SpinWait.SpinUntil(delegate { return fake.Active == 0; }, 2000), "Dispose cancels active playback");
                player.Stop(); player.Repeat(); player.Start("one", true); player.Dispose();
                Thread.Sleep(25);
                check(fake.Count == 9 && failures == 0 && fake.MaximumActive == 1,
                    "No voices overlap; disposed player and repeated Stop/Dispose are inert");
            }
            finally { player.Dispose(); }
        }

        private static byte[] Resource(string name) { return new byte[] { (byte)(name == "dictation-intro.wav" ? 0 : name == "one" ? 1 : 2) }; }

        private static void Failure(Action<bool, string> check)
        {
            using (ManualResetEvent failed = new ManualResetEvent(false))
            {
                FakePlayback fake = new FakePlayback { Throw = true };
                int failures = 0;
                using (DictationPlayer player = new DictationPlayer(delegate { Interlocked.Increment(ref failures); failed.Set(); }, Resource, fake))
                {
                    player.Start("one", false);
                    check(failed.WaitOne(2000), "Playback failure reaches callback without blocking caller");
                    Thread.Sleep(25);
                    check(failures == 1 && fake.Count == 1, "Playback failure ends loop rather than spinning");
                    player.Stop();
                }
            }
        }

        private static void Restoration(Action<bool, string> check)
        {
            FakeDevices source = new FakeDevices();
            FakeDevice first = new FakeDevice { Id = "first", IsDefault = true, Mute = true, Volume = 0 };
            FakeSession own = new FakeSession { Id = "own", IsOwn = true, Mute = true, Volume = 0 };
            FakeSession other = new FakeSession { Id = "other", Mute = false, Volume = 0.8f };
            FakeSession alreadyMuted = new FakeSession { Id = "already-muted", Mute = true, Volume = 0.4f };
            first.Sessions.Add(own); first.Sessions.Add(other); first.Sessions.Add(alreadyMuted); source.Items.Add(first);
            AudioMute mute = new AudioMute(source, false);
            mute.BeginAttack();
            check(!first.Mute && first.Volume == 0.25f && !own.Mute && own.Volume == 1,
                "Attack makes muted/zero master and own session audible");
            check(other.Mute && alreadyMuted.Mute && other.Volume == 0.8f, "Attack mutes other sessions without changing volume");
            FakeSession later = new FakeSession { Id = "later", Mute = false, Volume = 0.6f };
            first.Sessions.Add(later); mute.Refresh();
            check(later.Mute, "Refresh catches newly appearing sessions");
            mute.BeginAttack(); mute.Begin();
            check(first.Mute && other.Mute, "Attack to night mutes master and retains session ownership");
            mute.Restore();
            check(first.Mute && first.Volume == 0 && own.Mute && own.Volume == 0,
                "Restore preserves original master and own-session zero volume/mute across mode transition");
            check(!other.Mute && alreadyMuted.Mute && !later.Mute, "Restore keeps each original session mute separately");
            mute.Restore(); mute.Refresh();
            check(!other.Mute, "Restore is idempotent and stops polling changes");

            first.Mute = false; first.Volume = 0.7f;
            mute.Begin();
            check(first.Mute && !other.Mute, "Night uses master mute without rewriting app sessions");
            mute.Restore();
            check(!first.Mute && first.Volume == 0.7f, "Night restores original unmuted state and volume");

            mute.BeginAttack();
            first.IsDefault = false;
            FakeDevice second = new FakeDevice { Id = "second", IsDefault = true, Mute = true, Volume = 0 };
            source.Items.Add(second); mute.Refresh();
            check(!second.Mute && second.Volume == 0.25f, "New default device is captured and made audible");
            source.Items.Remove(first); mute.Begin(); mute.Restore();
            check(!first.Mute && first.Volume == 0.7f && second.Mute && second.Volume == 0 && !other.Mute,
                "Device changes retain and restore independent original snapshots");

            first.IsDefault = true; source.Items.Clear(); source.Items.Add(first);
            other.ThrowAfterMuteOnce = true;
            mute.BeginAttack(); mute.Restore();
            check(!other.Mute, "Snapshot precedes mutation even when setter changes state and then throws");
        }

        // Optional integration probe: reads existing state only. It never invokes
        // AudioMute.Begin, any COM setter, waveOutWrite, or opens a playback stream.
        internal static void RunNativeReadOnly(Action<bool, string> check)
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection devices = null;
            int sessionsRead = 0;
            try
            {
                enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
                Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0, 1, out devices));
                uint count;
                Marshal.ThrowExceptionForHR(devices.GetCount(out count));
                for (uint index = 0; index < count; index++)
                {
                    IMMDevice device = null;
                    object endpoint = null, manager = null;
                    IAudioSessionEnumerator list = null;
                    try
                    {
                        Marshal.ThrowExceptionForHR(devices.Item(index, out device));
                        string deviceId; Marshal.ThrowExceptionForHR(device.GetId(out deviceId));
                        Guid iid = typeof(IAudioEndpointVolume).GUID;
                        Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out endpoint));
                        bool masterMute; float masterVolume;
                        Marshal.ThrowExceptionForHR(((IAudioEndpointVolume)endpoint).GetMute(out masterMute));
                        Marshal.ThrowExceptionForHR(((IAudioEndpointVolume)endpoint).GetMasterVolumeLevelScalar(out masterVolume));
                        check(!String.IsNullOrEmpty(deviceId) && masterVolume >= 0 && masterVolume <= 1, "Read-only endpoint COM getters");
                        iid = typeof(IAudioSessionManager2).GUID;
                        Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out manager));
                        Marshal.ThrowExceptionForHR(((IAudioSessionManager2)manager).GetSessionEnumerator(out list));
                        int sessionCount; Marshal.ThrowExceptionForHR(list.GetCount(out sessionCount));
                        for (int item = 0; item < sessionCount; item++)
                        {
                            IAudioSessionControl control = null;
                            try
                            {
                                Marshal.ThrowExceptionForHR(list.GetSession(item, out control));
                                string id; uint processId; bool muted; float volume;
                                Marshal.ThrowExceptionForHR(((IAudioSessionControl2)control).GetSessionInstanceIdentifier(out id));
                                Marshal.ThrowExceptionForHR(((IAudioSessionControl2)control).GetProcessId(out processId));
                                Marshal.ThrowExceptionForHR(((ISimpleAudioVolume)control).GetMute(out muted));
                                Marshal.ThrowExceptionForHR(((ISimpleAudioVolume)control).GetMasterVolume(out volume));
                                check(!String.IsNullOrEmpty(id) && volume >= 0 && volume <= 1, "Read-only session COM ID/PID/mute/volume getters");
                                sessionsRead++;
                            }
                            finally { CoreAudioDevices.Release(control); }
                        }
                    }
                    finally { CoreAudioDevices.Release(list); CoreAudioDevices.Release(manager); CoreAudioDevices.Release(endpoint); CoreAudioDevices.Release(device); }
                }
                check(count > 0 && sessionsRead > 0, "Native read-only probe found devices and existing sessions");
                WinMmWavePlayback.WaveFormat format = new WinMmWavePlayback.WaveFormat
                    { Tag = 1, Channels = 1, SamplesPerSecond = 22050, BytesPerSecond = 44100, BlockAlign = 2, BitsPerSample = 16 };
                IntPtr unused;
                uint result = waveOutOpen(out unused, UInt32.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 1); // WAVE_FORMAT_QUERY
                check(result == 0, "WinMM accepts mono PCM 16-bit 22050 Hz in format-query mode (no stream)");
            }
            finally { CoreAudioDevices.Release(devices); CoreAudioDevices.Release(enumerator); }
        }

        [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr device, uint id, ref WinMmWavePlayback.WaveFormat format, IntPtr callback, IntPtr instance, uint flags);

        private sealed class FakePlayback : IWavePlayback
        {
            private readonly object gate = new object();
            private readonly List<byte> calls = new List<byte>();
            internal readonly AutoResetEvent Advance = new AutoResetEvent(false);
            internal int Active, MaximumActive;
            internal bool Throw;
            internal int Count { get { lock (gate) return calls.Count; } }
            internal byte At(int index) { lock (gate) return index < calls.Count ? calls[index] : (byte)255; }
            internal bool WaitCalls(int count) { return SpinWait.SpinUntil(delegate { return Count >= count; }, 2000); }
            public void Play(byte[] wav, WaitHandle cancelled)
            {
                int active = Interlocked.Increment(ref Active);
                if (active > MaximumActive) MaximumActive = active;
                try
                {
                    lock (gate) calls.Add(wav[0]);
                    if (Throw) throw new InvalidOperationException("Intentional fake playback failure.");
                    WaitHandle.WaitAny(new WaitHandle[] { cancelled, Advance });
                }
                finally { Interlocked.Decrement(ref Active); }
            }
        }
        private sealed class FakeDevices : IAttackAudioDevices
        {
            internal readonly List<IAttackAudioDevice> Items = new List<IAttackAudioDevice>();
            public IList<IAttackAudioDevice> OpenActiveDevices() { return new List<IAttackAudioDevice>(Items); }
        }
        private sealed class FakeDevice : IAttackAudioDevice
        {
            public string Id { get; set; }
            public bool IsDefault { get; set; }
            public bool Mute { get; set; }
            public float Volume { get; set; }
            internal readonly List<IAttackAudioSession> Sessions = new List<IAttackAudioSession>();
            public IList<IAttackAudioSession> OpenSessions() { return new List<IAttackAudioSession>(Sessions); }
            public void Dispose() { }
        }
        private sealed class FakeSession : IAttackAudioSession
        {
            public string Id { get; set; }
            public bool IsOwn { get; set; }
            private bool mute;
            internal bool ThrowAfterMuteOnce;
            public bool Mute
            {
                get { return mute; }
                set { mute = value; if (ThrowAfterMuteOnce) { ThrowAfterMuteOnce = false; throw new InvalidOperationException("Intentional fake mutation failure."); } }
            }
            public float Volume { get; set; }
            public void Dispose() { }
        }

#if AUDIO_CHECKS_STANDALONE
        [STAThread]
        private static int Main(string[] args)
        {
            int count = 0;
            Action<bool, string> check = delegate(bool ok, string message) { count++; if (!ok) throw new Exception(message); };
            Run(check);
            if (args.Length > 0 && args[0] == "--native") RunNativeReadOnly(check);
            Console.WriteLine("Audio checks passed: " + count);
            return 0;
        }
#endif
    }
}
