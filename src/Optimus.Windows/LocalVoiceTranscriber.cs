using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Optimus.Core.Voice;
using Whisper.net;

namespace Optimus.Windows
{
    public sealed class TranscriptionResult
    {
        public bool Ok { get; set; }
        public string Text { get; set; } = "";
        public string Error { get; set; } = "";
    }

    /// <summary>
    /// Speech-to-text entirely on the operator's machine — whisper.cpp through Whisper.net, a
    /// multilingual ggml model shipped inside the installer. No API key, no network call, no account:
    /// Davi rejected the earlier cloud-Whisper design outright (2026-08-14, "não é para usar API, eu
    /// quero que funcione offline"), and this is the replacement.
    ///
    /// <para>
    /// The model file is looked up next to the add-in's own DLLs (same install folder every other
    /// bundled asset lives in — see <c>OptimusBridge.InstallDir()</c>), so once the installer has run
    /// there is nothing left to download, configure, or fail on a flaky venue Wi-Fi.
    /// </para>
    /// </summary>
    public sealed class LocalVoiceTranscriber : IDisposable
    {
        public const string ModelFileName = "ggml-model.bin";

        private WhisperFactory? _factory;
        private readonly string _modelPath;

        public LocalVoiceTranscriber(string modelDirectory)
        {
            _modelPath = Path.Combine(modelDirectory, ModelFileName);

            // Whisper.net's OWN native-library loader resolves "runtimes/win-x64/*.dll" relative to
            // wherever Whisper.net.dll ITSELF was loaded from — which, in this add-in, is our own
            // addon folder (loaded via AssemblyResolve, not the CorelDRAW.exe folder). No override is
            // needed once the DLLs actually sit at "<modelDirectory>/runtimes/win-x64/*.dll" (see
            // build-all.ps1) — confirmed by reproducing this exact host/plugin split locally before
            // shipping (2026-08-15). An earlier attempt set RuntimeOptions.LibraryPath here as a
            // workaround for a WRONG folder layout (an extra "native" segment); once the layout was
            // fixed the override was not only unnecessary, it risked fighting the loader's own
            // correct default resolution — removed.
        }

        public bool ModelAvailable => File.Exists(_modelPath);

        /// <summary>The exact path checked — surfaced so a "model missing" report can be root-caused
        /// from a log line instead of guessed at.</summary>
        public string ModelPath => _modelPath;

        public async Task<TranscriptionResult> TranscribeAsync(byte[] wavBytes, string language)
        {
            if (!ModelAvailable)
                return new TranscriptionResult { Ok = false, Error = "Modelo de voz não encontrado (" + ModelFileName + ")." };
            if (wavBytes == null || wavBytes.Length == 0)
                return new TranscriptionResult { Ok = false, Error = "Áudio vazio." };

            try
            {
                _factory ??= WhisperFactory.FromPath(_modelPath);

                // Silence at either end is whisper.cpp's own documented hallucination trigger
                // (ggml-org/whisper.cpp#1724) — Optimus records from "start" click to "stop" click
                // with no trimming, so this runs before the model ever sees the clip. A quiet mic also
                // reads as "weak evidence" to the decoder, hence the peak boost alongside the trim.
                byte[] prepared = AudioPreprocessor.TrimAndNormalize(wavBytes);

                using WhisperProcessor processor = _factory.CreateBuilder()
                    .WithLanguage(string.IsNullOrWhiteSpace(language) ? "auto" : language)
                    // The three anti-hallucination knobs whisper.cpp exposes but Whisper.net does not
                    // default on — their absence is called out by name in a real dictation app's own
                    // bug tracker (Envious-Labs-LLC/enviouswispr-windows#101) as the reason it invented
                    // filler text on quiet audio. Values match whisper.cpp's own CLI defaults.
                    .WithNoSpeechThreshold(0.6f)
                    .WithLogProbThreshold(-1.0f)
                    .WithEntropyThreshold(2.4f)
                    .Build();

                using var audio = new MemoryStream(prepared);
                var sb = new StringBuilder();
                await foreach (SegmentData segment in processor.ProcessAsync(audio))
                    sb.Append(segment.Text);

                return new TranscriptionResult { Ok = true, Text = sb.ToString().Trim() };
            }
            catch (Exception ex)
            {
                return new TranscriptionResult { Ok = false, Error = ex.Message };
            }
        }

        public void Dispose()
        {
            try { _factory?.Dispose(); } catch (Exception) { }
        }
    }
}
