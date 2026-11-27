using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Bubliklyuk
{
    // Playback is confined to one worker. Replacing a request first cancels the old
    // request; the next one cannot start until the previous native handle is closed.
    internal sealed class DictationPlayer : IDisposable
    {
        private readonly object gate = new object();
        private readonly Action<Exception> failure;
        private readonly Func<string, byte[]> readResource;
        private readonly IWavePlayback playback;
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private Thread worker;
        private Request pending;
        private Request active;
        private string phrase;
        private bool disposed;

        internal DictationPlayer(Action<Exception> failure)
            : this(failure, ReadEmbeddedResource, new WinMmWavePlayback()) { }

        // Tests inject both dependencies, so they never open an audio device.
        internal DictationPlayer(Action<Exception> failure, Func<string, byte[]> readResource, IWavePlayback playback)
        {
            if (readResource == null || playback == null) throw new ArgumentNullException();
            this.failure = failure;
            this.readResource = readResource;
            this.playback = playback;
        }

        internal void Start(string resourceName, bool playIntro)
        {
            if (String.IsNullOrEmpty(resourceName)) throw new ArgumentException("Dictation resource is required.");
            lock (gate)
            {
                if (disposed) return;
                phrase = resourceName;
                Enqueue(resourceName, playIntro);
            }
        }

        internal void Repeat()
        {
            lock (gate)
            {
                if (!disposed && phrase != null) Enqueue(phrase, false);
            }
        }

        private void Enqueue(string resourceName, bool playIntro)
        {
            CancelRequests();
            pending = new Request(resourceName, playIntro);
            if (worker == null)
            {
                worker = new Thread(Run) { IsBackground = true, Name = "Glukoboy dictation" };
                worker.Start();
            }
            wake.Set();
        }

        internal void Stop()
        {
            lock (gate)
            {
                phrase = null;
                CancelRequests();
            }
        }

        private void CancelRequests()
        {
            if (active != null) active.Cancel.Set();
            if (pending != null)
            {
                pending.Cancel.Dispose();
                pending = null;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                phrase = null;
                CancelRequests();
                if (worker == null) wake.Dispose();
                else wake.Set(); // The worker owns event/native-resource cleanup.
            }
        }

        private void Run()
        {
            Dictionary<string, byte[]> cache = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            try
            {
                for (;;)
                {
                    Request request;
                    lock (gate)
                    {
                        if (disposed) return;
                        request = pending;
                        pending = null;
                        active = request;
                    }
                    if (request == null) { wake.WaitOne(); continue; }
                    try
                    {
                        byte[] audio = Load(cache, request.Resource);
                        if (request.Intro && !request.Cancel.WaitOne(0))
                            playback.Play(Load(cache, "dictation-intro.wav"), request.Cancel);
                        // Pauses between words and the final five seconds are in the WAV.
                        while (!request.Cancel.WaitOne(0)) playback.Play(audio, request.Cancel);
                    }
                    catch (Exception ex)
                    {
                        if (!request.Cancel.WaitOne(0))
                        {
                            Diag.Log("DictationPlayer.Play", ex);
                            try { if (failure != null) failure(ex); }
                            catch (Exception callbackError) { Diag.Log("DictationPlayer.FailureCallback", callbackError); }
                        }
                    }
                    finally
                    {
                        lock (gate)
                        {
                            if (active == request) active = null;
                            request.Cancel.Dispose();
                        }
                    }
                }
            }
            finally { wake.Dispose(); }
        }

        private byte[] Load(Dictionary<string, byte[]> cache, string name)
        {
            byte[] data;
            if (!cache.TryGetValue(name, out data))
            {
                data = readResource(name);
                if (data == null || data.Length == 0) throw new InvalidDataException("Empty dictation resource: " + name);
                cache.Add(name, data);
            }
            return data;
        }

        private static byte[] ReadEmbeddedResource(string name)
        {
            using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (input == null) throw new FileNotFoundException("Dictation resource is missing.", name);
                using (MemoryStream output = new MemoryStream())
                {
                    input.CopyTo(output);
                    return output.ToArray();
                }
            }
        }

        private sealed class Request
        {
            internal readonly string Resource;
            internal readonly bool Intro;
            internal readonly ManualResetEvent Cancel = new ManualResetEvent(false);
            internal Request(string resource, bool intro) { Resource = resource; Intro = intro; }
        }
    }

    internal interface IWavePlayback
    {
        // Returns only after completion/cancellation and release of the native device.
        void Play(byte[] wav, WaitHandle cancelled);
    }

    internal sealed class WinMmWavePlayback : IWavePlayback
    {
        public void Play(byte[] wav, WaitHandle cancelled)
        {
            WaveFormat format;
            byte[] samples = ReadPcm(wav, out format);
            if (cancelled.WaitOne(0)) return;
            IntPtr device = IntPtr.Zero, header = IntPtr.Zero, data = IntPtr.Zero;
            bool prepared = false;
            int headerSize = Marshal.SizeOf(typeof(WaveHeader));
            using (AutoResetEvent completed = new AutoResetEvent(false))
            {
                try
                {
                    Check(waveOutOpen(out device, UInt32.MaxValue, ref format,
                        completed.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, 0x50000), "waveOutOpen");
                    if (cancelled.WaitOne(0)) return;
                    data = Marshal.AllocHGlobal(samples.Length);
                    Marshal.Copy(samples, 0, data, samples.Length);
                    header = Marshal.AllocHGlobal(headerSize);
                    WaveHeader value = new WaveHeader { Data = data, BufferLength = (uint)samples.Length };
                    Marshal.StructureToPtr(value, header, false);
                    Check(waveOutPrepareHeader(device, header, (uint)headerSize), "waveOutPrepareHeader");
                    prepared = true;
                    if (cancelled.WaitOne(0)) return;
                    Check(waveOutWrite(device, header, (uint)headerSize), "waveOutWrite");
                    WaitHandle[] signals = new WaitHandle[] { cancelled, completed };
                    // Event callbacks may include state changes other than completion.
                    while (!cancelled.WaitOne(0))
                    {
                        value = (WaveHeader)Marshal.PtrToStructure(header, typeof(WaveHeader));
                        if ((value.Flags & 1) != 0) break; // WHDR_DONE
                        WaitHandle.WaitAny(signals);
                    }
                }
                finally
                {
                    if (device != IntPtr.Zero)
                    {
                        // Reset returns every outstanding buffer before we free its memory.
                        uint reset = waveOutReset(device);
                        uint unprepare = prepared ? waveOutUnprepareHeader(device, header, (uint)headerSize) : 0;
                        uint close = waveOutClose(device);
                        // A broken driver must not be handed freed memory still in use.
                        if (unprepare != 0 || close != 0)
                        {
                            header = IntPtr.Zero; data = IntPtr.Zero;
                            // If close failed, the driver might still signal this HANDLE.
                            // Keep it valid until process exit rather than reuse it.
                            if (close != 0) completed.SafeWaitHandle.SetHandleAsInvalid();
                        }
                        if (reset != 0 || unprepare != 0 || close != 0)
                        {
                            if (header != IntPtr.Zero) { Marshal.FreeHGlobal(header); header = IntPtr.Zero; }
                            if (data != IntPtr.Zero) { Marshal.FreeHGlobal(data); data = IntPtr.Zero; }
                            throw new InvalidOperationException("WinMM cleanup codes: " + reset + ", " + unprepare + ", " + close);
                        }
                    }
                    if (header != IntPtr.Zero) Marshal.FreeHGlobal(header);
                    if (data != IntPtr.Zero) Marshal.FreeHGlobal(data);
                }
            }
        }

        internal static byte[] ReadPcm(byte[] wav, out WaveFormat format)
        {
            format = new WaveFormat();
            if (wav == null) throw new InvalidDataException("WAV data is missing.");
            byte[] samples = null;
            bool foundFormat = false;
            using (BinaryReader input = new BinaryReader(new MemoryStream(wav, false), Encoding.ASCII))
            {
                if (wav.Length < 12 || new string(input.ReadChars(4)) != "RIFF") throw new InvalidDataException("Invalid WAV header.");
                uint riffSize = input.ReadUInt32();
                if ((long)riffSize + 8 != wav.Length || new string(input.ReadChars(4)) != "WAVE")
                    throw new InvalidDataException("Invalid WAV container.");
                while (input.BaseStream.Position + 8 <= wav.Length)
                {
                    string chunk = new string(input.ReadChars(4));
                    uint size = input.ReadUInt32();
                    long end = input.BaseStream.Position + size;
                    if (end > wav.Length) throw new InvalidDataException("Truncated WAV chunk.");
                    if (chunk == "fmt ")
                    {
                        if (size < 16) throw new InvalidDataException("Invalid WAV format chunk.");
                        format.Tag = input.ReadUInt16(); format.Channels = input.ReadUInt16();
                        format.SamplesPerSecond = input.ReadUInt32(); format.BytesPerSecond = input.ReadUInt32();
                        format.BlockAlign = input.ReadUInt16(); format.BitsPerSample = input.ReadUInt16();
                        foundFormat = true;
                    }
                    else if (chunk == "data") samples = input.ReadBytes(checked((int)size));
                    input.BaseStream.Position = end + (size & 1);
                }
            }
            if (!foundFormat || format.Tag != 1 || format.Channels != 1 || format.SamplesPerSecond != 22050 ||
                format.BitsPerSample != 16 || format.BlockAlign != 2 || format.BytesPerSecond != 44100 ||
                samples == null || samples.Length == 0 || (samples.Length % 2) != 0)
                throw new InvalidDataException("Dictation must be mono PCM, 16 bit, 22050 Hz.");
            return samples;
        }

        private static void Check(uint result, string operation)
        {
            if (result != 0) throw new InvalidOperationException(operation + " failed: " + result);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        internal struct WaveFormat
        {
            internal ushort Tag, Channels;
            internal uint SamplesPerSecond, BytesPerSecond;
            internal ushort BlockAlign, BitsPerSample, ExtraSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHeader
        {
            internal IntPtr Data;
            internal uint BufferLength, BytesRecorded;
            internal IntPtr User;
            internal uint Flags, Loops;
            internal IntPtr Next, Reserved;
        }

        // Native layouts and event semantics: Microsoft mmeapi.h / WAVEFORMATEX / WAVEHDR.
        // https://learn.microsoft.com/windows/win32/api/mmeapi/nf-mmeapi-waveoutopen
        // https://learn.microsoft.com/windows/win32/api/mmeapi/nf-mmeapi-waveoutreset
        [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr device, uint id, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr device);
        [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr device);
    }
}
