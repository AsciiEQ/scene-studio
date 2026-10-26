using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AsciiEQ.Scenes;

/// <summary>
/// Draws a scene as characters into a bitmap. Each character of the ramp is rendered once into
/// a glyph atlas (an alpha mask per glyph); a frame then composites cells from the atlas with the
/// scene's colours, which is fast enough for a reactive scene at 15 frames a second on the UI
/// thread. Still scenes render once.
/// </summary>
public sealed class SceneBitmap
{
    private static readonly string[] Ramps =
    [
        " `.,-~:;!><=+rcvzxsaoe2358ASGHDB#%@WM",     // Classic
        " ░▒▓█",                                      // Blocks
        " .·:∙•●",                                    // Dots
        " .,:;=+*#%@&01{}[]<>/\\|$",                  // Code
    ];

    private readonly Typeface _typeface = new(new FontFamily("Cascadia Mono, Consolas, Courier New"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private byte[][]? _atlas;   // per glyph: cellW × cellH alpha
    private string? _atlasRamp;
    private int _cellW, _cellH;
    private float[]? _brightness;
    private float[]? _imageLuma;
    private int _lumaCols, _lumaRows;
    private string? _lumaFor;
    private (byte R, byte G, byte B)[]? _palette;
    private string? _paletteFor;

    /// <summary>The cell size used for a given column count across a given pixel width.</summary>
    public static (int CellW, int CellH) CellSize(int pixelWidth, int columns)
    {
        int cellW = Math.Max(3, pixelWidth / Math.Max(1, columns));
        return (cellW, (int)Math.Round(cellW * 1.9));
    }

    /// <summary>
    /// Renders one frame of the scene into <paramref name="target"/> (BGRA32, the right size), or a fresh bitmap.
    /// With <paramref name="transparentBackground"/> only the characters are drawn (their alpha is the
    /// opacity) and the rest stays clear, so behind the app the window's own background, and Glass
    /// Mode, show through; wallpapers, the desktop and thumbnails use the scene's background colour.
    /// </summary>
    public WriteableBitmap Render(AsciiScene scene, int pixelWidth, int pixelHeight, double t, SceneAudio audio,
        WriteableBitmap? target = null, double? opacityOverride = null, int? columnsOverride = null, bool transparentBackground = false)
    {
        pixelWidth = Math.Max(8, pixelWidth);
        pixelHeight = Math.Max(8, pixelHeight);
        var (cellW, cellH) = CellSize(pixelWidth, columnsOverride ?? scene.Columns);
        int cols = Math.Max(1, pixelWidth / cellW), rows = Math.Max(1, pixelHeight / cellH);
        string ramp = Ramps[(int)scene.Charset % Ramps.Length];
        EnsureAtlas(ramp, cellW, cellH);
        EnsurePalette(scene);
        if (_brightness is null || _brightness.Length < cols * rows) _brightness = new float[cols * rows];
        if (scene.Pattern == ScenePattern.Image) EnsureImageLuma(scene, cols, rows);
        ScenePatterns.Fill(scene, _brightness, cols, rows, t, audio, scene.Pattern == ScenePattern.Image ? _imageLuma : null);

        if (target is null || target.PixelWidth != pixelWidth || target.PixelHeight != pixelHeight)
            target = new WriteableBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Bgra32, null);

        var bg = SceneColor.Parse(scene.Background);
        double opacity = opacityOverride ?? scene.Opacity;
        int stride = target.BackBufferStride;
        target.Lock();
        unsafe
        {
            byte* px = (byte*)target.BackBuffer;
            // Background (clear when transparent: BGRA32 is not premultiplied, so colour with alpha 0 is fine)
            byte bgA = transparentBackground ? (byte)0 : (byte)255;
            for (int y = 0; y < pixelHeight; y++)
            {
                byte* row = px + y * stride;
                for (int x = 0; x < pixelWidth; x++)
                {
                    row[x * 4] = bg.B; row[x * 4 + 1] = bg.G; row[x * 4 + 2] = bg.R; row[x * 4 + 3] = bgA;
                }
            }
            // Cells
            int glyphs = ramp.Length;
            int offX = (pixelWidth - cols * cellW) / 2, offY = (pixelHeight - rows * cellH) / 2;
            for (int cy = 0; cy < rows; cy++)
            {
                for (int cx = 0; cx < cols; cx++)
                {
                    float b = _brightness[cy * cols + cx];
                    int g = (int)(b * (glyphs - 1) + 0.5f);
                    if (g <= 0) continue;
                    var mask = _atlas![g];
                    var color = _palette![(int)(b * (_palette.Length - 1) + 0.5f)];
                    int x0 = offX + cx * cellW, y0 = offY + cy * cellH;
                    for (int gy = 0; gy < cellH; gy++)
                    {
                        int yy = y0 + gy;
                        if (yy < 0 || yy >= pixelHeight) continue;
                        byte* row = px + yy * stride;
                        for (int gx = 0; gx < cellW; gx++)
                        {
                            int xx = x0 + gx;
                            if (xx < 0 || xx >= pixelWidth) continue;
                            int a = mask[gy * cellW + gx];
                            if (a == 0) continue;
                            double k = a / 255.0 * opacity;
                            byte* p = row + xx * 4;
                            if (transparentBackground)
                            {
                                // The character's colour, as see-through as the scene's opacity says.
                                p[0] = color.B; p[1] = color.G; p[2] = color.R;
                                p[3] = (byte)Math.Max(p[3], (int)(k * 255));
                                continue;
                            }
                            p[0] = (byte)(p[0] + (color.B - p[0]) * k);
                            p[1] = (byte)(p[1] + (color.G - p[1]) * k);
                            p[2] = (byte)(p[2] + (color.R - p[2]) * k);
                        }
                    }
                }
            }
        }
        target.AddDirtyRect(new Int32Rect(0, 0, pixelWidth, pixelHeight));
        target.Unlock();
        return target;
    }

    /// <summary>A still of the scene as a PNG file (a wallpaper, a thumbnail). Reactive scenes are drawn at a lively moment.</summary>
    public void SavePng(AsciiScene scene, int pixelWidth, int pixelHeight, string path, double? opacity = 1, double t = 3.7)
    {
        var audio = scene.Reactive ? new SceneAudio(0.7, 0.5, 0.45, 0.3) : SceneAudio.Silence;
        var bmp = Render(scene, pixelWidth, pixelHeight, t, audio, null, opacity);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var f = File.Create(path);
        encoder.Save(f);
    }

    private void EnsureAtlas(string ramp, int cellW, int cellH)
    {
        if (_atlas is not null && _atlasRamp == ramp && _cellW == cellW && _cellH == cellH) return;
        _cellW = cellW; _cellH = cellH; _atlasRamp = ramp;
        _atlas = new byte[ramp.Length][];
        double fontSize = cellH * 0.92;
        for (int i = 0; i < ramp.Length; i++)
        {
            var mask = new byte[cellW * cellH];
            _atlas[i] = mask;
            if (ramp[i] == ' ') continue;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var text = new FormattedText(ramp[i].ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    _typeface, fontSize, Brushes.White, 1.0);
                dc.DrawText(text, new Point((cellW - text.Width) / 2, (cellH - text.Height) / 2));
            }
            var rt = new RenderTargetBitmap(cellW, cellH, 96, 96, PixelFormats.Pbgra32);
            rt.Render(visual);
            var pixels = new byte[cellW * cellH * 4];
            rt.CopyPixels(pixels, cellW * 4, 0);
            for (int p = 0; p < cellW * cellH; p++) mask[p] = pixels[p * 4 + 3];
        }
    }

