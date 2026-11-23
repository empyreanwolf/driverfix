using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using WifiFix.Core;

namespace WifiFix.App
{
    class ProcessRunner : IProcessRunner
    {
        public ProcessResult Run(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEndAsync(); // read both pipes concurrently: no deadlock on big output
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return new ProcessResult(p.ExitCode, output + err.Result);
        }
    }

    class UiLog : ILog
    {
        readonly Action<string> _sink;
        readonly string _file;
        public UiLog(Action<string> sink, string logFolder)
        {
            _sink = sink;
            _file = Path.Combine(logFolder, "driverfix-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
        }
        public void Info(string m) => Write(m);
        public void Warn(string m) => Write("WARNING: " + m);
        void Write(string m)
        {
            _sink(m);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                File.AppendAllText(_file, DateTime.Now.ToString("HH:mm:ss") + " " + m + Environment.NewLine);
            }
            catch { /* stick may be read-only; never fail the run */ }
        }
    }

    class WmiDeviceScanner : IDeviceScanner
    {
        /// <summary>Working WiFi adapters (used only by the forced install test).</summary>
        public IReadOnlyList<DeviceInfo> ScanWifiPresent()
        {
            var list = new List<DeviceInfo>();
            using var s = new ManagementObjectSearcher(
                "SELECT Name, PNPClass, ConfigManagerErrorCode, HardwareID, CompatibleID FROM Win32_PnPEntity WHERE PNPClass = 'Net'");
            foreach (ManagementObject o in s.Get())
            {
                var name = (o["Name"] as string) ?? "";
                if (name.IndexOf("wi-fi", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("wireless", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("wlan", StringComparison.OrdinalIgnoreCase) < 0) continue;
                list.Add(new DeviceInfo(name, "Net", Convert.ToInt32(o["ConfigManagerErrorCode"]),
                    (o["HardwareID"] as string[]) ?? new string[0], (o["CompatibleID"] as string[]) ?? new string[0]));
            }
            return list;
        }

        public IReadOnlyList<DeviceInfo> Scan()
        {
            var list = new List<DeviceInfo>();
            using var s = new ManagementObjectSearcher(
                "SELECT Name, PNPClass, ConfigManagerErrorCode, HardwareID, CompatibleID FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
            foreach (ManagementObject o in s.Get())
            {
                list.Add(new DeviceInfo(
                    (o["Name"] as string) ?? "Unknown device",
                    (o["PNPClass"] as string) ?? "",
                    Convert.ToInt32(o["ConfigManagerErrorCode"]),
                    (o["HardwareID"] as string[]) ?? new string[0],
                    (o["CompatibleID"] as string[]) ?? new string[0]));
            }
            return list;
        }
    }

    static class MachineScanner
    {
        public static MachineInfo Read()
        {
            string Q(string cls, string prop)
            {
                try
                {
                    using var s = new ManagementObjectSearcher("SELECT " + prop + " FROM " + cls);
                    foreach (ManagementObject o in s.Get()) return ((o[prop] as string) ?? "").Trim();
                }
                catch { }
                return "";
            }
            var maker = Q("Win32_ComputerSystem", "Manufacturer");
            var model = Q("Win32_ComputerSystem", "Model");
            // Lenovo stores the friendly name (e.g. "ThinkPad T14") in Win32_ComputerSystemProduct.Version.
            if (maker.StartsWith("LENOVO", StringComparison.OrdinalIgnoreCase))
            {
                var friendly = Q("Win32_ComputerSystemProduct", "Version");
                if (friendly.Length > 0) model = friendly + " [" + model + "]";
            }
            return new MachineInfo(maker, model, Q("Win32_BIOS", "SerialNumber"));
        }
    }

    class TimeoutWebClient : WebClient
    {
        protected override WebRequest GetWebRequest(Uri address)
        {
            var r = base.GetWebRequest(address);
            r.Timeout = 60000;
            return r;
        }
    }

    /// <summary>
    /// Searches the Microsoft Update Catalog by the device's hardware IDs (exact chip), then by PC model as a last resort,
    /// downloads the newest Windows 10 driver CAB and extracts it with expand.exe.
    /// </summary>
    class CatalogOnlineProvider : IOnlineProvider
    {
        const string Base = "https://www.catalog.update.microsoft.com/";
        readonly ILog _log;
        readonly MachineInfo? _machine;
        public CatalogOnlineProvider(ILog log, MachineInfo? machine) { _log = log; _machine = machine; }

        public bool HasInternet() => new WuaOnlineProvider(_log).HasInternet();

        public string? Download(DeviceInfo device, string workFolder)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                foreach (var q in Queries(device))
                {
                    _log.Info("Catalog search: " + q);
                    string html;
                    using (var wc = new TimeoutWebClient()) html = wc.DownloadString(Base + "Search.aspx?q=" + Uri.EscapeDataString(q));
                    var best = CatalogParser.PickBest(CatalogParser.ParseSearch(html));
                    if (best == null) continue;
                    _log.Info("Found: " + best.Title + " (" + best.Version + ")");
                    var dir = Fetch(best.UpdateId, workFolder);
                    if (dir != null) return dir;
                }
            }
            catch (Exception e) { _log.Warn("Catalog lookup failed: " + e.Message); }
            return null;
        }

        /// <summary>Search only (no download): "title (version)" of the driver Download would fetch, or null.</summary>
        public string? Probe(DeviceInfo device)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                foreach (var q in Queries(device))
                {
                    string html;
                    using (var wc = new TimeoutWebClient()) html = wc.DownloadString(Base + "Search.aspx?q=" + Uri.EscapeDataString(q));
                    var best = CatalogParser.PickBest(CatalogParser.ParseSearch(html));
                    if (best != null) return best.Title + " (" + best.Version + ") via \"" + q + "\"";
                }
            }
            catch (Exception e) { return "search failed: " + e.Message; }
            return null;
        }

