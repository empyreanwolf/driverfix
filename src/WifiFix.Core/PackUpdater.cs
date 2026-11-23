using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace WifiFix.Core
{
    public interface ICatalogSource
    {
        /// <summary>Newest Windows 10 driver for the hardware ID, or null.</summary>
        CatalogRow? FindBest(string hardwareId);
        /// <summary>All catalog rows (first page) for a text search.</summary>
        IReadOnlyList<CatalogRow> Search(string query);
        /// <summary>Downloads and extracts the update's driver files into <paramref name="folder"/>.</summary>
        void Download(string updateId, string folder);
    }

    public record UpdateReport(int Downloaded, int UpToDate, int NotFound, int Errors);

    /// <summary>A vendor-wide catalog search used to sweep up every current driver family; Category is the pack folder.</summary>
    public record SweepQuery(string Category, string Query);

    public static class SweepQueries
    {
        static SweepQuery Q(string c, string q) => new SweepQuery(c, q);
        public static readonly IReadOnlyList<SweepQuery> All = new[]
        {
            Q("intel", "Intel net Driver Update"), Q("intel", "Intel Wi-Fi"), Q("intel", "Intel Wireless-AC"),
            Q("realtek", "Realtek Semiconductor Net Driver Update"), Q("realtek", "Realtek PCIe Wireless LAN"),
            Q("mediatek", "MediaTek Net Driver Update"), Q("mediatek", "MediaTek Wi-Fi"),
            Q("qualcomm", "Qualcomm Atheros Wireless"), Q("qualcomm", "Qualcomm FastConnect"),
            Q("broadcom", "Broadcom Wireless LAN"),
            Q("audio", "Realtek High Definition Audio"), Q("audio", "Realtek Audio Driver Update"), Q("audio", "Intel Smart Sound Technology"),
            Q("audio", "Conexant SmartAudio"), Q("audio", "Cirrus Logic Audio"),
            Q("bluetooth", "Intel Wireless Bluetooth"), Q("bluetooth", "Realtek Bluetooth"), Q("bluetooth", "MediaTek Bluetooth"), Q("bluetooth", "Qualcomm Atheros Bluetooth"),
            Q("touchpad", "Synaptics Precision Touchpad"), Q("touchpad", "ELAN Touchpad"), Q("touchpad", "Intel Serial IO"), Q("touchpad", "Alps Pointing-device"),
        };
    }

    /// <summary>Refreshes the offline pack: for each known chip, fetches the newest catalog driver unless the pack already has it.</summary>
    public class PackUpdater
    {
        readonly ICatalogSource _cat;
        readonly ILog _log;
        public PackUpdater(ICatalogSource catalog, ILog log) { _cat = catalog; _log = log; }

        public UpdateReport Run(string driversRoot, IEnumerable<KnownChip> chips)
        {
            try { Directory.CreateDirectory(driversRoot); }
            catch (Exception e) { _log.Warn("Cannot write to the drivers folder: " + e.Message); return new UpdateReport(0, 0, 0, 1); }

            var statePath = Path.Combine(driversRoot, "pack-state.txt");
            var state = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { if (File.Exists(statePath)) foreach (var l in File.ReadAllLines(statePath)) if (l.Trim().Length > 0) state.Add(l.Trim()); }
            catch { /* treat as empty */ }

            int downloaded = 0, upToDate = 0, notFound = 0, errors = 0;
            var report = new StringBuilder("PACK REPORT " + DateTime.Now + "\r\n");
            foreach (var chip in chips)
            {
                var result = "notfound"; var status = "no driver available from Microsoft (skipped)";
                foreach (var id in chip.HardwareIds)
                {
                    try
                    {
                        var best = _cat.FindBest(id);
                        if (best == null) continue;
                        if (state.Contains(best.UpdateId)) { result = "uptodate"; status = "up to date: " + best.Title; break; }
                        var folder = Path.Combine(driversRoot, "win10", chip.Vendor.ToLowerInvariant(),
                            KnownChips.SafeName(best.Title) + "_" + best.UpdateId.Substring(0, 8));
                        _cat.Download(best.UpdateId, folder);
                        state.Add(best.UpdateId);
                        File.WriteAllLines(statePath, state);
                        result = "downloaded"; status = "downloaded: " + best.Title + " via " + id;
                        break;
                    }
                    catch (Exception e) { result = "error"; status = "ERROR " + e.Message; _log.Warn(chip.Name + ": " + e.Message); }
                }
                switch (result) { case "downloaded": downloaded++; break; case "uptodate": upToDate++; break; case "error": errors++; break; default: notFound++; break; }
                var line = chip.Vendor + " " + chip.Name + " -> " + status;
                if (result != "notfound") _log.Info(line);   // chips with no driver are only listed in PACK-REPORT.txt, not shown to the user
                report.Append(line).Append("\r\n");
            }
            report.Append("downloaded " + downloaded + ", up to date " + upToDate + ", not found " + notFound + ", errors " + errors + "\r\n");
            try { File.WriteAllText(Path.Combine(driversRoot, "PACK-REPORT.txt"), report.ToString()); } catch { }
            return new UpdateReport(downloaded, upToDate, notFound, errors);
        }

        const int MaxFamiliesPerQuery = 6;
        static readonly System.Text.RegularExpressions.Regex FamilyNoise = new System.Text.RegularExpressions.Regex(@"[\d\.\(\)]+");

        /// <summary>
        /// Vendor-wide sweep: for each search, keeps only Windows 10 drivers, the newest update of each driver family
        /// (title without version numbers), and downloads any the pack does not have yet.
        /// </summary>
        public UpdateReport Sweep(string driversRoot, IEnumerable<SweepQuery> queries)
        {
            try { Directory.CreateDirectory(driversRoot); }
            catch (Exception e) { _log.Warn("Cannot write to the drivers folder: " + e.Message); return new UpdateReport(0, 0, 0, 1); }
            var statePath = Path.Combine(driversRoot, "pack-state.txt");
            var state = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { if (File.Exists(statePath)) foreach (var l in File.ReadAllLines(statePath)) if (l.Trim().Length > 0) state.Add(l.Trim()); } catch { }
            int downloaded = 0, upToDate = 0, errors = 0;
            var report = new StringBuilder("SWEEP " + DateTime.Now + "\r\n");
            foreach (var q in queries)
            {
                try
                {
                    var best = _cat.Search(q.Query)
                        .Where(r => r.Products.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0
                                 && r.Classification.IndexOf("Driver", StringComparison.OrdinalIgnoreCase) >= 0)
                        .GroupBy(r => FamilyNoise.Replace(r.Title, "").Trim())
                        .Select(g => g.OrderByDescending(r => DateTime.TryParse(r.LastUpdated, System.Globalization.CultureInfo.GetCultureInfo("en-US"),
                                                              System.Globalization.DateTimeStyles.None, out var d) ? d : DateTime.MinValue).First())
                        .OrderByDescending(r => DateTime.TryParse(r.LastUpdated, System.Globalization.CultureInfo.GetCultureInfo("en-US"),
                                                              System.Globalization.DateTimeStyles.None, out var d2) ? d2 : DateTime.MinValue)
                        .Take(MaxFamiliesPerQuery).ToList();
                    foreach (var row in best)
                    {
                        if (state.Contains(row.UpdateId)) { upToDate++; continue; }
                        var folder = Path.Combine(driversRoot, "win10", q.Category.ToLowerInvariant(), KnownChips.SafeName(row.Title) + "_" + row.UpdateId.Substring(0, 8));
                        try
                        {
                            _cat.Download(row.UpdateId, folder);
                            state.Add(row.UpdateId); File.WriteAllLines(statePath, state);
                            downloaded++; _log.Info("Downloaded: " + row.Title); report.Append("downloaded: " + row.Title + "\r\n");
                        }
                        catch (Exception e) { errors++; _log.Warn(row.Title + ": " + e.Message); report.Append("ERROR " + row.Title + ": " + e.Message + "\r\n"); }
                    }
                }
                catch (Exception e) { errors++; _log.Warn(q.Query + ": " + e.Message); }
            }
            try { File.AppendAllText(Path.Combine(driversRoot, "PACK-REPORT.txt"), report.ToString()); } catch { }
            return new UpdateReport(downloaded, upToDate, 0, errors);
        }
    }

    /// <summary>Real Microsoft Update Catalog access (needs internet; Windows for expand.exe).</summary>
    public class MsCatalogSource : ICatalogSource
    {
        const string Base = "https://www.catalog.update.microsoft.com/";

        readonly string? _debugDir;
        int _debugCount;
        public MsCatalogSource(string? debugDir = null)
        {
            _debugDir = debugDir;
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
        }

        static WebClient Client()
        {
            var wc = new WebClient { Encoding = Encoding.UTF8 };
            // NO browser User-Agent: with one, the catalog answers with a "use Internet Explorer" page and zero results.
            return wc;
        }

        public CatalogRow? FindBest(string hardwareId) => CatalogParser.PickBest(Search(hardwareId));

        public IReadOnlyList<CatalogRow> Search(string query)
        {
            string html;
            using (var wc = Client()) html = wc.DownloadString(Base + "Search.aspx?q=" + Uri.EscapeDataString(query));
            Thread.Sleep(300); // be polite to the catalog
            var rows = CatalogParser.ParseSearch(html);
            if (rows.Count == 0 && _debugDir != null && _debugCount < 12)
            {
                _debugCount++; // keep a sample page for each query we could not read (max 12), so problems can be diagnosed
                var safe = System.Text.RegularExpressions.Regex.Replace(query, @"[^A-Za-z0-9]+", "_").Trim('_');
                try { Directory.CreateDirectory(_debugDir); File.WriteAllText(Path.Combine(_debugDir, "debug-" + safe + ".html"), "<!-- query: " + query + " -->\r\n" + html); } catch { }
            }
            return rows;
        }

        public void Download(string updateId, string folder)
        {
            var json = "[{\"size\":0,\"languages\":\"\",\"uidInfo\":\"" + updateId + "\",\"updateID\":\"" + updateId + "\"}]";
            var body = "updateIDs=" + Uri.EscapeDataString(json) +
                       "&updateIDsBlockedForImport=&wsusApiPresent=&contentImport=&sku=&serverName=&ssl=&portNumber=&version=";
            string page;
            using (var wc = Client())
            {
                wc.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                page = wc.UploadString(Base + "DownloadDialog.aspx", body);
            }
            var urls = CatalogParser.ParseDownloadUrls(page).Where(u => u.EndsWith(".cab", StringComparison.OrdinalIgnoreCase)).ToList();
            if (urls.Count == 0) throw new Exception("catalog gave no CAB for " + updateId);
            Directory.CreateDirectory(folder);
            var n = 0;
            foreach (var url in urls)
            {
                var cab = Path.Combine(Path.GetTempPath(), "wf_" + Guid.NewGuid().ToString("N") + ".cab");
                using (var wc = new WebClient()) wc.DownloadFile(url, cab);
                var dest = Path.Combine(folder, "p" + n++);
                Directory.CreateDirectory(dest);
                var psi = new ProcessStartInfo("expand.exe", "-F:* \"" + cab + "\" \"" + dest + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using (var p = Process.Start(psi)!) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); }
                try { File.Delete(cab); } catch { }
            }
        }
    }
}
