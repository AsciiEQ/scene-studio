namespace AsciiEQ.Scenes;

/// <summary>
/// The scenes that ship with AsciiEQ: six still ones for everyone, six that move with the music
/// for Pro and Studio. Ids are stable ("builtin:…"), so a setting that names one keeps working.
/// </summary>
public static class BuiltInScenes
{
    public const string Prefix = "builtin:";

    public static IReadOnlyList<AsciiScene> All { get; } =
    [
        Still("noise", "Static", "A slow field of grain, like an old monitor between channels.", ScenePattern.Noise, "#0B0E14", ["#1C2430", "#3B82F6", "#BFDBFE"]),
        Still("circuit", "Circuit", "Traces and pulses on a board.", ScenePattern.Circuit, "#07100C", ["#14532D", "#22C55E", "#BBF7D0"], SceneCharset.Code),
        Still("dunes", "Dunes", "Ridges of sand under a low sun.", ScenePattern.Dunes, "#120C08", ["#7C2D12", "#F59E0B", "#FDE68A"]),
        Still("grid", "Horizon", "A floor grid rolling toward you.", ScenePattern.Grid, "#0E0718", ["#4C1D95", "#EC4899", "#FBCFE8"]),
        Still("blueprint", "Blueprint", "Drafting paper, rings and a faint grid.", ScenePattern.Blueprint, "#0A1A2F", ["#1E3A8A", "#60A5FA", "#DBEAFE"], SceneCharset.Dots),
        Still("waves", "Shore", "A still sea line.", ScenePattern.Waves, "#06121A", ["#0E4F6E", "#14B8A6", "#CCFBF1"]),

        Live("rain", "Rain", "Falling code; the treble sparkles, the bass speeds it up.", ScenePattern.Rain, "#030A05", ["#0F3D22", "#22C55E", "#D9FDE4"], SceneCharset.Code),
        Live("stars", "Starfield", "Stars that twinkle with the treble and flash on the beat.", ScenePattern.Starfield, "#05070F", ["#1E293B", "#93C5FD", "#FFFFFF"], SceneCharset.Dots),
        Live("bars", "Spectrum", "Bars from bass to treble, like a wall of meters.", ScenePattern.Bars, "#0B0E14", ["#312E81", "#8B5CF6", "#DDD6FE"], SceneCharset.Blocks),
        Live("pulse", "Pulse", "Rings from the centre; the bass swells them, the beat lights them.", ScenePattern.Pulse, "#120A0A", ["#7F1D1D", "#EF4444", "#FECACA"]),
        Live("tunnel", "Tunnel", "Flying into a tunnel that breathes with the bass.", ScenePattern.Tunnel, "#0A0A12", ["#3730A3", "#38BDF8", "#E0F2FE"]),
        Live("tide", "Tide", "A sea that rises with the bass and froths with the treble.", ScenePattern.Tide, "#05121A", ["#0C4A6E", "#06B6D4", "#CFFAFE"], SceneCharset.Blocks),
    ];

    public static AsciiScene? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(s => s.Id == id);

    public static bool IsBuiltIn(string? id) => id is not null && id.StartsWith(Prefix, StringComparison.Ordinal);

    private static AsciiScene Still(string id, string name, string description, ScenePattern pattern, string bg, List<string> colors, SceneCharset charset = SceneCharset.Classic) =>
        new()
        {
            Id = Prefix + id, Name = name, Author = "AsciiEQ", Description = description, Pattern = pattern,
            Background = bg, Colors = colors, Charset = charset, Reactive = false, Seed = id.Length * 17 + 3,
        };

    private static AsciiScene Live(string id, string name, string description, ScenePattern pattern, string bg, List<string> colors, SceneCharset charset = SceneCharset.Classic) =>
        new()
        {
            Id = Prefix + id, Name = name, Author = "AsciiEQ", Description = description, Pattern = pattern,
            Background = bg, Colors = colors, Charset = charset, Reactive = true, Seed = id.Length * 31 + 5,
        };
}
