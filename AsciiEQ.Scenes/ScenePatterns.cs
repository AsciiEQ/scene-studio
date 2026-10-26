namespace AsciiEQ.Scenes;

/// <summary>
/// The patterns, as brightness fields: for each character cell (x, y) at time t with the music's
/// levels, a brightness 0 … 1. Everything is deterministic from the scene's seed, so a scene
/// looks the same on every PC. No allocations per cell.
/// </summary>
public static class ScenePatterns
{
    /// <summary>Fills <paramref name="brightness"/> (rows × cols, row-major) for a frame.</summary>
    public static void Fill(AsciiScene scene, float[] brightness, int cols, int rows, double t, SceneAudio audio, float[]? imageLuma = null)
    {
        double speed = scene.Reactive ? scene.Speed : 0.15; // still scenes drift very slowly
        double time = t * speed;
        double bass = scene.Reactive ? audio.Bass * scene.BassAmount : 0;
        double beat = scene.Reactive ? audio.Beat * scene.BeatAmount : 0;
        double treble = scene.Reactive ? audio.Treble * scene.TrebleAmount : 0;
        uint seed = (uint)scene.Seed * 2654435761u + 7;

        for (int y = 0; y < rows; y++)
        {
            double v = rows <= 1 ? 0 : y / (double)(rows - 1);
            for (int x = 0; x < cols; x++)
            {
                double u = cols <= 1 ? 0 : x / (double)(cols - 1);
                double b = scene.Pattern switch
                {
                    ScenePattern.Image => imageLuma is null ? 0 : imageLuma[y * cols + x],
                    ScenePattern.Noise => Noise(u * 6, v * 3, time * 0.3, seed) * (1 + bass * 0.6),
                    ScenePattern.Circuit => Circuit(x, y, time, seed, beat),
                    ScenePattern.Dunes => Dunes(u, v, time, bass),
                    ScenePattern.Grid => Grid(u, v, time, bass),
                    ScenePattern.Blueprint => Blueprint(x, y, u, v, time, seed, treble),
                    ScenePattern.Waves => Waves(u, v, time, bass),
                    ScenePattern.Rain => Rain(x, y, rows, time, seed, treble, bass),
                    ScenePattern.Starfield => Starfield(x, y, time, seed, treble, beat),
                    ScenePattern.Bars => Bars(u, v, time, seed, audio, scene),
                    ScenePattern.Pulse => Pulse(u, v, time, bass, beat),
                    ScenePattern.Tunnel => Tunnel(u, v, time, bass, beat),
                    ScenePattern.Tide => Tide(u, v, time, bass, treble),
                    _ => 0,
                };
                brightness[y * cols + x] = (float)Math.Clamp(b, 0, 1);
            }
        }
    }

    // ── Hashing and noise (integer hashes: repeatable everywhere) ──

