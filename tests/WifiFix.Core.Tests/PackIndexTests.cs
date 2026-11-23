using System;
using System.IO;
using System.Linq;
using WifiFix.Core;
using Xunit;

public class PackIndexTests
{
    static string Inf(string id) =>
        "[Manufacturer]\n%M%=Sec,NTamd64\n[Sec.NTamd64]\n%D%=Inst, " + id + "\n";

    static string TempRoot()
    {
        var d = Path.Combine(Path.GetTempPath(), "wf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    static void Put(string root, string rel, string text)
    {
        var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    [Fact]
    public void Build_indexes_every_inf_under_root()
    {
        var root = TempRoot();
        Put(root, "intel/ax200/a.inf", Inf(@"PCI\VEN_8086&DEV_2723"));
        Put(root, "realtek/rtl/b.inf", Inf(@"PCI\VEN_10EC&DEV_8822"));
        var idx = PackIndex.Build(root);
        Assert.Equal("intel/ax200/a.inf", idx.FindInf(new[] { @"PCI\VEN_8086&DEV_2723" }));
        Assert.Equal("realtek/rtl/b.inf", idx.FindInf(new[] { @"PCI\VEN_10EC&DEV_8822" }));
    }

    [Fact]
    public void Exact_match_wins_over_stripped_match()
    {
        var idx = new PackIndex();
        idx.Add("generic/a.inf", new[] { @"PCI\VEN_8086&DEV_2723" });
        idx.Add("specific/b.inf", new[] { @"PCI\VEN_8086&DEV_2723&SUBSYS_00848086" });
        Assert.Equal("specific/b.inf",
            idx.FindInf(new[] { @"PCI\VEN_8086&DEV_2723&SUBSYS_00848086&REV_1A" , @"PCI\VEN_8086&DEV_2723&SUBSYS_00848086" }));
    }

    [Fact]
    public void Device_id_with_subsys_and_rev_matches_index_entry_without_them()
    {
        var idx = new PackIndex();
        idx.Add("intel/a.inf", new[] { @"PCI\VEN_8086&DEV_2723" });
        Assert.Equal("intel/a.inf",
            idx.FindInf(new[] { @"pci\ven_8086&dev_2723&subsys_12345678&rev_1a" }));
    }

    [Fact]
    public void FindAll_returns_every_candidate_newest_driver_first()
    {
        var idx = new PackIndex();
        var id = @"PCI\VEN_8086&DEV_2723";
        idx.Add("old/a.inf", new[] { id }, new DateTime(2019, 1, 1));
        idx.Add("new/b.inf", new[] { id }, new DateTime(2023, 1, 1));
        Assert.Equal(new[] { "new/b.inf", "old/a.inf" }, idx.FindAll(new[] { id }).ToArray());
        Assert.Equal("new/b.inf", idx.FindInf(new[] { id }));
    }

    [Fact]
    public void Unknown_id_returns_null()
    {
        var idx = new PackIndex();
        idx.Add("intel/a.inf", new[] { @"PCI\VEN_8086&DEV_2723" });
        Assert.Null(idx.FindInf(new[] { @"PCI\VEN_1234&DEV_5678" }));
    }

    [Fact]
    public void Save_then_Load_roundtrips()
    {
        var root = TempRoot();
        var idx = new PackIndex();
        idx.Add("intel/a b/é.inf", new[] { @"PCI\VEN_8086&DEV_2723", @"USB\VID_0BDA&PID_8179" });
        var path = Path.Combine(root, "index.json");
        idx.Save(path);
        var loaded = PackIndex.Load(path);
        Assert.Equal("intel/a b/é.inf", loaded.FindInf(new[] { @"USB\VID_0BDA&PID_8179" }));
    }
}
