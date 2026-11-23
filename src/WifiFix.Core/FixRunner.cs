using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace WifiFix.Core
{
    public enum RunOutcome { NoWifiHardware, AlreadyWorking, Fixed, FixedRebootNeeded, NotFixed }

    public class FixRunner
    {
        readonly IDeviceScanner _scanner;
        readonly IWifiVerifier _verifier;
        readonly PackIndex _index;
        readonly PnpUtil _pnp;
        readonly IOnlineProvider _online;
        readonly string _packRoot;
        readonly ILog _log;

        readonly MachineInfo? _machine;

        public FixRunner(IDeviceScanner scanner, IWifiVerifier verifier, PackIndex index, PnpUtil pnp,
            IOnlineProvider online, string packRoot, ILog log, MachineInfo? machine = null, bool includeAudio = false, bool includeOther = false)
        {
            _scanner = scanner; _verifier = verifier; _index = index; _pnp = pnp;
            _online = online; _packRoot = packRoot; _log = log; _machine = machine; _includeAudio = includeAudio; _includeOther = includeOther;
        }

        readonly bool _includeAudio, _includeOther;

        public RunOutcome Run()
        {
            if (_machine != null) _log.Info("PC: " + _machine.Describe());
            var devices = _scanner.Scan();
            var wifi = devices.Where(d => DeviceFilter.IsMissingWifi(d, _index)).ToList();
            var audio = _includeAudio
                ? devices.Where(d => !wifi.Contains(d) && DeviceFilter.IsMissingAudio(d, _index)).ToList()
                : new System.Collections.Generic.List<DeviceInfo>();
            var other = _includeOther
                ? devices.Where(d => !wifi.Contains(d) && !audio.Contains(d) && DeviceFilter.IsMissingOther(d, _index)).ToList()
                : new System.Collections.Generic.List<DeviceInfo>();
            if (wifi.Count == 0 && audio.Count == 0 && other.Count == 0)
                return _verifier.AnyWifiAdapterPresent() ? RunOutcome.AlreadyWorking : RunOutcome.NoWifiHardware;

            var failed = false;
            var reboot = false;
            foreach (var dev in wifi.Concat(audio).Concat(other))
            {
                _log.Info((wifi.Contains(dev) ? "Missing WiFi driver: " : audio.Contains(dev) ? "Missing audio driver: " : "Missing driver (other device): ") + dev.Name + " " + (dev.HardwareIds.FirstOrDefault() ?? ""));
                var r = TryPack(dev);
                if (r == InstallResult.Failed) r = TryOnline(dev);
                if (r == InstallResult.Failed) failed = true;
                if (r == InstallResult.OkRebootNeeded) reboot = true;
            }

            if (failed) return RunOutcome.NotFixed;
            if (reboot) return RunOutcome.FixedRebootNeeded;
            if (wifi.Count > 0 && !_verifier.WifiAdapterUp()) return RunOutcome.NotFixed;
            return RunOutcome.Fixed;
        }

        InstallResult TryPack(DeviceInfo dev)
        {
            var candidates = _index.FindAll(dev.AllIds);
            if (candidates.Count == 0) { _log.Info("No match in pack."); return InstallResult.Failed; }
            foreach (var rel in candidates)
            {
                _log.Info("Installing from pack: " + rel);
                var r = _pnp.Install(Path.Combine(_packRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (r != InstallResult.Failed) return r;
                _log.Warn("Pack install failed: " + rel);
            }
            return InstallResult.Failed;
        }

        InstallResult TryOnline(DeviceInfo dev)
        {
            if (!_online.HasInternet()) { _log.Warn("No internet; plug in the USB WiFi adapter and run again."); return InstallResult.Failed; }
            var work = Path.Combine(Path.GetTempPath(), "wififix_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(work);
                var folder = _online.Download(dev, work);
                if (folder == null) { _log.Warn("Online search found no driver."); return InstallResult.Failed; }
                var result = InstallResult.Failed;
                foreach (var inf in Directory.GetFiles(folder, "*.inf", SearchOption.AllDirectories))
                {
                    var r = _pnp.Install(inf);
                    if (r == InstallResult.OkRebootNeeded) result = r;
                    else if (r == InstallResult.Ok && result == InstallResult.Failed) result = r;
                }
                if (result != InstallResult.Failed) SaveBack(dev, folder);
                return result;
            }
            catch (Exception e) { _log.Warn("Online install error: " + e.Message); return InstallResult.Failed; }
            finally { try { Directory.Delete(work, true); } catch { } }
        }

        void SaveBack(DeviceInfo dev, string folder)
        {
            try
            {
                var name = Regex.Replace(dev.HardwareIds.FirstOrDefault() ?? "device", "[^A-Za-z0-9]+", "_");
                var destRel = "win10/downloaded/" + name;
                var dest = Path.Combine(_packRoot, "win10", "downloaded", name);
                Directory.CreateDirectory(dest);
                foreach (var f in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(dest, f.Substring(folder.Length).TrimStart('\\', '/'));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(f, target, true);
                }
                foreach (var inf in Directory.GetFiles(dest, "*.inf", SearchOption.AllDirectories))
                {
                    var rel = destRel + inf.Substring(dest.Length).Replace('\\', '/');
                    _index.Add(rel, InfParser.GetHardwareIds(File.ReadAllText(inf)));
                }
                _index.Save(Path.Combine(_packRoot, "index.json"));
                _log.Info("Saved driver to pack: " + destRel);
            }
            catch (Exception e) { _log.Warn("Could not save driver back to the stick: " + e.Message); }
        }
    }
}
