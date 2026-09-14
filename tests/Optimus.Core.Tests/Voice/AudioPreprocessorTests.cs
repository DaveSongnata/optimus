using System;
using Optimus.Core.Voice;
using Xunit;

namespace Optimus.Core.Tests.Voice
{
    public class AudioPreprocessorTests
    {
        private const int SampleRate = 16000;

        private static byte[] BuildWav(short[] samples, int sampleRate = SampleRate, int channels = 1, int bitsPerSample = 16)
        {
            int bytesPerSample = bitsPerSample / 8;
            int dataLength = samples.Length * bytesPerSample;
            byte[] wav = new byte[44 + dataLength];
            WriteTag(wav, 0, "RIFF");
            BitConverter.GetBytes(36 + dataLength).CopyTo(wav, 4);
            WriteTag(wav, 8, "WAVE");
            WriteTag(wav, 12, "fmt ");
            BitConverter.GetBytes(16).CopyTo(wav, 16);
            BitConverter.GetBytes((short)1).CopyTo(wav, 20);
            BitConverter.GetBytes((short)channels).CopyTo(wav, 22);
            BitConverter.GetBytes(sampleRate).CopyTo(wav, 24);
            BitConverter.GetBytes(sampleRate * channels * bytesPerSample).CopyTo(wav, 28);
            BitConverter.GetBytes((short)(channels * bytesPerSample)).CopyTo(wav, 32);
            BitConverter.GetBytes((short)bitsPerSample).CopyTo(wav, 34);
            WriteTag(wav, 36, "data");
            BitConverter.GetBytes(dataLength).CopyTo(wav, 40);
            Buffer.BlockCopy(samples, 0, wav, 44, dataLength);
            return wav;
        }

        private static void WriteTag(byte[] buffer, int offset, string tag)
        {
            for (int i = 0; i < tag.Length; i++) buffer[offset + i] = (byte)tag[i];
        }

        private static short[] Silence(int ms) => new short[ms * SampleRate / 1000];

        private static short[] Tone(int ms, short amplitude)
        {
            int n = ms * SampleRate / 1000;
            var samples = new short[n];
            for (int i = 0; i < n; i++)
                samples[i] = (short)(amplitude * Math.Sin(2 * Math.PI * 440 * i / SampleRate));
            return samples;
        }

        private static short[] Concat(params short[][] parts)
        {
            int total = 0;
            foreach (short[] p in parts) total += p.Length;
            var result = new short[total];
            int offset = 0;
            foreach (short[] p in parts) { Array.Copy(p, 0, result, offset, p.Length); offset += p.Length; }
            return result;
        }

        private static int SampleCountOf(byte[] wav) => (wav.Length - 44) / 2;

        [Fact]
        public void Trims_leading_and_trailing_silence()
        {
            short[] samples = Concat(Silence(2000), Tone(500, 10000), Silence(2000));
            byte[] wav = BuildWav(samples);

            byte[] trimmed = AudioPreprocessor.TrimAndNormalize(wav);

            // 5s of raw audio down to comfortably under 1s: the 500ms tone plus 150ms padding on
            // each side, nowhere near the original 4s of pure silence that surrounded it.
            Assert.True(SampleCountOf(trimmed) < SampleCountOf(wav));
            Assert.True(SampleCountOf(trimmed) < SampleRate);
        }

        [Fact]
        public void All_silence_audio_is_returned_unchanged()
        {
            byte[] wav = BuildWav(Silence(1000));

            byte[] result = AudioPreprocessor.TrimAndNormalize(wav);

            // Nothing above the threshold anywhere: better to hand the caller's own "áudio vazio"
            // check the untouched clip than to invent a zero-length one.
            Assert.Equal(wav, result);
        }

        [Fact]
        public void Non_mono_16_bit_audio_is_left_untouched()
        {
            // VoiceRecorder only ever produces mono 16-bit — anything else means this ran on audio it
            // was never meant to see, and guessing at it would be worse than doing nothing.
            byte[] stereoWav = BuildWav(Concat(Silence(500), Tone(500, 10000)), channels: 2);

            byte[] result = AudioPreprocessor.TrimAndNormalize(stereoWav);

            Assert.Equal(stereoWav, result);
        }

        [Fact]
        public void A_quiet_recording_is_amplified_toward_the_target_peak()
        {
            short[] quiet = Tone(500, 500); // far below the -3dBFS default target
            byte[] wav = BuildWav(quiet);

            byte[] result = AudioPreprocessor.TrimAndNormalize(wav);

            short[] outSamples = new short[SampleCountOf(result)];
            Buffer.BlockCopy(result, 44, outSamples, 0, outSamples.Length * 2);

            int inPeak = 0, outPeak = 0;
            foreach (short s in quiet) inPeak = Math.Max(inPeak, Math.Abs((int)s));
            foreach (short s in outSamples) outPeak = Math.Max(outPeak, Math.Abs((int)s));

            Assert.True(outPeak > inPeak);
        }

        [Fact]
        public void A_recording_already_loud_enough_is_never_attenuated()
        {
            short[] loud = Tone(500, short.MaxValue); // already at full scale
            byte[] wav = BuildWav(loud);

            byte[] result = AudioPreprocessor.TrimAndNormalize(wav);

            short[] outSamples = new short[SampleCountOf(result)];
            Buffer.BlockCopy(result, 44, outSamples, 0, outSamples.Length * 2);

            int inPeak = 0, outPeak = 0;
            foreach (short s in loud) inPeak = Math.Max(inPeak, Math.Abs((int)s));
            foreach (short s in outSamples) outPeak = Math.Max(outPeak, Math.Abs((int)s));

            Assert.True(outPeak <= inPeak);
        }

        [Fact]
        public void Too_short_to_be_a_wav_is_returned_as_is()
        {
            byte[] tiny = { 1, 2, 3 };
            Assert.Equal(tiny, AudioPreprocessor.TrimAndNormalize(tiny));
        }

        [Fact]
        public void Null_input_never_throws()
        {
            Assert.Empty(AudioPreprocessor.TrimAndNormalize(null!));
        }
    }
}