    private void EnsurePalette(AsciiScene scene)
    {
        var key = string.Join(",", scene.Colors) + "|" + scene.Background;
        if (_palette is not null && _paletteFor == key) return;
        _paletteFor = key;
        var stops = new List<(byte R, byte G, byte B)> { SceneColor.Parse(scene.Background) };
        stops.AddRange(scene.Colors.Select(SceneColor.Parse));
        _palette = new (byte, byte, byte)[64];
        for (int i = 0; i < 64; i++)
        {
            double pos = i / 63.0 * (stops.Count - 1);
            int a = Math.Min(stops.Count - 1, (int)pos), b = Math.Min(stops.Count - 1, a + 1);
            double f = pos - a;
            _palette[i] = ((byte)(stops[a].R + (stops[b].R - stops[a].R) * f), (byte)(stops[a].G + (stops[b].G - stops[a].G) * f), (byte)(stops[a].B + (stops[b].B - stops[a].B) * f));
        }
    }

    private void EnsureImageLuma(AsciiScene scene, int cols, int rows)
    {
        var key = (scene.ImageBase64?.Length ?? 0) + ":" + scene.Id;
        if (_imageLuma is not null && _lumaFor == key && _lumaCols == cols && _lumaRows == rows) return;
        _lumaFor = key; _lumaCols = cols; _lumaRows = rows;
        _imageLuma = new float[cols * rows];
        if (string.IsNullOrEmpty(scene.ImageBase64)) return;
        try
        {
            var bytes = Convert.FromBase64String(scene.ImageBase64);
            using var ms = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var small = new TransformedBitmap(decoder.Frames[0], new ScaleTransform(cols / (double)decoder.Frames[0].PixelWidth, rows / (double)decoder.Frames[0].PixelHeight));
            var gray = new FormatConvertedBitmap(small, PixelFormats.Gray8, null, 0);
            var px = new byte[cols * rows];
            gray.CopyPixels(px, cols, 0);
            for (int i = 0; i < px.Length; i++) _imageLuma[i] = px[i] / 255f;
        }
        catch { /* a bad picture draws as nothing */ }
    }

    /// <summary>Reads an image file into a scene's embedded picture (resized so the file stays under the limit).</summary>
    public static string EncodeImage(string path, int maxSide = 1280)
    {
        var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames[0];
        double scale = Math.Min(1, maxSide / (double)Math.Max(frame.PixelWidth, frame.PixelHeight));
        if (scale < 1) frame = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
        var enc = new JpegBitmapEncoder { QualityLevel = 80 };
        enc.Frames.Add(BitmapFrame.Create(frame));
        using var ms = new MemoryStream();
        enc.Save(ms);
        if (ms.Length > AsciiScene.MaxImageBytes) throw new InvalidDataException("The picture is too big even after shrinking it.");
        return Convert.ToBase64String(ms.ToArray());
    }
}
