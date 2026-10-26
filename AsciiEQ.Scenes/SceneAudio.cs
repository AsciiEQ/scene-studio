using System.Diagnostics;

namespace AsciiEQ.Scenes;

/// <summary>
/// What the music is doing right now, 0 … 1 each: bass, mids, treble as smoothed levels, and
/// beat as a flash that jumps on a bass hit and fades. Silence is all zeros.
/// </summary>
public readonly record struct SceneAudio(double Bass, double Mid, double Treble, double Beat)
{
    public static readonly SceneAudio Silence = new(0, 0, 0, 0);
}

/// <summary>
/// Turns a spectrum (dB per band, with each band's frequency) into <see cref="SceneAudio"/>.
/// Each band group is scaled against its own recent quiet and loud levels, so a soft song and a
/// loud one both swing the whole 0 … 1 range and a steady mix doesn't pin the picture at one
/// level. Smoothing is in seconds, so the frame rate of the spectrum doesn't matter. Feed it every
/// spectrum frame; read <see cref="Current"/> when drawing.
/// </summary>
public sealed class SceneAudioTracker
{
    private readonly Band _bass = new(releaseSeconds: 0.22), _mid = new(releaseSeconds: 0.20), _treble = new(releaseSeconds: 0.14);
    private double _beat, _bassAvg, _lastBassRaw;
    private double _sinceBeat = 10;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTime;
    private Func<double>? _now; // tests inject time

    public SceneAudio Current => new(_bass.Value, _mid.Value, _treble.Value, _beat);

    public void Reset()
    {
        _bass.Reset(); _mid.Reset(); _treble.Reset();
        _beat = 0; _bassAvg = 0; _lastBassRaw = 0; _sinceBeat = 10;
    }

    /// <summary>Tests feed their own clock (seconds) so results don't depend on real time.</summary>
    internal void UseClock(Func<double> now) => _now = now;

    public void Update(ReadOnlySpan<float> magnitudeDb, ReadOnlySpan<float> frequencies)
    {
        double now = _now?.Invoke() ?? _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastTime, 0.001, 0.1);
        _lastTime = now;

        // Raw group levels: the mean of the bands' dB, floored so "nothing" reads as -90.
        double bass = 0, mid = 0, treble = 0;
        int nb = 0, nm = 0, nt = 0;
        int n = Math.Min(magnitudeDb.Length, frequencies.Length);
        for (int i = 0; i < n; i++)
        {
            double db = Math.Max(-90, magnitudeDb[i]);
            float f = frequencies[i];
            if (f < 180) { bass += db; nb++; }
            else if (f < 3000) { mid += db; nm++; }
            else { treble += db; nt++; }
        }
        bass = nb > 0 ? bass / nb : -90;
        mid = nm > 0 ? mid / nm : -90;
        treble = nt > 0 ? treble / nt : -90;

        _bass.Update(bass, dt);
        _mid.Update(mid, dt);
        _treble.Update(treble, dt);

        // Beat: the bass jumps above its short-term average while rising, at most ~8 a second.
        double bassNow = _bass.Normalized(bass);
        _bassAvg += (bassNow - _bassAvg) * Math.Min(1, dt / 0.35);
        _sinceBeat += dt;
        bool rising = bassNow > _lastBassRaw + 0.02;
        _lastBassRaw = bassNow;
        if (rising && bassNow > 0.3 && bassNow > _bassAvg * 1.2 + 0.08 && _sinceBeat > 0.12)
        {
            _beat = 1;
            _sinceBeat = 0;
        }
        else
        {
            _beat *= Math.Exp(-dt / 0.16);
            if (_beat < 0.01) _beat = 0;
        }
    }

    /// <summary>One frequency group: adaptive range in dB and an attack/release envelope on the result.</summary>
    private sealed class Band(double releaseSeconds)
    {
        private const double MinRangeDb = 10;   // never stretch a near-constant signal into noise
        private const double FloorRiseDbPerSec = 12;  // the quiet reference finds a louder mix within a couple of seconds
        private const double CeilFallDbPerSec = 4;    // the loud reference relaxes toward a softer mix

        private double _floor = -90, _ceil = -90, _value;

        public double Value => _value;

        public void Reset() { _floor = -90; _ceil = -90; _value = 0; }

        /// <summary>Where a raw level sits in the current range, without touching the envelope.</summary>
        public double Normalized(double db)
        {
            if (db <= -89) return 0; // silence is silence, whatever the range
            double range = Math.Max(MinRangeDb, _ceil - _floor);
            return Math.Clamp((db - _floor) / range, 0, 1);
        }

        public void Update(double db, double dt)
        {
            // Range tracking: the ceiling snaps up to a louder level and falls slowly; the floor
            // snaps down to a quieter one and rises slowly. Silence (-90) pulls both home fast.
            if (db > _ceil) _ceil = db; else _ceil = Math.Max(db, _ceil - CeilFallDbPerSec * dt);
            // Out of silence the floor jumps to just under the first sound, so the range is real at once.
            if (_floor <= -85 && db > -80) _floor = db - MinRangeDb;
            if (db < _floor) _floor = db; else _floor = Math.Min(db, _floor + FloorRiseDbPerSec * dt);
            if (db <= -89) { _ceil = Math.Max(-90, _ceil - 30 * dt); }
            // Keep a real gap so the loud end always has somewhere to go (never below -90).
            if (_ceil - _floor < MinRangeDb) _ceil = _floor + MinRangeDb;

            double target = Normalized(db);
            if (db <= -89) target = 0;
            // Rise almost at once, fall over the release time.
            double k = target > _value ? Math.Min(1, dt / 0.015) : Math.Min(1, dt / releaseSeconds);
            _value += (target - _value) * k;
            if (_value < 0.002) _value = 0;
        }
    }
}
