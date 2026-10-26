using System.IO;

namespace AsciiEQ.Scenes;

/// <summary>
/// The scenes a user can pick from: the built-in ones plus every <c>.asciiscene</c> file in a
/// folder (AsciiEQ uses <c>%APPDATA%\AsciiEQ\scenes</c>). Files are named by scene id, so
/// importing the same scene twice replaces it rather than doubling it.
/// </summary>
public sealed class SceneLibrary
{
    public const string Extension = ".asciiscene";

    public string Folder { get; }
    private readonly List<AsciiScene> _user = [];

    public SceneLibrary(string folder)
    {
        Folder = folder;
        Reload();
    }

    /// <summary>Built-in scenes first, then the user's, by name.</summary>
    public IReadOnlyList<AsciiScene> All => [.. BuiltInScenes.All, .. _user];
    public IReadOnlyList<AsciiScene> UserScenes => _user;

    public AsciiScene? Find(string? id) =>
        id is null ? null : BuiltInScenes.Find(id) ?? _user.FirstOrDefault(s => s.Id == id);

    public void Reload()
    {
        _user.Clear();
        if (!Directory.Exists(Folder)) return;
        foreach (var file in Directory.EnumerateFiles(Folder, "*" + Extension).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var scene = AsciiScene.FromJson(File.ReadAllText(file));
                if (scene is null || BuiltInScenes.IsBuiltIn(scene.Id)) continue;
                if (_user.Any(s => s.Id == scene.Id)) continue;
                _user.Add(scene);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        _user.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Copies a scene file into the library. Null when the file isn't a scene.</summary>
    public AsciiScene? Import(string path)
    {
        var scene = AsciiScene.FromJson(File.ReadAllText(path));
        if (scene is null) return null;
        return Save(scene);
    }

    /// <summary>Writes a scene into the library (a built-in gets a new id, so the original stays).</summary>
    public AsciiScene Save(AsciiScene scene)
    {
        if (BuiltInScenes.IsBuiltIn(scene.Id)) scene = scene.Copy();
        scene.Normalize();
        Directory.CreateDirectory(Folder);
        File.WriteAllText(PathFor(scene.Id), scene.ToJson());
        Reload();
        return Find(scene.Id) ?? scene;
    }

    /// <summary>Writes any scene (built-in or not) to a file the user chose.</summary>
    public static void Export(AsciiScene scene, string path) => File.WriteAllText(path, scene.ToJson());

    public bool Delete(string id)
    {
        if (BuiltInScenes.IsBuiltIn(id)) return false;
        var path = PathFor(id);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        Reload();
        return true;
    }

    private string PathFor(string id)
    {
        var safe = new string(id.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (safe.Length == 0) safe = "scene";
        return Path.Combine(Folder, safe + Extension);
    }
}
