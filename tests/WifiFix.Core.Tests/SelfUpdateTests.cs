using System;
using System.IO;
using WifiFix.Core;
using Xunit;

public class SelfUpdateTests
{
    const string Json = @"{""url"":""x"",""tag_name"":""v1.10"",""assets"":[
      {""name"":""README.txt"",""browser_download_url"":""https://github.com/o/r/releases/download/v1.10/README.txt""},
      {""name"":""DriverFix.exe"",""browser_download_url"":""https://github.com/o/r/releases/download/v1.10/DriverFix.exe""}]}";

    [Fact]
    public void Parses_tag_and_exe_asset_ignoring_other_assets()
    {
        var r = ReleaseParser.Parse(Json)!;
        Assert.Equal("1.10", r.Version);
        Assert.EndsWith("/DriverFix.exe", r.DownloadUrl);
    }

    [Fact]
    public void Release_without_exe_asset_or_tag_is_null()
    {
        Assert.Null(ReleaseParser.Parse(@"{""tag_name"":""v2.0"",""assets"":[]}"));
        Assert.Null(ReleaseParser.Parse(@"{""message"":""Not Found""}"));
        Assert.Null(ReleaseParser.Parse(""));
    }

    [Theory]
    [InlineData("1.8", "1.9", true)]
    [InlineData("1.9", "1.10", true)]     // numeric, not text, comparison
    [InlineData("1.8", "1.8", false)]
    [InlineData("1.9", "1.8", false)]
    [InlineData("1.8", "garbage", false)]
    public void IsNewer_compares_numerically(string cur, string latest, bool expected) =>
        Assert.Equal(expected, ReleaseParser.IsNewer(cur, latest));

    static string Dir() { var d = Path.Combine(Path.GetTempPath(), "su_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }
    static byte[] Exe(int size) { var b = new byte[size]; b[0] = (byte)'M'; b[1] = (byte)'Z'; return b; }

    [Fact]
    public void Swap_replaces_exe_keeps_old_copy_and_cleanup_removes_it()
    {
        var d = Dir(); var cur = Path.Combine(d, "DriverFix.exe"); var dl = Path.Combine(d, "DriverFix.new");
        File.WriteAllText(cur, "old"); File.WriteAllBytes(dl, Exe(200_000));
        SelfUpdate.Swap(cur, dl);
        Assert.Equal(200_000, new FileInfo(cur).Length);
        Assert.Equal("old", File.ReadAllText(Path.Combine(d, "DriverFix.old.exe")));
        Assert.False(File.Exists(dl));
        SelfUpdate.CleanupOld(d);
        Assert.False(File.Exists(Path.Combine(d, "DriverFix.old.exe")));
    }

    [Fact]
    public void Swap_rejects_a_bad_download_and_leaves_current_exe_alone()
    {
        var d = Dir(); var cur = Path.Combine(d, "DriverFix.exe"); var dl = Path.Combine(d, "DriverFix.new");
        File.WriteAllText(cur, "old");
        File.WriteAllText(dl, "<html>rate limited</html>");           // not an exe
        Assert.Throws<InvalidDataException>(() => SelfUpdate.Swap(cur, dl));
        Assert.Equal("old", File.ReadAllText(cur));
        File.WriteAllBytes(dl, Exe(500));                              // MZ but far too small
        Assert.Throws<InvalidDataException>(() => SelfUpdate.Swap(cur, dl));
        Assert.Equal("old", File.ReadAllText(cur));
    }
}