    private static uint Hash(uint x, uint y, uint seed)
    {
        uint h = x * 374761393u + y * 668265263u + seed * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    private static double Rand(uint x, uint y, uint seed) => Hash(x, y, seed) / 4294967296.0;

    /// <summary>Smooth value noise in three dimensions (the third is time).</summary>
    private static double Noise(double x, double y, double z, uint seed)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y), zi = (int)Math.Floor(z);
        double xf = x - xi, yf = y - yi, zf = z - zi;
        double Sm(double f) => f * f * (3 - 2 * f);
        xf = Sm(xf); yf = Sm(yf); zf = Sm(zf);
        double R(int dx, int dy, int dz) => Rand((uint)(xi + dx), (uint)(yi + dy), seed + (uint)(zi + dz) * 1013u);
        double c00 = R(0, 0, 0) + (R(1, 0, 0) - R(0, 0, 0)) * xf;
        double c10 = R(0, 1, 0) + (R(1, 1, 0) - R(0, 1, 0)) * xf;
        double c01 = R(0, 0, 1) + (R(1, 0, 1) - R(0, 0, 1)) * xf;
        double c11 = R(0, 1, 1) + (R(1, 1, 1) - R(0, 1, 1)) * xf;
        double c0 = c00 + (c10 - c00) * yf, c1 = c01 + (c11 - c01) * yf;
        return c0 + (c1 - c0) * zf;
    }

    // ── Still patterns ──

    private static double Circuit(int x, int y, double t, uint seed, double beat)
    {
        // Traces run along every 4th row or column; pulses travel along them.
        bool row = y % 4 == 0, col = x % 7 == 0;
        if (!row && !col) return Rand((uint)x, (uint)y, seed) < 0.02 ? 0.35 : 0.0;
        double pulse = row ? Frac((x - t * 6 + Rand((uint)y, 1, seed) * 50) / 23.0) : Frac((y - t * 4 + Rand((uint)x, 2, seed) * 50) / 17.0);
        double trace = row && col ? 0.55 : 0.28;
        return trace + (pulse < 0.12 ? 0.5 + beat * 0.5 : 0);
    }

    private static double Dunes(double u, double v, double t, double bass)
    {
        double ridge = Math.Sin(u * 9 + v * 4 + t * 0.4) * 0.5 + Math.Sin(u * 21 - v * 6 - t * 0.3) * 0.25;
        double b = (ridge + 0.75) / 1.5;              // 0 … 1
        return 0.05 + b * b * 0.85 + bass * 0.2 * (1 - v);
    }

    private static double Grid(double u, double v, double t, double bass)
    {
        // A floor grid in perspective, lines rolling toward the viewer; the horizon glows with bass.
        double depth = Math.Max(0.02, v - 0.45);
        double lineH = Frac(Math.Log(depth + 0.05) * 3 - t * 0.6);
        double px = (u - 0.5) / depth;
        double lineV = Frac(px * 1.2);
        double line = Math.Max(lineH < 0.08 || lineH > 0.92 ? 1 : 0, Math.Abs(lineV - 0.5) > 0.46 ? 1 : 0);
        double glow = Math.Exp(-Math.Abs(v - 0.45) * 12) * (0.4 + bass * 0.6);
        return v < 0.45 ? glow * 0.8 : Math.Max(line * 0.8 * (1 - depth * 0.6), glow);
    }

    private static double Blueprint(int x, int y, double u, double v, double t, uint seed, double treble)
    {
        double grid = (x % 10 == 0 || y % 5 == 0) ? 0.22 : 0.06;
        double cx = 0.5 + 0.3 * Math.Sin(t * 0.2), cy = 0.5 + 0.2 * Math.Cos(t * 0.17);
        double r = Math.Sqrt((u - cx) * (u - cx) * 2.8 + (v - cy) * (v - cy));
        double rings = Math.Abs(Frac(r * 6) - 0.5) < 0.06 && r < 0.5 ? 0.6 : 0;
        double sparkle = Rand((uint)x, (uint)y, seed + (uint)(t * 3)) < treble * 0.06 ? 0.9 : 0;
        return Math.Max(grid, Math.Max(rings, sparkle));
    }

    private static double Waves(double u, double v, double t, double bass)
    {
        double w = Math.Sin(u * 10 + t) * 0.06 + Math.Sin(u * 23 - t * 1.7) * 0.03 + Math.Sin(u * 5 + t * 0.4) * 0.08 * (1 + bass);
        double d = v - (0.55 + w);
        return d > 0 ? 0.3 + 0.5 * Math.Exp(-d * 8) : Math.Max(0, 0.6 - Math.Abs(d) * 30);
    }

    // ── Moving, music-driven patterns ──

    private static double Rain(int x, int y, int rows, double t, uint seed, double treble, double bass)
    {
        double colSpeed = 4 + Rand((uint)x, 11, seed) * 10 + bass * 8;
        double head = (t * colSpeed + Rand((uint)x, 12, seed) * rows * 3) % (rows * 1.6);
        double behind = head - y;
        if (behind < 0 || behind > 18) return Rand((uint)x, (uint)y, seed + (uint)(t * 2)) < treble * 0.03 ? 0.5 : 0;
        return behind < 1 ? 1 : 0.85 * Math.Exp(-behind / 7);
    }

    private static double Starfield(int x, int y, double t, uint seed, double treble, double beat)
    {
        double r = Rand((uint)x, (uint)y, seed);
        if (r > 0.04) return 0;
        double twinkle = 0.5 + 0.5 * Math.Sin(t * (2 + r * 100) + r * 60);
        return (0.3 + 0.7 * twinkle * (0.6 + treble * 0.8)) * (1 + beat * 0.4) * (r < 0.01 ? 1 : 0.6);
    }

    private static double Bars(double u, double v, double t, uint seed, SceneAudio a, AsciiScene s)
    {
        // Spectrum bars from the bass (left) to the treble (right); still scenes sway instead.
        int bar = (int)(u * 24);
        double across = (bar + 0.5) / 24.0;
        double level = s.Reactive
            ? (across < 0.33 ? a.Bass * s.BassAmount : across < 0.66 ? a.Mid : a.Treble * s.TrebleAmount)
              * (0.6 + 0.4 * Rand((uint)bar, 3, seed)) + a.Beat * s.BeatAmount * 0.2
            : 0.35 + 0.3 * Math.Sin(t * 0.8 + bar * 0.7);
        double height = Math.Clamp(level, 0.03, 1);
        double gap = Frac(u * 24) < 0.15 ? 0 : 1;
        return (1 - v) <= height ? gap * (0.35 + 0.65 * (1 - v) / Math.Max(0.05, height)) : 0;
    }

    private static double Pulse(double u, double v, double t, double bass, double beat)
    {
        double r = Math.Sqrt((u - 0.5) * (u - 0.5) * 2.8 + (v - 0.5) * (v - 0.5));
        double rings = Math.Abs(Frac(r * 7 - t * 0.9) - 0.5) < 0.1 + bass * 0.15 ? 1 : 0;
        double core = Math.Exp(-r * (9 - bass * 5)) * (0.5 + bass * 0.6 + beat * 0.6);
        return Math.Max(rings * (0.6 - r * 0.6) * (0.5 + beat * 0.5), core);
    }

    private static double Tunnel(double u, double v, double t, double bass, double beat)
    {
        double dx = (u - 0.5) * 1.7, dy = v - 0.5;
        double r = Math.Max(0.02, Math.Sqrt(dx * dx + dy * dy));
        double angle = Math.Atan2(dy, dx);
        double depth = Frac(0.25 / r + t * 0.8);
        double spokes = Math.Abs(Frac(angle / (2 * Math.PI) * 12 + t * 0.1) - 0.5) < 0.05 ? 0.5 : 0;
        double rings = depth < 0.15 + bass * 0.2 ? 0.9 : 0.15;
        double edge = Math.Clamp(1 - (r - 0.42) * 4, 0, 1); // fade the corners out
        return (Math.Max(rings, spokes) * Math.Min(1, r * 3)) * (0.7 + beat * 0.4) * edge;
    }

    private static double Tide(double u, double v, double t, double bass, double treble)
    {
        double level = 0.45 + 0.25 * bass;
        double w = Math.Sin(u * 14 + t * 2.2) * 0.04 + Math.Sin(u * 31 - t * 3.1) * 0.02 * (1 + treble * 2);
        double d = v - (1 - level + w);
        return d > 0 ? 0.35 + 0.45 * Math.Exp(-d * 6) * (1 + treble * 0.3) : Math.Max(0, 0.7 - Math.Abs(d) * 40);
    }

    private static double Frac(double x) => x - Math.Floor(x);
}
