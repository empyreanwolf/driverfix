using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WifiFix.Core;
using Xunit;

public class PackUpdaterTests
{
    class FakeCatalog : ICatalogSource
    {
        public Dictionary<string, CatalogRow> Rows = new();
        public HashSet<string> Throw = new();
        public List<string> Downloaded = new();
        public CatalogRow? FindBest(string id)
        {
            if (Throw.Contains(id)) throw new Exception("boom " + id);
            return Rows.TryGetValue(id, out var r) ? r : null;
        }
        public void Download(string updateId, string folder)
        {
            Downloaded.Add(updateId);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "x.inf"), "x");
        }
    }

    static CatalogRow Row(string guid, string ver = "1.0") =>
        new(guid, "Intel net Driver Update (" + ver + ")", "Windows 10", "Drivers (Networking)", "1/1/2026", ver);

    static string Root()
    {
        var d = Path.Combine(Path.GetTempPath(), "pu_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    static readonly KnownChip A = new("Intel", "Chip A", new[] { @"PCI\VEN_8086&DEV_0001" });
    static readonly KnownChip B = new("Intel", "Chip B", new[] { @"PCI\VEN_8086&DEV_0002" });
    const string G1 = "aaaaaaaa-1111-1111-1111-111111111111";
    const string G2 = "bbbbbbbb-2222-2222-2222-222222222222";

    [Fact]
    public void Downloads_new_driver_into_vendor_folder_and_records_state()
    {
        var root = Root(); var cat = new FakeCatalog(); cat.Rows[A.HardwareIds[0]] = Row(G1);
        var rep = new PackUpdater(cat, new NullLog()).Run(root, new[] { A });
        Assert.Equal(1, rep.Downloaded);
        Assert.Single(Directory.GetDirectories(Path.Combine(root, "win10", "intel")));
        Assert.Contains(G1, File.ReadAllText(Path.Combine(root, "pack-state.txt")));
    }

    [Fact]
    public void Second_run_downloads_nothing_when_up_to_date()
    {
        var root = Root(); var cat = new FakeCatalog(); cat.Rows[A.HardwareIds[0]] = Row(G1);
        new PackUpdater(cat, new NullLog()).Run(root, new[] { A });
        var rep = new PackUpdater(cat, new NullLog()).Run(root, new[] { A });
        Assert.Equal(0, rep.Downloaded);
        Assert.Equal(1, rep.UpToDate);
        Assert.Single(cat.Downloaded);
    }

    [Fact]
    public void One_update_covering_two_chips_downloads_once()
    {
        var root = Root(); var cat = new FakeCatalog();
        cat.Rows[A.HardwareIds[0]] = Row(G1); cat.Rows[B.HardwareIds[0]] = Row(G1);
        var rep = new PackUpdater(cat, new NullLog()).Run(root, new[] { A, B });
        Assert.Equal(1, rep.Downloaded);
        Assert.Equal(1, rep.UpToDate);
    }

    [Fact]
    public void Newer_catalog_update_is_downloaded_again()
    {
        var root = Root(); var cat = new FakeCatalog(); cat.Rows[A.HardwareIds[0]] = Row(G1, "1.0");
        new PackUpdater(cat, new NullLog()).Run(root, new[] { A });
        cat.Rows[A.HardwareIds[0]] = Row(G2, "2.0");
        var rep = new PackUpdater(cat, new NullLog()).Run(root, new[] { A });
        Assert.Equal(1, rep.Downloaded);
        Assert.Equal(new[] { G1, G2 }, cat.Downloaded.ToArray());
    }

    [Fact]
    public void Not_found_and_errors_are_counted_and_do_not_stop_the_run()
    {
        var root = Root(); var cat = new FakeCatalog();
        var broken = new KnownChip("Intel", "Broken", new[] { @"PCI\VEN_8086&DEV_BAD0" });
        cat.Throw.Add(broken.HardwareIds[0]);
        cat.Rows[B.HardwareIds[0]] = Row(G2);
        var rep = new PackUpdater(cat, new NullLog()).Run(root, new[] { A, broken, B });
        Assert.Equal(1, rep.NotFound);
        Assert.Equal(1, rep.Errors);
        Assert.Equal(1, rep.Downloaded);
        Assert.True(File.Exists(Path.Combine(root, "PACK-REPORT.txt")));
    }

    [Fact]
    public void Read_only_root_is_reported_not_thrown()
    {
        var root = Path.Combine(Root(), "file-not-folder");
        File.WriteAllText(root, "x"); // a FILE where the folder should be
        var rep = new PackUpdater(new FakeCatalog(), new NullLog()).Run(root, new[] { A });
        Assert.Equal(1, rep.Errors);
    }
}

public class PackUpdaterQuietTests
{
    class CapLog : ILog { public System.Collections.Generic.List<string> Lines = new(); public void Info(string m) => Lines.Add(m); public void Warn(string m) => Lines.Add(m); }
    class NoneCatalog : ICatalogSource { public CatalogRow? FindBest(string id) => null; public void Download(string u, string f) { } }

    [Fact]
    public void Chips_without_a_driver_are_not_shown_to_the_user_only_in_the_report()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pq_" + System.Guid.NewGuid().ToString("N"));
        var log = new CapLog();
        var chip = new KnownChip("Intel", "Old Chip", new[] { @"PCI\VEN_8086&DEV_0001" });
        new PackUpdater(new NoneCatalog(), log).Run(root, new[] { chip });
        Assert.DoesNotContain(log.Lines, l => l.Contains("catalog") || l.Contains("manually") || l.Contains("Old Chip"));
        Assert.Contains("Old Chip", System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PACK-REPORT.txt")));
    }
}
