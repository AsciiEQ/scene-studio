using System.Text.Json;
using System.Text.Json.Serialization;

namespace AsciiEQ.Scenes;

/// <summary>How the picture is made: a photo turned into characters, or a pattern drawn by code.</summary>
public enum ScenePattern
{
    Image, Noise, Circuit, Dunes, Grid, Blueprint, Waves,
    Rain, Starfield, Bars, Pulse, Tunnel, Tide,
}

/// <summary>Which characters stand in for brightness, darkest first.</summary>
public enum SceneCharset { Classic, Blocks, Dots, Code }

/// <summary>
/// An ASCII scene (an <c>.asciiscene</c> file): what to draw, in which colours, and how it moves
/// to sound. Scenes are JSON so anyone can make one, by hand or in Scene Studio. A scene with
/// <see cref="Reactive"/> set follows the music: bass, beat and treble each move something.
/// </summary>
public sealed class AsciiScene
{
    public const int CurrentVersion = 1;
    /// <summary>The biggest embedded image a scene may carry (bytes, before base64): keeps files shareable.</summary>
    public const int MaxImageBytes = 2 * 1024 * 1024;

    public int Version { get; set; } = CurrentVersion;
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "Untitled";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";

    public ScenePattern Pattern { get; set; } = ScenePattern.Noise;
    public SceneCharset Charset { get; set; } = SceneCharset.Classic;

    /// <summary>A PNG or JPEG, base64, when <see cref="Pattern"/> is Image (the file carries its own picture).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImageBase64 { get; set; }

    /// <summary>Background colour, "#RRGGBB".</summary>
    public string Background { get; set; } = "#0B0E14";
    /// <summary>One to four colours, dim to bright; the characters are coloured by brightness along them.</summary>
    public List<string> Colors { get; set; } = ["#1F3A3A", "#22C55E", "#A7F3D0"];

    /// <summary>Characters across the picture (60 … 240).</summary>
    public int Columns { get; set; } = 160;
    /// <summary>0 … 1: how strongly the scene shows behind the app's pages (the desktop uses 1).</summary>
    public double Opacity { get; set; } = 0.14;
    /// <summary>A random seed: the same scene always looks the same.</summary>
    public int Seed { get; set; } = 1;

    /// <summary>Moves with time and, when the app plays sound, with the music.</summary>
    public bool Reactive { get; set; }
    /// <summary>How fast the pattern moves on its own, 0 … 2 (1 = as designed).</summary>
    public double Speed { get; set; } = 1;
    /// <summary>How much the bass swells the picture, 0 … 2.</summary>
    public double BassAmount { get; set; } = 1;
    /// <summary>How much a beat flashes it, 0 … 2.</summary>
    public double BeatAmount { get; set; } = 1;
    /// <summary>How much the treble sparkles it, 0 … 2.</summary>
    public double TrebleAmount { get; set; } = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
        PropertyNameCaseInsensitive = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Reads a scene, clamping every value to its range. Null when the text isn't a scene.</summary>
    public static AsciiScene? FromJson(string json)
    {
        AsciiScene? s;
        try { s = JsonSerializer.Deserialize<AsciiScene>(json, Json); }
        catch (JsonException) { return null; }
        if (s is null || string.IsNullOrWhiteSpace(s.Name)) return null;
        s.Normalize();
        return s;
    }

    /// <summary>Puts every value into its range, so a hand-edited or hostile file can't break the renderer.</summary>
    public void Normalize()
    {
        Name = Name.Trim().Length > 60 ? Name.Trim()[..60] : Name.Trim();
        Author = (Author ?? "").Trim() is var a && a.Length > 60 ? a[..60] : a;
        Description = (Description ?? "").Trim() is var d && d.Length > 400 ? d[..400] : d;
        if (!Enum.IsDefined(Pattern)) Pattern = ScenePattern.Noise;
        if (!Enum.IsDefined(Charset)) Charset = SceneCharset.Classic;
        Background = SceneColor.Clean(Background, "#0B0E14");
        Colors = (Colors ?? []).Select(c => SceneColor.Clean(c, "")).Where(c => c.Length > 0).Take(4).ToList();
        if (Colors.Count == 0) Colors = ["#22C55E"];
        Columns = Math.Clamp(Columns, 60, 240);
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0, 1) : 0.14;
        Speed = double.IsFinite(Speed) ? Math.Clamp(Speed, 0, 2) : 1;
        BassAmount = double.IsFinite(BassAmount) ? Math.Clamp(BassAmount, 0, 2) : 1;
        BeatAmount = double.IsFinite(BeatAmount) ? Math.Clamp(BeatAmount, 0, 2) : 1;
        TrebleAmount = double.IsFinite(TrebleAmount) ? Math.Clamp(TrebleAmount, 0, 2) : 1;
        if (string.IsNullOrEmpty(Id) || Id.Length > 40) Id = Guid.NewGuid().ToString("N")[..12];
        if (ImageBase64 is not null && ImageBase64.Length > MaxImageBytes * 4 / 3 + 4) ImageBase64 = null;
        if (Pattern != ScenePattern.Image) ImageBase64 = null;
        Version = CurrentVersion;
    }

    /// <summary>A copy with a new id (for "save as" and for community uploads).</summary>
    public AsciiScene Copy()
    {
        var c = FromJson(ToJson())!;
        c.Id = Guid.NewGuid().ToString("N")[..12];
        return c;
    }
}

/// <summary>"#RRGGBB" helpers.</summary>
public static class SceneColor
{
    public static string Clean(string? hex, string fallback)
    {
        if (hex is null) return fallback;
        hex = hex.Trim();
        if (hex.Length == 7 && hex[0] == '#' && hex.Skip(1).All(Uri.IsHexDigit)) return hex.ToUpperInvariant();
        if (hex.Length == 6 && hex.All(Uri.IsHexDigit)) return "#" + hex.ToUpperInvariant();
        return fallback;
    }

    public static (byte R, byte G, byte B) Parse(string hex)
    {
        hex = Clean(hex, "#000000");
        return (Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));
    }
}
