using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Optimus.Windows
{
    /// <summary>One recording device, as the operator would recognise it.</summary>
    public sealed class VoiceDeviceInfo
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// Records one utterance straight to a 16 kHz mono 16-bit PCM WAV stream — the format whisper.cpp
    /// expects, so no transcoding step (and no browser audio codec) sits between the mic and the
    /// model.
    ///
    /// <para>
    /// Capture happens in the C# host, not the WebView2 page: recording here, instead of via the
    /// page's <c>MediaRecorder</c>, means the audio never needs decoding from WebM/Opus before
    /// reaching whisper.cpp — one less moving part for a feature that has to work on a trade-show
    /// floor with no retry.
    /// </para>
    /// </summary>
    public sealed class VoiceRecorder : IDisposable
    {
        private const int SampleRate = 16000;

        private WaveInEvent? _waveIn;
        private MemoryStream? _buffer;
        private WaveFileWriter? _writer;
        private TaskCompletionSource<byte[]>? _stopped;

        public bool IsRecording { get; private set; }

        /// <summary>Every recording device Windows currently exposes — enumerated fresh on every call,
        /// never cached: a USB headset plugged in mid-session must show up without a restart.</summary>
        public static List<VoiceDeviceInfo> ListDevices()
        {
            var devices = new List<VoiceDeviceInfo>();
            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                try { devices.Add(new VoiceDeviceInfo { Index = i, Name = WaveInEvent.GetCapabilities(i).ProductName }); }
                catch (Exception) { /* one unreadable device never hides the rest */ }
            }
            return devices;
        }

        /// <summary>
        /// Resolves a saved device NAME back to today's index. Device numbers are assigned by Windows
        /// in enumeration order and shift whenever something is plugged, unplugged, or the machine
        /// reboots with a different default — storing the index itself would silently start recording
        /// from the wrong microphone. Falls back to the system default (0) when the name is blank or
        /// no longer present, rather than failing to record at all.
        /// </summary>
        public static int ResolveDeviceIndex(string savedDeviceName)
        {
            if (string.IsNullOrWhiteSpace(savedDeviceName)) return 0;
            foreach (VoiceDeviceInfo d in ListDevices())
                if (string.Equals(d.Name, savedDeviceName, StringComparison.OrdinalIgnoreCase)) return d.Index;
            return 0;
        }

        /// <summary>Starts capturing from <paramref name="deviceIndex"/> (0 = system default). Throws
        /// if no recording device is available (caller decides how to tell the operator — a missing
        /// mic is a real failure mode at a trade show).</summary>
        public void Start(int deviceIndex = 0)
        {
            if (IsRecording) return;
            if (WaveInEvent.DeviceCount <= 0) throw new InvalidOperationException("Nenhum microfone encontrado.");
            if (deviceIndex < 0 || deviceIndex >= WaveInEvent.DeviceCount) deviceIndex = 0;

            _buffer = new MemoryStream();
            _waveIn = new WaveInEvent { DeviceNumber = deviceIndex, WaveFormat = new WaveFormat(SampleRate, 16, 1) };
            _writer = new WaveFileWriter(_buffer, _waveIn.WaveFormat);
            _stopped = new TaskCompletionSource<byte[]>();

            _waveIn.DataAvailable += (_, e) => { try { _writer?.Write(e.Buffer, 0, e.BytesRecorded); } catch (Exception) { } };
            _waveIn.RecordingStopped += (_, _) =>
            {
                try
                {
                    _writer?.Flush();
                    _stopped?.TrySetResult(_buffer?.ToArray() ?? Array.Empty<byte>());
                }
                catch (Exception ex) { _stopped?.TrySetException(ex); }
            };

            _waveIn.StartRecording();
            IsRecording = true;
        }

        /// <summary>Stops capturing and returns the complete WAV bytes recorded so far.</summary>
        public async Task<byte[]> StopAsync()
        {
            if (!IsRecording || _waveIn == null || _stopped == null) return Array.Empty<byte>();
            IsRecording = false;
            _waveIn.StopRecording();
            byte[] wav = await _stopped.Task.ConfigureAwait(false);
            CleanUp();
            return wav;
        }

        private void CleanUp()
        {
            try { _writer?.Dispose(); } catch (Exception) { }
            try { _waveIn?.Dispose(); } catch (Exception) { }
            _writer = null;
            _waveIn = null;
            _buffer = null;
        }

        public void Dispose() => CleanUp();
    }
}
