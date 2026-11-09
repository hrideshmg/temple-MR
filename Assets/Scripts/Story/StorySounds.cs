using UnityEngine;

/// <summary>
/// Sound effects for the story and the puzzle, synthesised once at runtime so the project needs no
/// audio assets for them: a rising shimmer and a soft bell. (The collapse plays KovilLens's cannon
/// blast, from TempleDemolition.)
/// </summary>
public static class StorySounds
{
    private static AudioClip _shimmer;
    private static AudioClip _chime;

    private static int Rate => AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;


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
