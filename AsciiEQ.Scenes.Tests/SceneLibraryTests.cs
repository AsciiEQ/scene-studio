using System.IO;

namespace AsciiEQ.Scenes.Tests;

public class SceneLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "asciieq-lib-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void MissingFolderIsJustBuiltIns()
    {
        var lib = new SceneLibrary(_dir);
        Assert.Equal(BuiltInScenes.All.Count, lib.All.Count);
        Assert.Empty(lib.UserScenes);
        Assert.NotNull(lib.Find("builtin:rain"));
    }

    [Fact]
    public void SaveImportExportDelete()
    {
        var lib = new SceneLibrary(_dir);
        var mine = new AsciiScene { Name = "Mine", Pattern = ScenePattern.Pulse, Reactive = true, Author = "me" };
        var saved = lib.Save(mine);
        Assert.Single(lib.UserScenes);
        Assert.Equal(saved.Id, lib.Find(saved.Id)!.Id);
        Assert.Single(Directory.GetFiles(_dir, "*.asciiscene"));

        // Saving again with the same id replaces, not doubles.
        saved.Name = "Mine 2";
        lib.Save(saved);
        Assert.Single(lib.UserScenes);
        Assert.Equal("Mine 2", lib.Find(saved.Id)!.Name);

        // Export then import into a second library.
        var file = Path.Combine(_dir, "out.json");
        SceneLibrary.Export(saved, file);
        var dir2 = _dir + "-b";
        var lib2 = new SceneLibrary(dir2);
        var imported = lib2.Import(file);
        Assert.NotNull(imported);
        Assert.Equal(saved.Id, imported!.Id);
        Assert.Single(lib2.UserScenes);
        Directory.Delete(dir2, true);

        Assert.True(lib.Delete(saved.Id));
        Assert.Empty(lib.UserScenes);
        Assert.False(lib.Delete("builtin:rain"));
    }

    [Fact]
    public void SavingABuiltInMakesACopy()
    {
        var lib = new SceneLibrary(_dir);
        var copy = lib.Save(BuiltInScenes.Find("builtin:tide")!);
        Assert.False(BuiltInScenes.IsBuiltIn(copy.Id));
        Assert.Equal("Tide", copy.Name);
        Assert.Equal(BuiltInScenes.All.Count + 1, lib.All.Count);
    }

    [Fact]
    public void BadFilesAreSkipped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "junk.asciiscene"), "not json");
        File.WriteAllText(Path.Combine(_dir, "fake.asciiscene"), "{\"Name\":\"x\",\"Id\":\"builtin:rain\"}"); // can't shadow a built-in
        var lib = new SceneLibrary(_dir);
        Assert.Empty(lib.UserScenes);
        Assert.Null(lib.Import(Path.Combine(_dir, "junk.asciiscene")));
    }
}
