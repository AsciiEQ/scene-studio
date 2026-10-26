# Scene Studio

Design **scenes** for [AsciiEQ](https://asciieq.com): ASCII pictures drawn behind the app, set as a
Windows wallpaper, or (with AsciiEQ Pro/Studio) running live on the desktop behind the icons, moving
with the bass, beat and treble of whatever is playing.

A scene is one small JSON file, `name.asciiscene`. This repository holds:

- `AsciiEQ.Scenes/` — the scene format, the 13 patterns, the renderer and the 12 built-in scenes (a .NET 8 / WPF library).
- `AsciiEQ.SceneStudio/` — the editor window (a library, so AsciiEQ opens it in-app).
- `AsciiEQ.SceneStudio.App/` — the standalone `SceneStudio.exe`.

Everything here is MIT licensed: anything you make with it is yours.

## Using Scene Studio

1. Pick a **pattern** (or *Image* and choose your own picture: it's turned into characters by brightness and kept inside the file, up to 2 MB).
2. Choose the **characters**, a **background** and one to four **colours**, dim to bright.
3. Set **columns** (detail) and the **opacity** AsciiEQ draws it at behind its pages.
4. Tick **Live** to make it move with the music, and set how much the **bass** swells it, the **beat** flashes it and the **treble** sparkles it. The preview's *Test beat* fakes a kick drum so you can see it without playing anything.
5. **Save**: you get `name.asciiscene` and `name.png` (the preview the gallery shows).

Try it in AsciiEQ with **Themes → Import…** (or *Add to my AsciiEQ scenes* when Scene Studio was opened from the app).

## Sharing

Upload the `.asciiscene` and the `.png` at <https://asciieq.com/scenes>. Uploads need an AsciiEQ Pro or
Studio licence key and are reviewed by a person before they appear. By uploading you confirm the scene
is yours to share (including any picture in it) and you let AsciiEQ show it and let others download it.

## The file format

```json
{
  "Version": 1,
  "Id": "a1b2c3d4e5f6",
  "Name": "Neon Rain",
  "Author": "you",
  "Description": "Falling code; the treble sparkles, the bass speeds it up.",
  "Pattern": "Rain",            // Image, Noise, Circuit, Dunes, Grid, Blueprint, Waves, Rain, Starfield, Bars, Pulse, Tunnel, Tide
  "Charset": "Code",            // Classic, Blocks, Dots, Code
  "Background": "#030A05",
  "Colors": ["#0F3D22", "#22C55E", "#D9FDE4"],
  "Columns": 160,               // 60 … 240
  "Opacity": 0.14,              // 0 … 1, behind the app
  "Seed": 1,
  "Reactive": true,
  "Speed": 1, "BassAmount": 1, "BeatAmount": 1, "TrebleAmount": 1,   // 0 … 2
  "ImageBase64": null           // a JPEG/PNG when Pattern is Image
}
```

Every value is clamped on load, so a hand-edited file can't break the renderer. Patterns are plain
functions of (x, y, time, audio) in `ScenePatterns.cs`; adding one is a pull request away.

## Building

```
dotnet build AsciiEQ.SceneStudio.App -c Release
dotnet publish AsciiEQ.SceneStudio.App -c Release -r win-x64 --self-contained true -o out
```

Requires the .NET 8 SDK on Windows. Contributions welcome: new patterns, charsets, and anything that
makes scenes easier to make.
