using System.IO;

namespace AsciiEQ.Scenes.Tests;

public class RenderTests
{
    [Fact]
    public void EveryPatternFillsInRange_AndIsDeterministic()
    {
        foreach (var pattern in Enum.GetValues<ScenePattern>())
        {
            var scene = new AsciiScene { Name = "t", Pattern = pattern, Reactive = true, Seed = 9 };
            int cols = 80, rows = 30;
            var a = new float[cols * rows];
            var b = new float[cols * rows];
            var audio = new SceneAudio(0.6, 0.4, 0.5, 0.8);
            ScenePatterns.Fill(scene, a, cols, rows, 2.5, audio);
            ScenePatterns.Fill(scene, b, cols, rows, 2.5, audio);
            Assert.All(a, v => Assert.InRange(v, 0f, 1f));
            Assert.Equal(a, b);
            if (pattern != ScenePattern.Image)
                Assert.True(a.Any(v => v > 0.05f), $"{pattern} drew nothing");
        }
    }

    [Fact]
    public void ReactiveScenesRespondToBass()
    {
        foreach (var scene in BuiltInScenes.All.Where(s => s.Reactive))
        {
            int cols = 80, rows = 30;
            var quiet = new float[cols * rows];
            var loud = new float[cols * rows];
            ScenePatterns.Fill(scene, quiet, cols, rows, 1.0, SceneAudio.Silence);
            ScenePatterns.Fill(scene, loud, cols, rows, 1.0, new SceneAudio(1, 0.8, 1, 1));
            Assert.True(loud.Sum() > quiet.Sum(), $"{scene.Name} didn't brighten with music");
        }
    }

    [Fact]
    public void StillScenesIgnoreAudio()
    {
        foreach (var scene in BuiltInScenes.All.Where(s => !s.Reactive))
        {
            int cols = 80, rows = 30;
            var quiet = new float[cols * rows];
            var loud = new float[cols * rows];
            ScenePatterns.Fill(scene, quiet, cols, rows, 1.0, SceneAudio.Silence);
            ScenePatterns.Fill(scene, loud, cols, rows, 1.0, new SceneAudio(1, 0.8, 1, 1));
            Assert.Equal(quiet, loud);
        }
    }

    [Fact]
    public void AudioTrackerFollowsSpectrumAndDetectsBeat()
    {
        var tracker = new SceneAudioTracker();
        double t = 0;
        tracker.UseClock(() => t);
        void Feed(float[] db, int frames = 1) { for (int i = 0; i < frames; i++) { t += 1 / 30.0; tracker.Update(db, Freqs); } }

        Feed(Silent, 20);
        Assert.Equal(0, tracker.Current.Bass, 3);
        Assert.Equal(0, tracker.Current.Beat, 3);

        // A kick after silence: full bass at once, and a beat.
        Feed(Kick);
        Assert.InRange(tracker.Current.Bass, 0.9, 1.0);
        Assert.Equal(1, tracker.Current.Beat, 3);

        // Silence again: both fall away within a second.
        Feed(Silent, 30);
        Assert.InRange(tracker.Current.Bass, 0, 0.05);
        Assert.InRange(tracker.Current.Beat, 0, 0.02);
    }

    [Fact]
    public void AudioTrackerScalesToTheSongNotTheVolume()
    {
        // The same groove 20 dB quieter still swings the bass most of the way, because each
        // group is scaled against its own recent range, not a fixed dB scale.
        double Swing(float offset)
        {
            var tracker = new SceneAudioTracker();
            double t = 0;
            tracker.UseClock(() => t);
            double max = 0, min = 1;
            for (int beat = 0; beat < 8; beat++)
            {
                for (int f = 0; f < 15; f++)
                {
                    t += 1 / 30.0;
                    bool hit = f < 3;
                    var frame = new float[Freqs.Length];
                    for (int i = 0; i < frame.Length; i++)
                        frame[i] = (Freqs[i] < 180 ? (hit ? -6f : -26f) : -30f) + offset;
                    tracker.Update(frame, Freqs);
                    if (beat >= 4) { max = Math.Max(max, tracker.Current.Bass); min = Math.Min(min, tracker.Current.Bass); }
                }
            }
            return max - min;
        }
        Assert.True(Swing(0) > 0.6, $"loud swing {Swing(0):F2}");
        Assert.True(Swing(-20) > 0.6, $"quiet swing {Swing(-20):F2}");
    }

