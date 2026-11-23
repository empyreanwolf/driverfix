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
        /// <summary>Downloads and extracts the update's driver files into <paramref name="folder"/>.</summary>
        void Download(string updateId, string folder);
    }

    public record UpdateReport(int Downloaded, int UpToDate, int NotFound, int Errors);

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
    }

    /// <summary>Real Microsoft Update Catalog access (needs internet; Windows for expand.exe).</summary>
    public class MsCatalogSource : ICatalogSource
    {
        const string Base = "https://www.catalog.update.microsoft.com/";

        readonly string? _debugDir;
        bool _debugSaved;
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

        public CatalogRow? FindBest(string hardwareId)
        {
            string html;
            using (var wc = Client()) html = wc.DownloadString(Base + "Search.aspx?q=" + Uri.EscapeDataString(hardwareId));
            Thread.Sleep(300); // be polite to the catalog
            var rows = CatalogParser.ParseSearch(html);
            if (rows.Count == 0 && !_debugSaved && _debugDir != null)
            {
                _debugSaved = true; // keep ONE sample of a page we could not read, so it can be fixed
                try { Directory.CreateDirectory(_debugDir); File.WriteAllText(Path.Combine(_debugDir, "debug-search.html"), "<!-- query: " + hardwareId + " -->\r\n" + html); } catch { }
            }
            return CatalogParser.PickBest(rows);
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
