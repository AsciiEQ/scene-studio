using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AsciiEQ.Scenes;
using Microsoft.Win32;

namespace AsciiEQ.SceneStudio;

/// <summary>
/// The scene editor. Edits an <see cref="AsciiScene"/> through plain controls and shows it live;
/// Save writes the <c>.asciiscene</c> file plus a PNG preview for the gallery. The window has no
/// dependency on AsciiEQ itself, so it runs standalone (AsciiEQ.SceneStudio.App) or from the app.
/// </summary>
public partial class SceneStudioWindow : Window
{
    public const string GalleryUrl = "https://asciieq.com/scenes";

    private readonly SceneBitmap _renderer = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;
    private readonly string? _libraryFolder;
    private AsciiScene _scene = new() { Name = "My Scene" };
    private string? _path;
    private bool _loading = true; // sliders raise ValueChanged while XAML is still loading
    private bool _dirty;
    private WriteableBitmap? _bitmap;
    private readonly List<(Border Swatch, TextBox Box)> _colorRows = [];

    /// <summary>Raised after a scene was saved into the AsciiEQ library folder (the app refreshes its picker).</summary>
    public event Action<AsciiScene>? SavedToLibrary;

    /// <param name="libraryFolder">AsciiEQ's scene folder, when opened from the app; enables "Add to my AsciiEQ scenes".</param>
    public SceneStudioWindow(string? libraryFolder = null)
    {
        InitializeComponent();
        // Standalone there is no AsciiEQ theme to inherit, so bring the studio's own palette. Inside
        // the app the keys already exist (and follow the user's theme), so nothing is added.
        if (Application.Current?.TryFindResource("AccentBrush") is null)
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/AsciiEQ.SceneStudio;component/StudioPalette.xaml") });
        _libraryFolder = libraryFolder;
        if (libraryFolder is not null) AddToAppButton.Visibility = Visibility.Visible;

        PatternBox.ItemsSource = Enum.GetValues<ScenePattern>();
        CharsetBox.ItemsSource = Enum.GetValues<SceneCharset>();
        for (int i = 0; i < 4; i++) ColorRows.Children.Add(MakeColorRow(i));
        BpmSlider.ValueChanged += (_, _) => BpmLabel.Text = ((int)BpmSlider.Value).ToString();
        AppOpacityBox.Checked += (_, _) => RenderNow();
        AppOpacityBox.Unchecked += (_, _) => RenderNow();
        TestBeatBox.Checked += (_, _) => RenderNow();
        TestBeatBox.Unchecked += (_, _) => RenderNow();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { Save_Click(this, new RoutedEventArgs()); e.Handled = true; }
        };
        Closing += (_, e) =>
        {
            if (!_dirty) return;
            var r = MessageBox.Show(this, "Save your scene before closing?", "Scene Studio", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (r == MessageBoxResult.Yes && !Save()) e.Cancel = true;
        };
        Closed += (_, _) => _timer!.Stop();

        LoadSceneIntoControls();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(66), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher);
        _timer.Start();
    }

    /// <summary>Opens the editor on an existing scene (a copy, so the original is untouched).</summary>
    public void Edit(AsciiScene scene, string? path = null)
    {
        _scene = AsciiScene.FromJson(scene.ToJson()) ?? new AsciiScene();
        _path = path;
        LoadSceneIntoControls();
        _dirty = false;
        UpdateTitle();
    }

    // ── Controls ↔ scene ──

    private DockPanel MakeColorRow(int index)
    {
        var swatch = new Border { Width = 24, Height = 24, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand, Tag = index, ToolTip = "Click for a palette, or type a #RRGGBB (empty = unused)" };
        swatch.SetResourceReference(Border.BorderBrushProperty, "SecondaryBrush");
        swatch.SetResourceReference(Border.CornerRadiusProperty, "Corner.Small");
        swatch.MouseLeftButtonDown += Swatch_Click;
        var box = new TextBox { MaxLength = 7, Tag = index };
        box.TextChanged += Color_Changed;
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        row.Children.Add(swatch);
        row.Children.Add(box);
        _colorRows.Add((swatch, box));
        return row;
    }

    private void LoadSceneIntoControls()
    {
        _loading = true;
        NameBox.Text = _scene.Name;
        AuthorBox.Text = _scene.Author;
        DescriptionBox.Text = _scene.Description;
        PatternBox.SelectedItem = _scene.Pattern;
        CharsetBox.SelectedItem = _scene.Charset;
        BgBox.Text = _scene.Background;
        for (int i = 0; i < 4; i++) _colorRows[i].Box.Text = i < _scene.Colors.Count ? _scene.Colors[i] : "";
        ColumnsSlider.Value = _scene.Columns;
        OpacitySlider.Value = _scene.Opacity;
        ReactiveBox.IsChecked = _scene.Reactive;
        SpeedSlider.Value = _scene.Speed;
        BassSlider.Value = _scene.BassAmount;
        BeatSlider.Value = _scene.BeatAmount;
        TrebleSlider.Value = _scene.TrebleAmount;
        _loading = false;
        ReadControls();
        _bitmap = null;
        RenderNow();
    }

    private void ReadControls()
    {
        if (_loading) return;
        _scene.Name = NameBox.Text;
        _scene.Author = AuthorBox.Text;
        _scene.Description = DescriptionBox.Text;
        if (PatternBox.SelectedItem is ScenePattern p) _scene.Pattern = p;
        if (CharsetBox.SelectedItem is SceneCharset c) _scene.Charset = c;
        _scene.Background = SceneColor.Clean(BgBox.Text, _scene.Background);
        var colors = _colorRows.Select(r => SceneColor.Clean(r.Box.Text, "")).Where(s => s.Length > 0).ToList();
        if (colors.Count > 0) _scene.Colors = colors;
        _scene.Columns = (int)ColumnsSlider.Value;
        _scene.Opacity = OpacitySlider.Value;
        _scene.Reactive = ReactiveBox.IsChecked == true;
        _scene.Speed = SpeedSlider.Value;
        _scene.BassAmount = BassSlider.Value;
        _scene.BeatAmount = BeatSlider.Value;
        _scene.TrebleAmount = TrebleSlider.Value;

        ColumnsLabel.Text = _scene.Columns.ToString();
        OpacityLabel.Text = $"{_scene.Opacity * 100:0}%";
        MotionPanel.IsEnabled = _scene.Reactive;
        MotionPanel.Opacity = _scene.Reactive ? 1 : 0.4;
        PictureButton.Visibility = _scene.Pattern == ScenePattern.Image ? Visibility.Visible : Visibility.Collapsed;
        BgSwatch.Background = Brush(_scene.Background);
        foreach (var (swatch, box) in _colorRows)
        {
            var clean = SceneColor.Clean(box.Text, "");
            swatch.Background = clean.Length > 0 ? Brush(clean) : Brushes.Transparent;
        }
    }

    private static SolidColorBrush Brush(string hex)
    {
        var (r, g, b) = SceneColor.Parse(hex);
        return new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ReadControls();
        MarkDirty();
        if (!_scene.Reactive) RenderNow();
    }

    private void Color_Changed(object sender, TextChangedEventArgs e) => Field_Changed(sender, e);

    private void Swatch_Click(object sender, MouseButtonEventArgs e)
    {
        // A tiny palette: click a chip to fill the box (hex can always be typed by hand).
        var palette = new[] { "#0B0E14", "#111111", "#1E293B", "#22C55E", "#A7F3D0", "#3B82F6", "#BFDBFE", "#8B5CF6", "#DDD6FE", "#EC4899", "#FBCFE8", "#EF4444", "#FECACA", "#F59E0B", "#FDE68A", "#14B8A6", "#CCFBF1", "#FFFFFF", "#9CA3AF" };
        var menu = new ContextMenu();
        var wrap = new WrapPanel { Width = 7 * 26 };
        foreach (var hex in palette)
        {
            var chip = new Border { Width = 22, Height = 22, Margin = new Thickness(2), CornerRadius = new CornerRadius(4), Background = Brush(hex), Cursor = Cursors.Hand, ToolTip = hex, BorderThickness = new Thickness(1) };
            chip.SetResourceReference(Border.BorderBrushProperty, "SecondaryBrush");
            chip.MouseLeftButtonDown += (_, _) =>
            {
                if (sender is Border { Tag: "bg" }) BgBox.Text = hex;
                else if (sender is Border { Tag: int i }) _colorRows[i].Box.Text = hex;
                menu.IsOpen = false;
            };
            wrap.Children.Add(chip);
        }
        menu.Items.Add(new MenuItem { Header = wrap, StaysOpenOnClick = true });
        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
    }

    private void Seed_Click(object sender, RoutedEventArgs e)
    {
        _scene.Seed = Random.Shared.Next(1, 100000);
        MarkDirty();
        RenderNow();
    }

    private void Picture_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Choose a picture", Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            _scene.ImageBase64 = SceneBitmap.EncodeImage(dlg.FileName);
            _scene.Id = Guid.NewGuid().ToString("N")[..12]; // the renderer caches the picture by id
            MarkDirty();
            _bitmap = null;
            RenderNow();
            Status($"Picture added ({_scene.ImageBase64.Length * 3 / 4 / 1024} KB inside the scene).");
        }
        catch (Exception ex) { Status("Couldn't use that picture: " + ex.Message); }
    }

    // ── Preview ──

    private SceneAudio FakeAudio()
    {
        if (!_scene.Reactive || TestBeatBox.IsChecked != true) return SceneAudio.Silence;
        double t = _clock.Elapsed.TotalSeconds;
        double beatLen = 60.0 / Math.Max(1, BpmSlider.Value);
        double phase = (t % beatLen) / beatLen;             // 0 at each kick
        double kick = Math.Exp(-phase * 6);
        double bass = 0.25 + 0.7 * kick;
        double mid = 0.4 + 0.15 * Math.Sin(t * 1.7);
        double treble = 0.3 + 0.25 * Math.Abs(Math.Sin(t * 5.3)) + (phase > 0.5 && phase < 0.56 ? 0.3 : 0);
        double beat = phase < 0.18 ? 1 - phase / 0.18 : 0;
        return new SceneAudio(bass, mid, treble, beat);
    }

    private void Tick()
    {
        if (!_scene.Reactive || !IsVisible || WindowState == WindowState.Minimized) return;
        RenderNow();
    }

    private void RenderNow()
    {
        if (Preview is null) return;
        int w = 960, h = 540;
        try
        {
            double? opacity = AppOpacityBox.IsChecked == true ? null : 1;
            _bitmap = _renderer.Render(_scene, w, h, _clock.Elapsed.TotalSeconds, FakeAudio(), _bitmap, opacity);
            if (!ReferenceEquals(Preview.Source, _bitmap)) Preview.Source = _bitmap;
        }
        catch (Exception ex) { Status("Preview failed: " + ex.Message); }
    }

    // ── Files ──

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty && MessageBox.Show(this, "Discard the current scene?", "Scene Studio", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _scene = new AsciiScene { Name = "My Scene", Author = _scene.Author };
        _path = null;
        LoadSceneIntoControls();
        _dirty = false;
        UpdateTitle();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Open scene", Filter = "AsciiEQ scenes (*.asciiscene)|*.asciiscene|All files|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var scene = AsciiScene.FromJson(File.ReadAllText(dlg.FileName));
            if (scene is null) { Status("That file isn't a scene."); return; }
            _scene = scene;
            _path = dlg.FileName;
            LoadSceneIntoControls();
            _dirty = false;
            UpdateTitle();
            Status("Opened " + Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status("Couldn't open: " + ex.Message); }
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();
    private void SaveAs_Click(object sender, RoutedEventArgs e) => Save(forceDialog: true);

    private bool Save(bool forceDialog = false)
    {
        ReadControls();
        if (string.IsNullOrWhiteSpace(_scene.Name)) { Status("Give the scene a name first."); NameBox.Focus(); return false; }
        if (_path is null || forceDialog)
        {
            var dlg = new SaveFileDialog { Title = "Save scene", Filter = "AsciiEQ scenes (*.asciiscene)|*.asciiscene", FileName = SafeName(_scene.Name) + SceneLibrary.Extension };
            if (dlg.ShowDialog(this) != true) return false;
            _path = dlg.FileName;
        }
        try
        {
            _scene.Normalize();
            SceneLibrary.Export(_scene, _path);
            var png = Path.ChangeExtension(_path, ".png");
            _renderer.SavePng(_scene, 640, 360, png);
            _dirty = false;
            UpdateTitle();
            Status($"Saved {Path.GetFileName(_path)} and {Path.GetFileName(png)} (the preview for the gallery).");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status("Couldn't save: " + ex.Message);
            return false;
        }
    }

    private void AddToApp_Click(object sender, RoutedEventArgs e)
    {
        if (_libraryFolder is null) return;
        ReadControls();
        if (string.IsNullOrWhiteSpace(_scene.Name)) { Status("Give the scene a name first."); NameBox.Focus(); return; }
        try
        {
            var lib = new SceneLibrary(_libraryFolder);
            var saved = lib.Save(_scene);
            _scene.Id = saved.Id;
            _dirty = false;
            UpdateTitle();
            SavedToLibrary?.Invoke(saved);
            Status($"\"{saved.Name}\" is now in your AsciiEQ scenes (Themes page).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status("Couldn't save: " + ex.Message); }
    }

    private void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        ReadControls();
        var dlg = new SaveFileDialog { Title = "Export wallpaper", Filter = "PNG picture|*.png", FileName = SafeName(_scene.Name) + "-wallpaper.png" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            int w = (int)(SystemParameters.PrimaryScreenWidth * dpi), h = (int)(SystemParameters.PrimaryScreenHeight * dpi);
            _renderer.SavePng(_scene, w, h, dlg.FileName);
            Status($"Saved a {w}×{h} wallpaper. Right-click it in Explorer → Set as desktop background.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status("Couldn't save: " + ex.Message); }
    }

    private void Share_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(GalleryUrl + "#studio") { UseShellExecute = true }); } catch { }
        Status("Upload the .asciiscene and the .png that Save made. Scenes are reviewed before they appear.");
    }

    private static string SafeName(string name)
    {
        var s = string.Concat(name.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_')).Trim();
        return s.Length == 0 ? "scene" : s;
    }

    private void MarkDirty() { _dirty = true; UpdateTitle(); }
    private void UpdateTitle() => Title = $"Scene Studio — {(_scene.Name.Length > 0 ? _scene.Name : "Untitled")}{(_dirty ? " *" : "")}";
    private void Status(string text) => StatusText.Text = text;
}
