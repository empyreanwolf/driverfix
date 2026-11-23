using System;
using System.IO;
using WifiFix.Core;
using Xunit;

public class SelfUpdateTests
{
    [Fact]
    public void Version_file_gives_version_and_exe_url_in_the_repo()
    {
        var r = ReleaseParser.FromVersionFile("1.10\r\n")!;
        Assert.Equal("1.10", r.Version);
        Assert.Equal("https://raw.githubusercontent.com/" + AppInfo.Repo + "/main/latest/DriverFix.exe", r.DownloadUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>404: Not Found</html>")]
    [InlineData("v")]
    public void Bad_version_file_is_null(string text) => Assert.Null(ReleaseParser.FromVersionFile(text));

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

    [Fact]
    public void A_real_sized_DriverFix_exe_of_about_80KB_is_accepted()
    {
        var d = Dir(); var cur = Path.Combine(d, "DriverFix.exe"); var dl = Path.Combine(d, "DriverFix.new");
        File.WriteAllText(cur, "old"); File.WriteAllBytes(dl, Exe(79_000));
        SelfUpdate.Swap(cur, dl);
        Assert.Equal(79_000, new FileInfo(cur).Length);
    }
}
