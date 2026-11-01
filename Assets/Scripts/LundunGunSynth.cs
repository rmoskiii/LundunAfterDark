using System;

/// Sound design for the guns, generated in code (no audio files, all original). Pure maths, no Unity types,
/// so it can be tested anywhere; LundunGunAudio turns the samples into AudioClips.
/// Every shot is made of: a crack (the supersonic snap), a burst of filtered noise (the blast), a falling
/// low tone (the body / thump), a darker noise tail and a couple of echoes (London streets bounce sound back).
public static class LundunGunSynth
{
    public const int Rate = 44100;

    public struct Shot
    {
        public float length;                 // seconds
        public float crack;                  // snap level
        public float blast, blastDecay, blastLow, blastHigh;   // noise burst: level, decay (s), band (Hz)
        public float body, bodyFrom, bodyTo, bodyDecay;        // thump: level, pitch sweep (Hz), decay (s)
        public float tail, tailDecay, tailLow;                 // dark rumble afterwards
        public float echo1, echo2, echoGain;                   // seconds, seconds, level
        public int seed;
    }

    // ------------------------------------------------------------------ the four guns
    public static readonly Shot Pistol = new Shot
    {
        length = 0.5f, crack = 0.9f, blast = 1f, blastDecay = 0.022f, blastLow = 5500f, blastHigh = 350f,
        body = 0.55f, bodyFrom = 210f, bodyTo = 85f, bodyDecay = 0.05f, tail = 0.22f, tailDecay = 0.16f, tailLow = 900f,
        echo1 = 0.085f, echo2 = 0.16f, echoGain = 0.22f, seed = 11
    };
    public static readonly Shot Smg = new Shot
    {
        length = 0.28f, crack = 0.7f, blast = 0.9f, blastDecay = 0.014f, blastLow = 7000f, blastHigh = 600f,
        body = 0.35f, bodyFrom = 260f, bodyTo = 130f, bodyDecay = 0.03f, tail = 0.12f, tailDecay = 0.07f, tailLow = 1200f,
        echo1 = 0.07f, echo2 = 0.13f, echoGain = 0.12f, seed = 23
    };
    public static readonly Shot Carbine = new Shot
    {
        length = 0.7f, crack = 1f, blast = 1f, blastDecay = 0.032f, blastLow = 4800f, blastHigh = 250f,
        body = 0.75f, bodyFrom = 160f, bodyTo = 58f, bodyDecay = 0.085f, tail = 0.32f, tailDecay = 0.26f, tailLow = 700f,
        echo1 = 0.11f, echo2 = 0.23f, echoGain = 0.28f, seed = 37
    };
    public static readonly Shot Shotgun = new Shot
    {
        length = 1f, crack = 0.6f, blast = 1f, blastDecay = 0.065f, blastLow = 2600f, blastHigh = 120f,
        body = 1f, bodyFrom = 105f, bodyTo = 38f, bodyDecay = 0.17f, tail = 0.45f, tailDecay = 0.4f, tailLow = 450f,
        echo1 = 0.13f, echo2 = 0.27f, echoGain = 0.3f, seed = 53
    };

    static float A(float hz) { return 1f - (float)Math.Exp(-2.0 * Math.PI * hz / Rate); }

    public static float[] MakeShot(Shot p)
    {
        int n = (int)(p.length * Rate);
        float[] d = new float[n];
        Random r = new Random(p.seed);
        float lp = 0f, hpLow = 0f, tlp = 0f, phase = 0f;
        float aLow = A(p.blastLow), aHigh = A(p.blastHigh), aTail = A(p.tailLow);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float white = (float)(r.NextDouble() * 2.0 - 1.0);

            // blast: band-limited noise, very fast decay
            lp += (white - lp) * aLow;
            hpLow += (lp - hpLow) * aHigh;
            float band = lp - hpLow;
            float blast = band * p.blast * (float)Math.Exp(-t / p.blastDecay) * 2.2f;

            // crack: a few milliseconds of bright snap
            float crack = white * p.crack * (float)Math.Exp(-t / 0.0016f);

            // body: a low tone that drops in pitch
            float f = p.bodyTo + (p.bodyFrom - p.bodyTo) * (float)Math.Exp(-t / 0.03f);
            phase += 2f * (float)Math.PI * f / Rate;
            float body = (float)Math.Sin(phase) * p.body * (float)Math.Exp(-t / p.bodyDecay);

            // tail: dark rumble that fades in just after the blast
            tlp += (white - tlp) * aTail;
            float fadeIn = Math.Min(1f, t / 0.012f);
            float tail = tlp * p.tail * 3f * (float)Math.Exp(-t / p.tailDecay) * fadeIn;

            d[i] = blast + crack + body + tail;
        }

