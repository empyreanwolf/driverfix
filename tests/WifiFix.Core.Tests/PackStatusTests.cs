using System;
using System.IO;
using WifiFix.Core;
using Xunit;

public class PackStatusTests
{
    static string Root() { var d = Path.Combine(Path.GetTempPath(), "ps_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

    [Fact]
    public void Missing_folder_is_empty_and_tells_user_to_update()
    {
        var s = PackStatus.Read(Path.Combine(Root(), "nope"));
        Assert.Equal(0, s.Packages);
        Assert.Null(s.LastChecked);
        Assert.Contains("empty", s.Describe());
    }

    [Fact]
    public void Counts_downloaded_packages_and_reports_last_check_time()
    {
        var root = Root();
        File.WriteAllLines(Path.Combine(root, "pack-state.txt"), new[] { "a", "b", "" });
        File.WriteAllText(Path.Combine(root, "PACK-REPORT.txt"), "x");
        File.SetLastWriteTime(Path.Combine(root, "PACK-REPORT.txt"), new DateTime(2026, 11, 23, 10, 25, 0));
        var s = PackStatus.Read(root);
        Assert.Equal(2, s.Packages);
        Assert.Equal(new DateTime(2026, 11, 23, 10, 25, 0), s.LastChecked);
        Assert.Contains("2 downloaded", s.Describe());
        Assert.Contains("2026-11-23 10:25", s.Describe());
    }

    [Fact]
    public void Version_is_a_dotted_number() => Assert.Matches(@"^\d+\.\d+", AppInfo.Version);
}
