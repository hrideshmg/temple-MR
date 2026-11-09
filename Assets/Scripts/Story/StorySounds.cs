using UnityEngine;

/// <summary>
/// Sound effects for the story sequence, synthesised once at runtime so the project needs no audio
/// assets for them: a deep rumble for the collapse, a rising shimmer for the rebuild and a soft
/// bell for each part of the temple as it is named.
/// </summary>
public static class StorySounds
{
    private static AudioClip _rumble;
    private static AudioClip _shimmer;
    private static AudioClip _chime;

    private static int Rate => AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;

    /// <summary>A heavy thump followed by a long, crackling rumble.</summary>
    public static AudioClip Rumble => _rumble != null ? _rumble : _rumble = Build("StoryRumble", 4f, RumbleSample);

    /// <summary>A rising, sparkling swell that lasts about as long as the rebuild.</summary>
    public static AudioClip Shimmer => _shimmer != null ? _shimmer : _shimmer = Build("StoryShimmer", 2.8f, ShimmerSample);

    /// <summary>A short temple-bell tone.</summary>
    public static AudioClip Chime => _chime != null ? _chime : _chime = Build("StoryChime", 2.2f, ChimeSample);

    private delegate float Sample(float t, ref State s);

    private struct State
    {
        public System.Random Rng;
        public float Brown;
        public float Low;
        public float Crackle;
    }

    private static AudioClip Build(string name, float seconds, Sample sample)
    {
        int rate = Rate;
        int n = Mathf.CeilToInt(seconds * rate);
        var data = new float[n];
        var state = new State { Rng = new System.Random(1234) };
        float peak = 1e-4f;
        for (int i = 0; i < n; i++)
        {
            data[i] = sample(i / (float)rate, ref state);
            peak = Mathf.Max(peak, Mathf.Abs(data[i]));
        }
        float gain = 0.9f / peak;
        for (int i = 0; i < n; i++)
        {
            data[i] *= gain;
        }
        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static float Noise(ref State s)
    {
        return (float)(s.Rng.NextDouble() * 2.0 - 1.0);
    }

    private static float RumbleSample(float t, ref State s)
    {
        // Brown noise, low-passed again, for the body of the rumble.
        s.Brown = Mathf.Clamp(s.Brown + Noise(ref s) * 0.02f, -1f, 1f) * 0.998f;
        s.Low += (s.Brown - s.Low) * 0.05f;
        float body = s.Low * Mathf.Min(1f, t / 0.04f) * Mathf.Exp(-t / 1.4f);

        // The initial impact: a sine sweeping down from 70 to 30 Hz.
        float freq = Mathf.Lerp(70f, 30f, Mathf.Clamp01(t / 0.6f));
        float thump = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t / 0.35f) * 0.8f;

        // Sparse cracks of falling stone.
        if (s.Rng.NextDouble() < 0.0009 * Mathf.Exp(-t / 1.2f))
        {
            s.Crackle = 1f;
        }
        s.Crackle *= 0.995f;
        float crackle = Noise(ref s) * s.Crackle * 0.25f;

        return body * 3f + thump + crackle;
    }

    private static float ShimmerSample(float t, ref State s)
    {
        const float length = 2.8f;
        float swell = Mathf.SmoothStep(0f, 1f, t / (length * 0.7f)) * Mathf.Clamp01((length - t) / 0.6f);
        float chord = 0f;
        float[] notes = { 261.6f, 392f, 523.3f, 659.3f, 784f, 1046.5f };
        for (int k = 0; k < notes.Length; k++)
        {
            float vibrato = 1f + 0.004f * Mathf.Sin(2f * Mathf.PI * (4.5f + k) * t);
            // Higher notes enter later, so the sound climbs as the temple rises.
            float enter = Mathf.Clamp01((t - k * 0.25f) / 0.6f);
            chord += Mathf.Sin(2f * Mathf.PI * notes[k] * vibrato * t) * enter / (1f + k * 0.4f);
        }
        // A breathy rising whoosh underneath.
        s.Low += (Noise(ref s) - s.Low) * Mathf.Lerp(0.02f, 0.2f, t / length);
        return (chord * 0.35f + s.Low * 0.5f) * swell;
    }

    private static float ChimeSample(float t, ref State s)
    {
        float attack = Mathf.Min(1f, t / 0.005f);
        // Bell-like inharmonic partials, the higher ones dying away faster.
        return attack * (
            Mathf.Sin(2f * Mathf.PI * 880f * t) * Mathf.Exp(-t / 0.9f)
            + 0.5f * Mathf.Sin(2f * Mathf.PI * 1760f * 1.003f * t) * Mathf.Exp(-t / 0.5f)
            + 0.3f * Mathf.Sin(2f * Mathf.PI * 2637f * t) * Mathf.Exp(-t / 0.3f)
            + 0.2f * Mathf.Sin(2f * Mathf.PI * 440f * t) * Mathf.Exp(-t / 1.4f));
    }
}
