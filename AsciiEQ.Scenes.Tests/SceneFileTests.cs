namespace AsciiEQ.Scenes.Tests;

public class SceneFileTests
{
    [Fact]
    public void RoundTripsThroughJson()
    {
        var s = BuiltInScenes.All[7];
        var back = AsciiScene.FromJson(s.ToJson());
        Assert.NotNull(back);
        Assert.Equal(s.Id, back!.Id);
        Assert.Equal(s.Name, back.Name);
        Assert.Equal(s.Pattern, back.Pattern);
        Assert.Equal(s.Charset, back.Charset);
        Assert.Equal(s.Colors, back.Colors);
        Assert.Equal(s.Reactive, back.Reactive);
        Assert.Equal(s.Seed, back.Seed);
    }

    [Fact]
    public void TwelveBuiltIns_SixStillSixLive_UniqueIds()
    {
        Assert.Equal(12, BuiltInScenes.All.Count);
        Assert.Equal(6, BuiltInScenes.All.Count(s => !s.Reactive));
        Assert.Equal(6, BuiltInScenes.All.Count(s => s.Reactive));
        Assert.Equal(12, BuiltInScenes.All.Select(s => s.Id).Distinct().Count());
        Assert.All(BuiltInScenes.All, s => Assert.True(BuiltInScenes.IsBuiltIn(s.Id)));
        Assert.Same(BuiltInScenes.All[0], BuiltInScenes.Find(BuiltInScenes.All[0].Id));
        Assert.Null(BuiltInScenes.Find("builtin:nope"));
    }

    [Fact]
    public void HostileValuesAreClamped()
    {
        var json = """
        {"Name":"  x  ","Pattern":"Rain","Columns":99999,"Opacity":9,"Speed":-4,"BassAmount":"NaN",
         "Colors":["zzz","#ff0000","00ff00","#1","#aaa","#bbbbbb","#cccccc","#dddddd"],"Background":"red","Seed":5,
         "Reactive":true,"ImageBase64":"QUJD"}
        """;
        var s = AsciiScene.FromJson(json);
        Assert.NotNull(s);
        Assert.Equal("x", s!.Name);
        Assert.Equal(240, s.Columns);
        Assert.Equal(1, s.Opacity);
        Assert.Equal(0, s.Speed);
        Assert.Equal("#0B0E14", s.Background);
        Assert.Equal(["#FF0000", "#00FF00", "#BBBBBB", "#CCCCCC"], s.Colors);
        Assert.Null(s.ImageBase64); // not an Image scene
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"Name\":\"\"}")]
    [InlineData("[1,2]")]
    public void BadTextIsNull(string text) => Assert.Null(AsciiScene.FromJson(text));

    [Fact]
    public void EmptyObjectIsADefaultScene()
    {
        var s = AsciiScene.FromJson("{}");
        Assert.NotNull(s);
        Assert.Equal("Untitled", s!.Name);
        Assert.Equal(ScenePattern.Noise, s.Pattern);
    }

    [Fact]
    public void EmptyColorsFallBack()
    {
        var s = AsciiScene.FromJson("{\"Name\":\"n\",\"Pattern\":\"Noise\",\"Charset\":\"Classic\",\"Colors\":[]}");
        Assert.NotNull(s);
        Assert.Equal(["#22C55E"], s!.Colors);
    }

    [Fact]
    public void CopyGetsNewId()
    {
        var s = BuiltInScenes.All[0];
        var c = s.Copy();
        Assert.NotEqual(s.Id, c.Id);
        Assert.Equal(s.Name, c.Name);
    }

    [Fact]
    public void ColorsParse()
    {
        Assert.Equal("#AABBCC", SceneColor.Clean("aabbcc", "x"));
        Assert.Equal("x", SceneColor.Clean("#aabbc", "x"));
        Assert.Equal(((byte)0x12, (byte)0x34, (byte)0x56), SceneColor.Parse("#123456"));
    }
}
