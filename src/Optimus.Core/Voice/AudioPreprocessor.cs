using System;

namespace Optimus.Core.Voice
{
    /// <summary>
    /// Trims leading/trailing silence and peak-normalizes a 16-bit PCM mono WAV before it reaches
    /// whisper.cpp.
    ///
    /// <para>
    /// WHY. whisper.cpp hallucinates on low-evidence audio BY DESIGN — its own maintainers describe
    /// silence/near-silence padding as the documented trigger for invented, repeated text
    /// (ggml-org/whisper.cpp#1724), and reading Whisper.net's OWN WaveParser and its official NAudio
    /// sample confirms neither does ANY of this: no trim, no normalization, no VAD — the whole burden
    /// sits on the caller (researched 2026-09). Optimus records from "start" click to "stop" click
    /// with no silence handling at either end, which is exactly the pattern reported to produce a
    /// "reconhecimento ficou ruim" complaint.
    /// </para>
    /// <para>
    /// Pure math over the PCM samples — no COM, no Windows API, no third-party dependency — so it is
    /// the one part of the voice pipeline testable without a microphone.
    /// </para>
    /// </summary>
    public static class AudioPreprocessor
    {
        private const int BytesPerSample = 2; // 16-bit PCM — the only format VoiceRecorder produces.

        /// <summary>
        /// Trims silence from both ends (with a small padding so speech isn't clipped) and, if the
        /// loudest sample is quieter than <paramref name="targetPeakDbfs"/>, amplifies the whole clip
        /// up to that peak. Never attenuates an already-strong recording, and returns the ORIGINAL
        /// bytes unchanged whenever the WAV isn't the shape it expects (wrong format, all silence) —
        /// doing nothing is always safer than guessing at audio it cannot parse.
        /// </summary>
        public static byte[] TrimAndNormalize(byte[] wav, double silenceThresholdDbfs = -40, int padMs = 150, double targetPeakDbfs = -3)
        {
            if (wav == null || wav.Length < 44) return wav ?? Array.Empty<byte>();
            if (!TryFindDataChunk(wav, out int sampleRate, out int channels, out int bitsPerSample, out int dataOffset, out int dataLength))
                return wav;
            if (channels != 1 || bitsPerSample != 16) return wav; // only VoiceRecorder's own fixed format

            short[] samples = new short[dataLength / BytesPerSample];
            Buffer.BlockCopy(wav, dataOffset, samples, 0, samples.Length * BytesPerSample);
            if (samples.Length == 0) return wav;

            int windowSamples = Math.Max(1, sampleRate / 50); // 20 ms windows
            int start = FindEdge(samples, windowSamples, silenceThresholdDbfs, fromStart: true);
            int end = FindEdge(samples, windowSamples, silenceThresholdDbfs, fromStart: false);
            if (start < 0 || end < 0 || start >= end)
                return wav; // nothing above the threshold anywhere — let the caller's own empty-audio check handle it

            int padSamples = (int)((long)padMs * sampleRate / 1000);
            int trimStart = Math.Max(0, start - padSamples);
            int trimEnd = Math.Min(samples.Length, end + padSamples);

            short[] trimmed = new short[trimEnd - trimStart];
            Array.Copy(samples, trimStart, trimmed, 0, trimmed.Length);

            NormalizePeak(trimmed, targetPeakDbfs);

            return BuildWav(trimmed, sampleRate, channels, bitsPerSample);
        }

        /// <summary>Sample index of the first (or, scanning backwards, the last) 20 ms window whose RMS
        /// is at or above the threshold — or -1 when every window is quieter than it.</summary>
        private static int FindEdge(short[] samples, int windowSamples, double thresholdDbfs, bool fromStart)
        {
            int windows = (samples.Length + windowSamples - 1) / windowSamples;
            if (fromStart)
            {
                for (int w = 0; w < windows; w++)
                {
                    int s = w * windowSamples;
                    if (WindowRmsDbfs(samples, s, windowSamples) >= thresholdDbfs) return s;
                }
            }
            else
            {
                for (int w = windows - 1; w >= 0; w--)
                {
                    int s = w * windowSamples;
                    if (WindowRmsDbfs(samples, s, windowSamples) >= thresholdDbfs)
                        return Math.Min(samples.Length, s + windowSamples);
                }
            }
            return -1;
        }

