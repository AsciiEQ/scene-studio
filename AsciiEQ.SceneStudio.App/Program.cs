using System.IO;
using System.Windows;
using AsciiEQ.Scenes;
using AsciiEQ.SceneStudio;

namespace AsciiEQ.SceneStudio.App;

/// <summary>Standalone Scene Studio: `SceneStudio.exe [file.asciiscene]`.</summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        // If AsciiEQ is installed, its scene folder gets the "Add to my AsciiEQ scenes" button.
        var library = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AsciiEQ", "scenes");
        var window = new SceneStudioWindow(Directory.Exists(Path.GetDirectoryName(library)!) ? library : null);
        if (args.Length > 0 && File.Exists(args[0]))
        {
            var scene = AsciiScene.FromJson(File.ReadAllText(args[0]));
            if (scene is not null) window.Edit(scene, args[0]);
        }
        app.MainWindow = window;
        window.Show();
        return app.Run();
    }
}
