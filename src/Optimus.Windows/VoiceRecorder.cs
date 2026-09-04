using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Optimus.Windows
{
    /// <summary>
    /// Records one utterance from the default microphone straight to a 16 kHz mono 16-bit PCM WAV
    /// stream — the format whisper.cpp expects, so no transcoding step (and no browser audio codec)
    /// sits between the mic and the model.
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

        /// <summary>Starts capturing. Throws if no recording device is available (caller decides how
        /// to tell the operator — a missing mic is a real failure mode at a trade show).</summary>
        public void Start()
        {
            if (IsRecording) return;
            if (WaveInEvent.DeviceCount <= 0) throw new InvalidOperationException("Nenhum microfone encontrado.");

            _buffer = new MemoryStream();
            _waveIn = new WaveInEvent { WaveFormat = new WaveFormat(SampleRate, 16, 1) };
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