        private static double WindowRmsDbfs(short[] samples, int start, int count)
        {
            int end = Math.Min(start + count, samples.Length);
            int n = end - start;
            if (n <= 0) return double.NegativeInfinity;

            long sumSquares = 0;
            for (int i = start; i < end; i++) sumSquares += (long)samples[i] * samples[i];
            double rms = Math.Sqrt(sumSquares / (double)n);
            return rms <= 0 ? double.NegativeInfinity : 20.0 * Math.Log10(rms / short.MaxValue);
        }

        /// <summary>Boosts the clip so its loudest sample reaches <paramref name="targetPeakDbfs"/> —
        /// only ever UP. A recording already at or above that peak is left alone: turning it down
        /// would not help whisper and risks changing behaviour for a clip that was already fine.</summary>
        private static void NormalizePeak(short[] samples, double targetPeakDbfs)
        {
            int peak = 0;
            foreach (short s in samples) { int a = Math.Abs((int)s); if (a > peak) peak = a; }
            if (peak == 0) return; // pure digital silence — nothing to scale

            double targetPeakLinear = short.MaxValue * Math.Pow(10, targetPeakDbfs / 20.0);
            double gain = targetPeakLinear / peak;
            if (gain <= 1.0) return;

            for (int i = 0; i < samples.Length; i++)
            {
                double v = samples[i] * gain;
                samples[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, v));
            }
        }

        /// <summary>Walks RIFF chunks looking for "fmt " and "data" — a hand-rolled reader instead of a
        /// dependency because this needs to run in Optimus.Core (netstandard2.0, no NAudio reference)
        /// to stay pure, testable logic per the project's architecture rule.</summary>
        private static bool TryFindDataChunk(byte[] wav, out int sampleRate, out int channels, out int bitsPerSample, out int dataOffset, out int dataLength)
        {
            sampleRate = 0; channels = 0; bitsPerSample = 0; dataOffset = 0; dataLength = 0;
            if (wav.Length < 12 || !Match(wav, 0, "RIFF") || !Match(wav, 8, "WAVE")) return false;

            int pos = 12;
            bool haveFmt = false;
            while (pos + 8 <= wav.Length)
            {
                int size = BitConverter.ToInt32(wav, pos + 4);
                int bodyStart = pos + 8;
                if (size < 0 || bodyStart + size > wav.Length) return false; // malformed — pass through untouched

                if (Match(wav, pos, "fmt ") && size >= 16)
                {
                    channels = BitConverter.ToInt16(wav, bodyStart + 2);
                    sampleRate = BitConverter.ToInt32(wav, bodyStart + 4);
                    bitsPerSample = BitConverter.ToInt16(wav, bodyStart + 14);
                    haveFmt = true;
                }
                else if (Match(wav, pos, "data"))
                {
                    dataOffset = bodyStart;
                    dataLength = Math.Min(size, wav.Length - bodyStart);
                    return haveFmt;
                }

                pos = bodyStart + size + (size % 2); // chunks are word-aligned
            }
            return false;
        }

        private static bool Match(byte[] data, int offset, string tag)
        {
            for (int i = 0; i < tag.Length; i++)
                if (offset + i >= data.Length || data[offset + i] != (byte)tag[i]) return false;
            return true;
        }

        private static byte[] BuildWav(short[] samples, int sampleRate, int channels, int bitsPerSample)
        {
            int dataLength = samples.Length * BytesPerSample;
            int byteRate = sampleRate * channels * bitsPerSample / 8;
            short blockAlign = (short)(channels * bitsPerSample / 8);

            byte[] result = new byte[44 + dataLength];
            WriteTag(result, 0, "RIFF");
            BitConverter.GetBytes(36 + dataLength).CopyTo(result, 4);
            WriteTag(result, 8, "WAVE");
            WriteTag(result, 12, "fmt ");
            BitConverter.GetBytes(16).CopyTo(result, 16);
            BitConverter.GetBytes((short)1).CopyTo(result, 20); // PCM
            BitConverter.GetBytes((short)channels).CopyTo(result, 22);
            BitConverter.GetBytes(sampleRate).CopyTo(result, 24);
            BitConverter.GetBytes(byteRate).CopyTo(result, 28);
            BitConverter.GetBytes(blockAlign).CopyTo(result, 32);
            BitConverter.GetBytes((short)bitsPerSample).CopyTo(result, 34);
            WriteTag(result, 36, "data");
            BitConverter.GetBytes(dataLength).CopyTo(result, 40);
            Buffer.BlockCopy(samples, 0, result, 44, dataLength);
            return result;
        }

        private static void WriteTag(byte[] buffer, int offset, string tag)
        {
            for (int i = 0; i < tag.Length; i++) buffer[offset + i] = (byte)tag[i];
        }
    }
}