        // echoes off the buildings: duller, quieter copies
        float[] src = (float[])d.Clone();
        float s = 0f;
        for (int i = 0; i < n; i++) { s += (src[i] - s) * A(1500f); src[i] = s; }
        int e1 = (int)(p.echo1 * Rate), e2 = (int)(p.echo2 * Rate);
        for (int i = n - 1; i >= 0; i--)
        {
            if (i - e1 >= 0) d[i] += src[i - e1] * p.echoGain;
            if (i - e2 >= 0) d[i] += src[i - e2] * p.echoGain * 0.55f;
        }
        Finish(d, 0.92f);
        return d;
    }

    // ------------------------------------------------------------------ handling sounds
    /// Pump action: back, then forward (two metallic clacks).
    public static float[] MakePump()
    {
        float[] d = new float[(int)(0.34f * Rate)];
        Clack(d, 0f, 1700f, 0.9f, 1);
        Clack(d, 0.17f, 2300f, 1f, 2);
        Finish(d, 0.8f);
        return d;
    }

    public static float[] MakeDry()
    {
        float[] d = new float[(int)(0.07f * Rate)];
        Clack(d, 0f, 2600f, 0.6f, 3);
        Finish(d, 0.5f);
        return d;
    }

    public static float[] MakeMagOut()
    {
        float[] d = new float[(int)(0.18f * Rate)];
        Clack(d, 0f, 1300f, 0.7f, 4);
        Slide(d, 0.02f, 0.09f, 5);
        Finish(d, 0.6f);
        return d;
    }

    public static float[] MakeMagIn()
    {
        float[] d = new float[(int)(0.16f * Rate)];
        Thump(d, 0f, 230f, 0.9f);
        Clack(d, 0.004f, 1900f, 0.9f, 6);
        Finish(d, 0.75f);
        return d;
    }

    public static float[] MakeShell()
    {
        float[] d = new float[(int)(0.12f * Rate)];
        Thump(d, 0f, 300f, 0.6f);
        Clack(d, 0.002f, 2100f, 0.6f, 7);
        Finish(d, 0.6f);
        return d;
    }

    /// W3: helicopter rotor, a seamless 2-second loop: the "wop-wop" of the blades (5 a second),
    /// a low engine drone and a faint turbine whine.
    public static float[] MakeRotor()
    {
        int n = 2 * Rate, x = (int)(0.05f * Rate);
        float[] raw = new float[n + x];              // a little extra, cross-faded into the start so the loop is seamless
        Random r = new Random(77);
        float lp = 0f, lp2 = 0f, a = A(380f), a2 = A(90f);
        const float blades = 5f;                     // thumps per second (a whole number of cycles per loop)
        for (int i = 0; i < raw.Length; i++)
        {
            float t = i / (float)Rate;
            float white = (float)(r.NextDouble() * 2.0 - 1.0);
            lp += (white - lp) * a;
            lp2 += (white - lp2) * a2;
            float ph = (t * blades) % 1f;                                   // 0..1 through each blade pass
            float thump = (float)Math.Exp(-ph / 0.09f);                     // sharp attack, quick fade
            float wop = lp * 3.2f * (0.15f + thump) + (float)Math.Sin(2 * Math.PI * 62f * t) * 0.45f * thump;
            float drone = lp2 * 2.5f + (float)Math.Sin(2 * Math.PI * 41f * t) * 0.12f;
            float whine = (float)Math.Sin(2 * Math.PI * 1840f * t) * 0.025f + (float)Math.Sin(2 * Math.PI * 2210f * t) * 0.015f;
            raw[i] = wop + drone + whine;
        }
        float[] d = new float[n];
        for (int i = 0; i < n; i++) d[i] = raw[i];
        // the extra bit after the end continues naturally into sample 0: blend it over the start
        for (int i = 0; i < x; i++) { float k = i / (float)x; d[i] = raw[i] * k + raw[n + i] * (1f - k); }
        float max = 1e-6f;
        foreach (float v in d) max = Math.Max(max, Math.Abs(v));
        for (int i = 0; i < n; i++) d[i] = (float)Math.Tanh(d[i] / max * 1.3f) * 0.8f;
        return d;
    }

    /// G3: a spent brass case hitting the pavement: a short bright metallic ting.
    public static float[] MakeCasing()
    {
        float[] d = new float[(int)(0.16f * Rate)];
        float[] partials = { 4150f, 6080f, 8320f, 10900f };
        float[] levels = { 1f, 0.7f, 0.45f, 0.25f };
        Random r = new Random(91);
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate, v = 0f;
            for (int k = 0; k < partials.Length; k++) v += (float)Math.Sin(2 * Math.PI * partials[k] * t) * levels[k] * (float)Math.Exp(-t / (0.045f - k * 0.008f));
            v += (float)(r.NextDouble() * 2 - 1) * (float)Math.Exp(-t / 0.0015f) * 0.8f;
            d[i] = v;
        }
        Finish(d, 0.6f);
        return d;
    }

    static void Clack(float[] d, float at, float ringHz, float level, int seed)
    {
        Random r = new Random(seed);
        int start = (int)(at * Rate);
        float lp = 0f, phase = 0f, a = A(6000f);
        for (int i = start; i < d.Length; i++)
        {
            float t = (i - start) / (float)Rate;
            if (t > 0.08f) break;
            float white = (float)(r.NextDouble() * 2.0 - 1.0);
            lp += (white - lp) * a;
            phase += 2f * (float)Math.PI * ringHz / Rate;
            float ring = ((float)Math.Sin(phase) + 0.5f * (float)Math.Sin(phase * 2.71f)) * (float)Math.Exp(-t / 0.018f);
            float hit = lp * (float)Math.Exp(-t / 0.003f) * 2f;
            d[i] += (ring * 0.6f + hit) * level;
        }
    }

    static void Slide(float[] d, float at, float len, int seed)
    {
        Random r = new Random(seed);
        int start = (int)(at * Rate), end = Math.Min(d.Length, start + (int)(len * Rate));
        float lp = 0f, a = A(3000f);
        for (int i = start; i < end; i++)
        {
            float t = (i - start) / (float)(end - start);
            lp += ((float)(r.NextDouble() * 2.0 - 1.0) - lp) * a;
            d[i] += lp * 0.5f * (float)Math.Sin(Math.PI * t);
        }
    }

    static void Thump(float[] d, float at, float hz, float level)
    {
        int start = (int)(at * Rate);
        float phase = 0f;
        for (int i = start; i < d.Length; i++)
        {
            float t = (i - start) / (float)Rate;
            if (t > 0.12f) break;
            phase += 2f * (float)Math.PI * hz * (1f - t * 2f) / Rate;
            d[i] += (float)Math.Sin(phase) * level * (float)Math.Exp(-t / 0.03f);
        }
    }

    /// Normalise, soft-clip for punch, short fades so nothing clicks.
    static void Finish(float[] d, float peak)
    {
        float max = 1e-6f;
        foreach (float v in d) max = Math.Max(max, Math.Abs(v));
        float k = 1.4f / max;
        for (int i = 0; i < d.Length; i++) d[i] = (float)Math.Tanh(d[i] * k) * peak;
        int fade = Math.Min(d.Length / 4, (int)(0.03f * Rate));
        for (int i = 0; i < fade; i++) d[d.Length - 1 - i] *= i / (float)fade;
        d[0] = 0f;
    }
}
