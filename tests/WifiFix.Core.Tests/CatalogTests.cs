using System;
using System.Linq;
using WifiFix.Core;
using Xunit;

public class CatalogTests
{
    const string G1 = "11111111-1111-1111-1111-111111111111";
    const string G2 = "22222222-2222-2222-2222-222222222222";
    const string G3 = "33333333-3333-3333-3333-333333333333";

    static string Row(string g, string title, string products, string cls, string date, string ver) =>
        $"<tr id='{g}_R0'><td><input type='checkbox'/></td><td><a id='{g}_link' href='x'>{title}</a></td>" +
        $"<td>{products}</td><td>{cls}</td><td>{date}</td><td>{ver}</td><td>23.1 MB</td><td></td></tr>";

    static string Html(params string[] rows) => "<table id='ctl00_catalogBody_updateMatches'>" + string.Concat(rows) + "</table>";

    [Fact]
    public void ParseSearch_reads_rows()
    {
        var rows = CatalogParser.ParseSearch(Html(
            Row(G1, "Intel net Driver Update (24.20.0.4)", "Windows 10, version 1809 and later, Servicing Drivers", "Drivers (Networking)", "1/5/2026", "24.20.0.4")));
        var r = Assert.Single(rows);
        Assert.Equal(G1, r.UpdateId);
        Assert.Equal("Intel net Driver Update (24.20.0.4)", r.Title);
        Assert.Equal("Drivers (Networking)", r.Classification);
        Assert.Equal("24.20.0.4", r.Version);
    }

    [Fact]
    public void PickBest_prefers_windows10_driver_with_highest_version()
    {
        var rows = CatalogParser.ParseSearch(Html(
            Row(G1, "old", "Windows 10, version 1809 and later, Servicing Drivers", "Drivers (Networking)", "1/5/2024", "22.10.0.1"),
            Row(G2, "win11", "Windows 11 Client, version 22H2 and later, Servicing Drivers", "Drivers (Networking)", "1/5/2026", "99.0.0.1"),
            Row(G3, "new", "Windows 10, version 1809 and later, Servicing Drivers", "Drivers (Networking)", "1/5/2026", "24.20.0.4")));
        Assert.Equal(G3, CatalogParser.PickBest(rows)!.UpdateId);
    }

    [Fact]
    public void PickBest_ignores_non_drivers_and_returns_null_when_nothing_fits()
    {
        var rows = CatalogParser.ParseSearch(Html(
            Row(G1, "KB", "Windows 10", "Security Updates", "1/5/2026", "1.0")));
        Assert.Null(CatalogParser.PickBest(rows));
    }

    [Fact]
    public void ParseDownloadUrls_finds_all_urls()
    {
        var html = "x downloadInformation[0].files[0].url = 'https://a.example/one.cab'; downloadInformation[0].files[1].url = \"https://a.example/two.cab\";";
        var urls = CatalogParser.ParseDownloadUrls(html);
        Assert.Equal(new[] { "https://a.example/one.cab", "https://a.example/two.cab" }, urls.ToArray());
    }

    [Fact]
    public void ParseSearch_on_garbage_returns_empty() => Assert.Empty(CatalogParser.ParseSearch("<html>no table</html>"));

    class P : IOnlineProvider
    {
        public bool Net = true; public string? Result; public int Calls;
        public bool HasInternet() => Net;
        public string? Download(DeviceInfo d, string w) { Calls++; return Result; }
    }
    static DeviceInfo Dev() => new("x", "", 28, new[] { "PCI\\VEN_1&DEV_2" }, new string[0]);

    [Fact]
    public void Fallback_uses_second_only_when_first_finds_nothing()
    {
        var a = new P { Result = null }; var b = new P { Result = "folder" };
        Assert.Equal("folder", new FallbackOnlineProvider(a, b).Download(Dev(), "w"));
        Assert.Equal(1, a.Calls); Assert.Equal(1, b.Calls);
        var c = new P { Result = "first" }; var d = new P { Result = "second" };
        Assert.Equal("first", new FallbackOnlineProvider(c, d).Download(Dev(), "w"));
        Assert.Equal(0, d.Calls);
    }

    [Fact]
    public void Fallback_HasInternet_true_if_either_has_it() =>
        Assert.True(new FallbackOnlineProvider(new P { Net = false }, new P { Net = true }).HasInternet());

    [Fact]
    public void MachineInfo_describes_model_and_handles_blank()
    {
        Assert.Equal("Dell Inc. Latitude 5420 (S/N ABC123)", new MachineInfo("Dell Inc.", "Latitude 5420", "ABC123").Describe());
        Assert.Equal("Unknown PC", new MachineInfo("", "", "").Describe());
    }
}
