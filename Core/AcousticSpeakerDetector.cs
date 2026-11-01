using System;
using System.Collections.Generic;
using System.Threading;

namespace LiveTranslatorOverlay.Core;

public class SpeakerIdentity
{
    public string Label { get; set; } = "Unknown speaker";
    public string Gender { get; set; } = "Unknown";
    public int SpeakerIndex { get; set; } = -1;
    public bool IsEstimate { get; set; }
    public float PitchHz { get; set; }
    public string ColorHex { get; set; } = "#A1A1AA";
}

/// <summary>
/// Conservative, session-local voice estimates for Whisper, which supplies no speaker IDs.
/// Pitch is not a biometric identity: ambiguous/mixed/unvoiced audio is left unassigned.
/// Use the diarized speech provider for word-level multi-speaker attribution.
/// </summary>
public static class AcousticSpeakerDetector
{
    private static readonly List<SpeakerProfile> _knownSpeakers = new();
    private static readonly object _lock = new();
    private static readonly string[] Colors = { "#38BDF8", "#FBBF24", "#A78BFA", "#34D399", "#FB7185", "#22D3EE", "#FB923C", "#E879F9" };
    private sealed class SpeakerProfile
    {
        public float AveragePitch { get; set; }
    }

    public static SpeakerIdentity DetectSpeaker(float[] pcm16k, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return new SpeakerIdentity();
        float pitch = EstimateChunkPitch(pcm16k);
        if (pitch <= 0) return new SpeakerIdentity();
        lock (_lock)
        {
            if (token.IsCancellationRequested) return new SpeakerIdentity();
            int closest = -1;
            double best = double.MaxValue, second = double.MaxValue;
            for (int i = 0; i < _knownSpeakers.Count; i++)
            {
                double distance = Math.Abs(Math.Log(pitch / _knownSpeakers[i].AveragePitch));
                if (distance < best) { second = best; best = distance; closest = i; }
                else second = Math.Min(second, distance);
            }
            // Compare relative pitch, without a male/female boundary or first-match bias.
            if (best <= Math.Log(1.22))
            {
                if (second - best < 0.04) return new SpeakerIdentity();
                _knownSpeakers[closest].AveragePitch = 0.8f * _knownSpeakers[closest].AveragePitch + 0.2f * pitch;
            }
            else
            {
                if (_knownSpeakers.Count >= Colors.Length) return new SpeakerIdentity();
                closest = _knownSpeakers.Count;
                _knownSpeakers.Add(new SpeakerProfile { AveragePitch = pitch });
            }
            return new SpeakerIdentity
            {
                Label = $"Speaker {closest + 1}", SpeakerIndex = closest, IsEstimate = true,
                PitchHz = pitch, ColorHex = Colors[closest]
            };
        }
    }

    public static void Reset() { lock (_lock) _knownSpeakers.Clear(); }

    private static float EstimateChunkPitch(float[] audio)
    {
        if (audio == null || audio.Length < 2800) return 0;
        const int frameSize = 1200, step = 800;
        var pitches = new List<float>();
        for (int offset = 0; offset <= audio.Length - frameSize; offset += step)
        {
            double energy = 0, sum = 0;
            for (int j = 0; j < frameSize; j++)
            {
                double sample = audio[offset + j];
                energy += sample * sample; sum += sample;
            }
            double mean = sum / frameSize;
            if (!double.IsFinite(energy) || energy - sum * mean < 0.04) continue;
            float pitch = EstimateFramePitch(audio, offset, frameSize, mean);
            if (pitch >= 95 && pitch <= 420) pitches.Add(pitch);
        }
        if (pitches.Count < 3) return 0;
        pitches.Sort();
        // A chunk containing substantially different pitch groups may contain multiple people.
        if (pitches[(int)((pitches.Count - 1) * 0.9)] / pitches[(int)((pitches.Count - 1) * 0.1)] > 1.35f) return 0;
        return pitches[pitches.Count / 2];
    }

    private static float EstimateFramePitch(float[] audio, int start, int length, double mean)
    {
        const int minLag = 16000 / 420, maxLag = 16000 / 95;
        var correlation = new double[maxLag + 1];
        double best = 0;
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double cross = 0, left = 0, right = 0;
            for (int j = 0; j < length - maxLag; j++)
            {
                double a = audio[start + j] - mean, b = audio[start + j + lag] - mean;
                cross += a * b; left += a * a; right += b * b;
            }
            correlation[lag] = left > 0 && right > 0 ? cross / Math.Sqrt(left * right) : 0;
            best = Math.Max(best, correlation[lag]);
        }
        if (best < 0.75) return 0;
        // Prefer the first strong peak instead of a later harmonic that halves the pitch.
        for (int lag = minLag + 1; lag < maxLag; lag++)
            if (correlation[lag] >= best * 0.95 && correlation[lag] >= correlation[lag - 1] && correlation[lag] >= correlation[lag + 1])
                return 16000f / lag;
        return 0;
    }
}