        IEnumerable<string> Queries(DeviceInfo d)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in d.HardwareIds.Take(3))
            {
                var baseId = id; var i = id.IndexOf("&SUBSYS_", StringComparison.OrdinalIgnoreCase);
                if (i > 0) baseId = id.Substring(0, i);
                if (seen.Add(baseId)) yield return baseId;   // most useful: PCI\VEN_xxxx&DEV_xxxx
                if (seen.Add(id)) yield return id;
            }
            if (_machine != null && !string.IsNullOrWhiteSpace(_machine.Model))
                yield return _machine.Model.Split('[')[0].Trim() + (DeviceFilter.LooksAudio(d) ? " audio" : d.Name.IndexOf("bluetooth", StringComparison.OrdinalIgnoreCase) >= 0 ? " bluetooth" : DeviceFilter.IsMissingOther(d, new PackIndex()) ? " touchpad" : " wireless");
        }

        string? Fetch(string updateId, string workFolder)
        {
            var json = "[{\"size\":0,\"languages\":\"\",\"uidInfo\":\"" + updateId + "\",\"updateID\":\"" + updateId + "\"}]";
            var body = "updateIDs=" + Uri.EscapeDataString(json) +
                       "&updateIDsBlockedForImport=&wsusApiPresent=&contentImport=&sku=&serverName=&ssl=&portNumber=&version=";
            string page;
            using (var wc = new TimeoutWebClient())
            {
                wc.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                page = wc.UploadString(Base + "DownloadDialog.aspx", body);
            }
            var urls = CatalogParser.ParseDownloadUrls(page).Where(u => u.EndsWith(".cab", StringComparison.OrdinalIgnoreCase)).ToList();
            if (urls.Count == 0) { _log.Warn("Catalog gave no downloadable CAB."); return null; }
            var outDir = Path.Combine(workFolder, "cat");
            Directory.CreateDirectory(outDir);
            var n = 0;
            foreach (var url in urls)
            {
                var cab = Path.Combine(workFolder, "c" + n + ".cab");
                _log.Info("Downloading driver...");
                using (var wc = new TimeoutWebClient()) wc.DownloadFile(url, cab);
                var dest = Path.Combine(outDir, "p" + n++);
                Directory.CreateDirectory(dest);
                new ProcessRunner().Run("expand.exe", "-F:* \"" + cab + "\" \"" + dest + "\"");
            }
            return outDir;
        }
    }

    /// <summary>"Up" means a Wi-Fi interface exists (driver loaded); being connected to a network is not required.</summary>
    class WifiVerifier : IWifiVerifier
    {
        public bool AnyWifiAdapterPresent() => Check();
        public bool WifiAdapterUp()
        {
            for (var i = 0; i < 10; i++) { if (Check()) return true; Thread.Sleep(1500); }
            return false;
        }
        static bool Check() => NetworkInterface.GetAllNetworkInterfaces()
            .Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
    }

    /// <summary>Uses the Windows Update Agent to find a driver for the device, downloads its CAB(s), extracts with expand.exe.</summary>
    class WuaOnlineProvider : IOnlineProvider
    {
        readonly ILog _log;
        public WuaOnlineProvider(ILog log) { _log = log; }

        public bool HasInternet()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("http://www.msftconnecttest.com/connecttest.txt");
                req.Timeout = 6000;
                using var resp = (HttpWebResponse)req.GetResponse();
                return resp.StatusCode == HttpStatusCode.OK;
            }
            catch { return false; }
        }

        public string? Download(DeviceInfo device, string workFolder)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                dynamic session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session")!)!;
                dynamic searcher = session.CreateUpdateSearcher();
                _log.Info("Searching Windows Update for a driver...");
                dynamic result = searcher.Search("IsInstalled=0 and Type='Driver'");
                var ids = device.AllIds.ToList();
                var outDir = Path.Combine(workFolder, "wu");
                Directory.CreateDirectory(outDir);
                var got = 0;
                foreach (dynamic update in result.Updates)
                {
                    string hw = update.DriverHardwareID ?? "";
                    if (hw.Length == 0) continue;
                    if (!ids.Any(d => d.Equals(hw, StringComparison.OrdinalIgnoreCase) ||
                                      d.StartsWith(hw + "&", StringComparison.OrdinalIgnoreCase))) continue;
                    _log.Info("Found: " + (string)update.Title);
                    foreach (dynamic content in update.DownloadContents)
                    {
                        string url = content.DownloadUrl;
                        if (string.IsNullOrEmpty(url)) continue;
                        var cab = Path.Combine(workFolder, "d" + got++ + ".cab");
                        using (var wc = new WebClient()) wc.DownloadFile(url, cab);
                        var dest = Path.Combine(outDir, "p" + got);
                        Directory.CreateDirectory(dest);
                        new ProcessRunner().Run("expand.exe", "-F:* \"" + cab + "\" \"" + dest + "\"");
                    }
                }
                return got > 0 ? outDir : null;
            }
            catch (Exception e) { _log.Warn("Windows Update lookup failed: " + e.Message); return null; }
        }
    }
}
