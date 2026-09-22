using System;
using System.Collections.Generic;

namespace LiveTranslatorOverlay.Core
{
    public class SpeakerIdentity
    {
        public string Label { get; set; } = "Speaker";
        public string Gender { get; set; } = "Unknown"; // "Man", "Woman"
        public float PitchHz { get; set; }
        public string ColorHex { get; set; } = "#38BDF8";
    }

    /// <summary>
    /// Lightweight, zero-dependency acoustic pitch analyzer using Normalized Autocorrelation (NACF)
    /// to detect fundamental frequency (F0) and distinguish speaker gender (Man / Woman) and speaker IDs.
    /// </summary>
    public static class AcousticSpeakerDetector
    {
        private static readonly List<SpeakerProfile> _knownSpeakers = new();
        private static readonly object _lock = new();

        private class SpeakerProfile
        {
            public string Label { get; set; } = "";
            public string Gender { get; set; } = "";
            public string ColorHex { get; set; } = "#38BDF8";
            public float AveragePitch { get; set; }
            public int DetectionCount { get; set; }
        }

        /// <summary>
        /// Analyze 16kHz mono PCM float audio chunk and return speaker identity.
        /// </summary>
        public static SpeakerIdentity DetectSpeaker(float[] pcm16k)
        {
            float pitch = EstimateChunkPitch(pcm16k, 16000);

            lock (_lock)
            {
                if (pitch <= 0)
                {
                    // Pitch unvoiced or faint noise; return last active speaker or default
                    if (_knownSpeakers.Count > 0)
                    {
                        var last = _knownSpeakers[^1];
                        return new SpeakerIdentity
                        {
                            Label = last.Label,
                            Gender = last.Gender,
                            PitchHz = last.AveragePitch,
                            ColorHex = last.ColorHex
                        };
                    }

                    return new SpeakerIdentity
                    {
                        Label = "Speaker 1",
                        Gender = "Unknown",
                        PitchHz = 0,
                        ColorHex = "#38BDF8"
                    };
                }

                // Phonetic threshold: Male pitch avg ~110-140Hz, Female pitch avg ~180-240Hz
                bool isFemale = pitch >= 155f;
                string gender = isFemale ? "Woman" : "Man";

                SpeakerProfile? matched = null;
                foreach (var s in _knownSpeakers)
                {
                    if (s.Gender == gender && Math.Abs(s.AveragePitch - pitch) < 28f)
                    {
                        matched = s;
                        break;
                    }
                }

                if (matched == null)
                {
                    int countOfGender = 0;
                    foreach (var s in _knownSpeakers)
                    {
                        if (s.Gender == gender) countOfGender++;
                    }

                    int speakerNum = countOfGender + 1;
                    string label = $"{gender} {speakerNum}";

                    // Modern distinct colors
                    string color;
                    if (isFemale)
                    {
                        color = (speakerNum % 2 == 1) ? "#F472B6" : "#FB7185"; // Pink / Rose
                    }
                    else
                    {
                        color = (speakerNum % 2 == 1) ? "#38BDF8" : "#818CF8"; // Sky Blue / Indigo
                    }

                    matched = new SpeakerProfile
                    {
                        Label = label,
                        Gender = gender,
                        ColorHex = color,
                        AveragePitch = pitch,
                        DetectionCount = 1
                    };
                    _knownSpeakers.Add(matched);
                }
                else
                {
                    // Smooth rolling average pitch
                    matched.AveragePitch = (matched.AveragePitch * matched.DetectionCount + pitch) / (matched.DetectionCount + 1);
                    matched.DetectionCount++;
                }

                return new SpeakerIdentity
                {
                    Label = matched.Label,
                    Gender = matched.Gender,
                    PitchHz = pitch,
                    ColorHex = matched.ColorHex
                };
            }
        }

        public static void Reset()
        {
            lock (_lock)
            {
                _knownSpeakers.Clear();
            }
        }

        /// <summary>
        /// Estimate fundamental frequency (F0) across multiple voiced frames in the chunk.
        /// </summary>
        private static float EstimateChunkPitch(float[] audio, int sampleRate)
        {
            if (audio == null || audio.Length < 1600) return 0f;

            int frameSize = 1200; // 75ms window at 16kHz
            int stepSize = 800;   // 50ms step
            List<float> pitchEstimates = new List<float>();

            for (int i = 0; i <= audio.Length - frameSize; i += stepSize)
            {
                float frameEnergy = 0f;
                for (int j = 0; j < frameSize; j++)
                {
                    float s = audio[i + j];
                    frameEnergy += s * s;
                }

                // Skip quiet or unvoiced frames
                if (frameEnergy < 0.04f) continue;

                float pitch = EstimateFramePitch(audio, i, frameSize, sampleRate);
                if (pitch > 70f && pitch < 380f)
                {
                    pitchEstimates.Add(pitch);
                }
            }

            if (pitchEstimates.Count == 0) return 0f;

            // Use median pitch to filter out octave jumps
            pitchEstimates.Sort();
            return pitchEstimates[pitchEstimates.Count / 2];
        }

        /// <summary>
        /// Computes normalized autocorrelation for lag range [42, 220] (~73Hz to 380Hz).
        /// </summary>
        private static float EstimateFramePitch(float[] audio, int start, int length, int sampleRate)
        {
            int minLag = sampleRate / 380; // ~42 samples
            int maxLag = sampleRate / 73;  // ~219 samples

            if (length <= maxLag) return 0f;

            float r0 = 0f;
            for (int j = 0; j < length - maxLag; j++)
            {
                float s = audio[start + j];
                r0 += s * s;
            }

            if (r0 < 0.001f) return 0f;

            float bestCorr = -1f;
            int bestLag = 0;

            for (int lag = minLag; lag <= maxLag; lag++)
            {
                float corr = 0f;
                for (int j = 0; j < length - maxLag; j++)
                {
                    corr += audio[start + j] * audio[start + j + lag];
                }

                float normalized = corr / r0;
                if (normalized > bestCorr)
                {
                    bestCorr = normalized;
                    bestLag = lag;
                }
            }

            // Confidence threshold: correlation peak > 0.4 indicates strong harmonic periodicity
            if (bestCorr > 0.40f && bestLag > 0)
            {
                return (float)sampleRate / bestLag;
            }

            return 0f;
        }
    }
}
