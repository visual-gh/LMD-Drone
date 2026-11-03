namespace LMDDrone.Audio;

/// <summary>
/// The drone's sound, worked out here: no such sound is in the game's banks. What a real one has and a synthesiser hasn't: its tone is the blades passing, soft sine harmonics that
/// waver as the air does, no two turns alike; and most of what's heard is air, noise that the blades chop in time. Pure sums, so it can run off the main thread and be listened to
/// outside the game.
/// </summary>
internal static class MotorSound
{
    public const int Rate = 44100;
    public const float LoopSeconds = 3f;

    /// <summary>Blades passing per second at the loop's own pitch: about a consumer drone's.</summary>
    private const int BladePass = 165;
    private const int Harmonics = 18;

    /// <summary>Harmonics fade above this, which is what keeps it from whining.</summary>
    private const float SoftAbove = 1400f;

    /// <summary>How much of the blades' tone there is against their air, and the slow breathing of each.</summary>
    private const float ToneShare = 0.55f;
    private const float AirShare = 0.75f;
    private const float Chop = 0.45f;

    private const float MotorLevel = 0.3f;
    private const float WindLevel = 0.3f;

    /// <summary>Samples crossfaded where a loop's noise joins up.</summary>
    private const int JoinSamples = Rate / 5;

    private const float Tau = MathF.PI * 2f;

    /// <summary>The motors: tone and chopped air, at a whole number of blade passes per loop, so it joins up.</summary>
    public static float[] Motor()
    {
        var count = (int)(Rate * LoopSeconds);
        var cycles = (int)(BladePass * LoopSeconds);
        var random = new Random(3);
        var tone = new float[count];
        var phases = new float[Harmonics];
        var amounts = new float[Harmonics];
        var wavers = new float[Harmonics];
        var waverPhases = new float[Harmonics];
        for (var k = 0; k < Harmonics; k++)
        {
            var number = k + 1;
            var hertz = number * (float)BladePass;
            phases[k] = (float)random.NextDouble() * Tau;
            amounts[k] = MathF.Pow(number, -1.15f) / (1f + hertz * hertz / (SoftAbove * SoftAbove));
            wavers[k] = 1 + random.Next(4, 22);
            waverPhases[k] = (float)random.NextDouble() * Tau;
        }

        var unbalanced = (float)random.NextDouble() * Tau;
        for (var i = 0; i < count; i++)
        {
            var t = (float)i / count;
            var angle = Tau * cycles * t;

            // The whole rotation wanders a little, as it does in moving air: whole cycles a loop, so it comes back.
            var wander = 0.35f * MathF.Sin(Tau * 5f * t + 1f) + 0.22f * MathF.Sin(Tau * 11f * t + 2.6f);
            var value = 0.12f * MathF.Sin(angle / 3f + unbalanced);
            for (var k = 0; k < Harmonics; k++)
            {
                var waver = 1f + 0.4f * MathF.Sin(Tau * wavers[k] * t + waverPhases[k]);
                value += amounts[k] * waver * MathF.Sin((k + 1) * (angle + wander) + phases[k]);
            }

            tone[i] = value;
        }

        var air = Noise(count, new Random(5), 0.075f, 0.3f);
        var toneScale = ToneShare / Rms(tone);
        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            var t = (float)i / count;
            var chopped = 1f - Chop * (0.5f + 0.5f * MathF.Sin(Tau * cycles * t));
            samples[i] = tone[i] * toneScale + air[i] * chopped * AirShare;
        }

        return Finish(samples, MotorLevel);
    }

    /// <summary>The rush of air going past, for speed: low and slow to change, with gusts.</summary>
    public static float[] Wind()
    {
        var count = (int)(Rate * LoopSeconds);
        var samples = Noise(count, new Random(9), 0.03f, 0.2f);
        for (var i = 0; i < count; i++)
        {
            var t = (float)i / count;
            samples[i] *= 0.75f + 0.17f * MathF.Sin(Tau * 2f * t) + 0.08f * MathF.Sin(Tau * 7f * t + 1.3f);
        }

        return Finish(samples, WindLevel);
    }

    /// <summary>The noise between two simple filters: low is how far down the body reaches, high how far up the hiss, as shares of the rate. Joined at the ends so it loops without a click.</summary>
    private static float[] Noise(int count, Random random, float low, float high)
    {
        var raw = new float[count + JoinSamples];
        var under = 0f;
        var over = 0f;
        var body = 0f;
        for (var i = 0; i < raw.Length; i++)
        {
            var white = (float)random.NextDouble() * 2f - 1f;
            over += (white - over) * high;
            under += (over - under) * low;
            body += (under - body) * 0.25f;

            // A tilt to the low end, as air has: what's left of the hiss, under what passes the low filter.
            raw[i] = (over - under) * 0.15f + body * 2.2f;
        }

        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            samples[i] = raw[i];
        }

        for (var i = 0; i < JoinSamples; i++)
        {
            var fade = (float)i / JoinSamples;
            samples[i] = raw[i] * fade + raw[count + i] * (1f - fade);
        }

        var rms = Rms(samples);
        for (var i = 0; i < count; i++)
        {
            samples[i] /= rms;
        }

        return samples;
    }

    public static float Rms(float[] samples)
    {
        var sum = 0.0;
        foreach (var sample in samples)
        {
            sum += sample * sample;
        }

        return (float)Math.Sqrt(sum / samples.Length);
    }

    /// <summary>To its level, and any peak rounded off, not clipped.</summary>
    private static float[] Finish(float[] samples, float level)
    {
        var rms = Rms(samples);
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = MathF.Tanh(samples[i] / rms * level);
        }

        return samples;
    }
}
