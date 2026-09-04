using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;

namespace Optimus.Windows
{
    /// <summary>
    /// Speaks back a FIXED, small set of confirmations using pre-rendered WAV files bundled with the
    /// installer — never the operator's own installed TTS voices.
    ///
    /// <para>
    /// Davi rejected relying on <c>window.speechSynthesis</c> outright (2026-08-15): quality varies
    /// per machine (some install nothing but the oldest legacy Windows voice), and he wants the exact
    /// same result "cem por cento das vezes", not something that depends on what a given PC happens to
    /// have installed. The fix mirrors how the offline speech-to-text model is shipped: render once,
    /// bundle the output, never regenerate on the customer's machine.
    /// </para>
    /// <para>
    /// The WAV files were rendered ONCE, at build time, with Piper (MIT) — but Piper's phonemizer
    /// embeds eSpeak NG, which is GPL-3.0. That license covers the TOOL, not ITS OUTPUT: nothing here
    /// ships piper.exe or espeak-ng.dll, only the resulting audio, the same way exporting a PNG from
    /// GPL'd GIMP does not make the PNG GPL. See THIRD-PARTY-NOTICES.txt.
    /// </para>
    /// <para>
    /// pt-BR only for now — the whole feature is being demoed in Portuguese. A missing key plays
    /// nothing rather than throwing; a demo must never crash because a sound clip didn't ship.
    /// </para>
    /// </summary>
    public sealed class VoicePlayer
    {
        private readonly string _soundsDir;

        public VoicePlayer(string installDir)
        {
            _soundsDir = Path.Combine(installDir, "voice-sounds", "pt-BR");
        }

        /// <summary>Fire-and-forget: returns immediately, never blocks the caller (the STA/UI
        /// thread). Runs on a threadpool thread using PlaySync — SoundPlayer.Play() alone returns
        /// before playback finishes, and disposing it that early cuts the clip off mid-word.</summary>
        public void Play(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            string path = Path.Combine(_soundsDir, key + ".wav");
            if (!File.Exists(path)) return;

            Task.Run(() =>
            {
                try
                {
                    using var player = new SoundPlayer(path);
                    player.PlaySync();
                }
                catch (Exception) { /* a missing/locked audio device costs the confirmation, not the app */ }
            });
        }
    }
}