    private static readonly float[] Freqs = [40, 80, 120, 500, 1000, 2000, 4000, 8000, 16000];
    private static readonly float[] Silent = Enumerable.Repeat(-90f, Freqs.Length).ToArray();
    private static readonly float[] Kick = [-3, -3, -6, -40, -40, -40, -50, -50, -50];

    [Fact]
    public void BitmapRendersEveryBuiltIn_AndSavesPng()
    {
        RunSta(() =>
        {
            var sb = new SceneBitmap();
            var dir = Path.Combine(Path.GetTempPath(), "asciieq-scene-tests");
            foreach (var scene in BuiltInScenes.All)
            {
                var bmp = sb.Render(scene, 320, 180, 1.0, new SceneAudio(0.5, 0.5, 0.5, 0.5));
                Assert.Equal(320, bmp.PixelWidth);
                var px = new byte[320 * 180 * 4];
                bmp.CopyPixels(px, 320 * 4, 0);
                // Not a flat picture: some pixels differ from the background.
                Assert.True(px.Chunk(4).Select(p => (p[0], p[1], p[2])).Distinct().Count() > 4, scene.Name);
                var file = Path.Combine(dir, scene.Id.Replace(':', '_') + ".png");
                sb.SavePng(scene, 160, 90, file);
                Assert.True(new FileInfo(file).Length > 100);
            }
            // Re-rendering into the same bitmap reuses it.
            var first = sb.Render(BuiltInScenes.All[0], 100, 60, 0, SceneAudio.Silence);
            var again = sb.Render(BuiltInScenes.All[1], 100, 60, 0, SceneAudio.Silence, first);
            Assert.Same(first, again);
        });
    }

    [Fact]
    public void TransparentBackgroundLeavesEmptyCellsClear()
    {
        RunSta(() =>
        {
            var sb = new SceneBitmap();
            var scene = BuiltInScenes.Find("builtin:stars")!; // mostly empty cells
            var bmp = sb.Render(scene, 320, 180, 1.0, SceneAudio.Silence, null, 0.5, null, transparentBackground: true);
            var px = new byte[320 * 180 * 4];
            bmp.CopyPixels(px, 320 * 4, 0);
            int clear = 0, drawn = 0;
            for (int i = 3; i < px.Length; i += 4) { if (px[i] == 0) clear++; else drawn++; }
            Assert.True(clear > drawn * 3, $"{clear} clear vs {drawn} drawn");
            Assert.True(drawn > 0);
            // Nothing is drawn stronger than the scene's opacity allows.
            for (int i = 3; i < px.Length; i += 4) Assert.True(px[i] <= 128);
        });
    }

    [Fact]
    public void FrameCostIsSmall()
    {
        // A reactive frame at app-window size on the UI thread must stay well under a 15 fps budget.
        RunSta(() =>
        {
            var sb = new SceneBitmap();
            var scene = BuiltInScenes.Find("builtin:rain")!;
            var bmp = sb.Render(scene, 1280, 800, 0, SceneAudio.Silence);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 1; i <= 20; i++) sb.Render(scene, 1280, 800, i * 0.07, new SceneAudio(0.5, 0.5, 0.5, 0.2), bmp);
            double ms = sw.Elapsed.TotalMilliseconds / 20;
            Console.WriteLine($"SCENE FRAME {ms:F1} ms");
            Assert.True(ms < 25, $"frame took {ms:F1} ms");
        });
    }

    private static void RunSta(Action body)
    {
        Exception? failure = null;
        var t = new Thread(() => { try { body(); } catch (Exception e) { failure = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
