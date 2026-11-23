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
        public Dictionary<string, List<CatalogRow>> SearchRows = new();
        public IReadOnlyList<CatalogRow> Search(string query)
        {
            if (Throw.Contains(query)) throw new Exception("boom " + query);
            return SearchRows.TryGetValue(query, out var r) ? r : new List<CatalogRow>();
        }
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
    class NoneCatalog : ICatalogSource { public System.Collections.Generic.IReadOnlyList<CatalogRow> Search(string q) => new CatalogRow[0]; public CatalogRow? FindBest(string id) => null; public void Download(string u, string f) { } }

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

public class PackSweepTests
{
    class Cat : ICatalogSource
    {
        public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<CatalogRow>> Rows = new();
        public System.Collections.Generic.HashSet<string> Throw = new();
        public System.Collections.Generic.List<string> Downloaded = new();
        public System.Collections.Generic.IReadOnlyList<CatalogRow> Search(string q)
        {
            if (Throw.Contains(q)) throw new System.Exception("boom");
            return Rows.TryGetValue(q, out var r) ? r : new System.Collections.Generic.List<CatalogRow>();
        }
        public CatalogRow? FindBest(string id) => null;
        public void Download(string id, string folder) { Downloaded.Add(id); System.IO.Directory.CreateDirectory(folder); System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "a.inf"), "x"); }
    }
    class NoLog : ILog { public void Info(string m) { } public void Warn(string m) { } }
    static CatalogRow R(string id, string title, string date, string products = "Windows 10 and later drivers", string cls = "Drivers (Networking)") =>
        new(id, title, products, cls, date, "n/a");
    static string Root() { var d = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sw_" + System.Guid.NewGuid().ToString("N")); System.IO.Directory.CreateDirectory(d); return d; }
    static readonly SweepQuery Q = new("intel", "Intel net Driver Update");
    const string A = "aaaaaaaa-0000-0000-0000-000000000001", B = "bbbbbbbb-0000-0000-0000-000000000002", C = "cccccccc-0000-0000-0000-000000000003", D = "dddddddd-0000-0000-0000-000000000004";

    [Fact]
    public void Keeps_only_the_newest_update_per_driver_family_and_only_windows10_drivers()
    {
        var cat = new Cat();
        cat.Rows[Q.Query] = new() {
            R(A, "Intel - net - 22.200.0.6", "1/10/2023"),
            R(B, "Intel - net - 24.40.0.4", "4/15/2026"),                 // same family, newer -> only this one
            R(C, "Realtek - Net - 2024.0.4.208", "1/1/2025"),             // different family -> also downloaded
            R(D, "Intel - net - 25.0.0.1", "5/1/2026", "Windows 7 drivers"),   // not Windows 10 -> ignored
        };
        var rep = new PackUpdater(cat, new NoLog()).Sweep(Root(), new[] { Q });
        Assert.Equal(2, rep.Downloaded);
        Assert.Equal(new[] { B, C }, cat.Downloaded.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Already_downloaded_updates_are_up_to_date_and_errors_do_not_stop_the_sweep()
    {
        var cat = new Cat(); var root = Root();
        cat.Rows[Q.Query] = new() { R(B, "Intel - net - 24.40.0.4", "4/15/2026") };
        new PackUpdater(cat, new NoLog()).Sweep(root, new[] { Q });
        var bad = new SweepQuery("realtek", "bad query"); cat.Throw.Add(bad.Query);
        var rep = new PackUpdater(cat, new NoLog()).Sweep(root, new[] { bad, Q });
        Assert.Equal(0, rep.Downloaded);
        Assert.Equal(1, rep.UpToDate);
        Assert.Equal(1, rep.Errors);
    }

    [Fact]
    public void Sweep_queries_cover_all_vendors_and_device_types()
    {
        var cats = SweepQueries.All.Select(q => q.Category).Distinct().ToList();
        foreach (var c in new[] { "intel", "realtek", "mediatek", "qualcomm", "broadcom", "audio", "bluetooth", "touchpad" }) Assert.Contains(c, cats);
    }
}
