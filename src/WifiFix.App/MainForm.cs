using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using WifiFix.Core;

namespace WifiFix.App
{
    class MainForm : Form
    {
        readonly Button _go = new Button();
        readonly Label _banner = new Label();
        readonly TextBox _log = new TextBox();

        readonly CheckBox _dry = new CheckBox();

        readonly CheckBox _audio = new CheckBox();
        bool _includeAudio;

        readonly CheckBox _other = new CheckBox();
        bool _includeOther;
        readonly CheckBox _update = new CheckBox();
        readonly Button _updateBtn = new Button();
        readonly Label _packInfo = new Label();
        readonly Button _swBtn = new Button();
        readonly Label _swInfo = new Label();
        ReleaseInfo? _latest;
        bool _autoUpdate;

        public MainForm(bool auto, bool dry = false, bool audio = false, bool autoUpdate = true, bool other = false)
        {
            _other.Text = "Also fix touchpad / mouse / Bluetooth / other (optional)"; _other.Dock = DockStyle.Top; _other.Height = 26; _other.Checked = other;
            _update.Text = "Also update the driver pack automatically after fixing (when online)"; _update.Dock = DockStyle.Top; _update.Height = 26; _update.Checked = autoUpdate;
            _audio.Text = "Also fix audio drivers (optional)"; _audio.Dock = DockStyle.Top; _audio.Height = 26; _audio.Checked = audio;
            _dry.Text = "Test only (scan and report, install nothing)"; _dry.Dock = DockStyle.Top; _dry.Height = 26; _dry.Checked = dry;
            Text = "DriverFix v" + AppInfo.Version + " - 1-click WiFi driver";
            Width = 680; Height = 600; StartPosition = FormStartPosition.CenterScreen;
            _go.Text = "FIX WIFI"; _go.Font = new Font("Segoe UI", 22, FontStyle.Bold);
            _go.Dock = DockStyle.Top; _go.Height = 90; _go.Click += (s, e) => Start();
            _updateBtn.Text = "UPDATE DRIVER PACK (needs internet)"; _updateBtn.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            _updateBtn.Dock = DockStyle.Top; _updateBtn.Height = 44; _updateBtn.Click += (s, e) => UpdateNow();
            _swBtn.Text = "UPDATE SOFTWARE (get latest DriverFix from GitHub)"; _swBtn.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            _swBtn.Dock = DockStyle.Top; _swBtn.Height = 36; _swBtn.Click += (s, e) => UpdateSoftware();
            _swInfo.Dock = DockStyle.Top; _swInfo.Height = 22; _swInfo.Font = new Font("Segoe UI", 9); _swInfo.Text = "DriverFix v" + AppInfo.Version + " - checking for a newer version...";
            _packInfo.Dock = DockStyle.Top; _packInfo.Height = 22; _packInfo.Font = new Font("Segoe UI", 9); _packInfo.Text = PackStatus.Read(PackRoot).Describe();
            _banner.Dock = DockStyle.Top; _banner.Height = 40; _banner.TextAlign = ContentAlignment.MiddleCenter;
            _banner.Font = new Font("Segoe UI", 14, FontStyle.Bold); _banner.Text = "Click the button";
            _log.Multiline = true; _log.ReadOnly = true; _log.ScrollBars = ScrollBars.Vertical;
            _log.Dock = DockStyle.Fill; _log.Font = new Font("Consolas", 10);
            Controls.Add(_log); Controls.Add(_packInfo); Controls.Add(_swInfo); Controls.Add(_banner); Controls.Add(_update); Controls.Add(_other); Controls.Add(_audio); Controls.Add(_dry); Controls.Add(_updateBtn); Controls.Add(_swBtn); Controls.Add(_go);
            AddLine("DriverFix v" + AppInfo.Version);
            SelfUpdate.CleanupOld(AppDomain.CurrentDomain.BaseDirectory);
            Shown += (s, e) => Task.Run(() => CheckSoftware(false));
            if (auto) Shown += (s, e) => Start();
        }

        void AddLine(string m)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(AddLine), m); return; }
            _log.AppendText(m + Environment.NewLine);
        }

        void Start()
        {
            SetBusy(true); _banner.Text = "Working..."; _banner.ForeColor = Color.Black; _log.Clear();
            _dryRun = _dry.Checked; _includeAudio = _audio.Checked; _includeOther = _other.Checked; _autoUpdate = _update.Checked;
            Task.Run(() =>
            {
                RunOutcome outcome;
                try { outcome = Execute(); }
                catch (Exception e) { AddLine("ERROR: " + e.Message); outcome = RunOutcome.NotFixed; }
                BeginInvoke(new Action(() => Finish(outcome)));   // show the result first; pack update continues below
                if (_autoUpdate && !_dryRun && !_forceInstall)
                {
                    try { AutoUpdatePack(false); }
                    catch (Exception e) { AddLine("Pack update error: " + e.Message); }
                }
                BeginInvoke(new Action(() => SetBusy(false)));
            });
        }

        /// <summary>Asks GitHub for the newest release and says plainly whether this copy is current.</summary>
        void CheckSoftware(bool loud)
        {
            try
            {
                _latest = SelfUpdate.CheckLatest();
                string t; Color c;
                if (_latest == null) { t = "DriverFix v" + AppInfo.Version + " - no release found on GitHub (" + AppInfo.Repo + ")"; c = Color.DarkOrange; }
                else if (ReleaseParser.IsNewer(AppInfo.Version, _latest.Version)) { t = "DriverFix v" + AppInfo.Version + " - NEW version " + _latest.Version + " available: click UPDATE SOFTWARE"; c = Color.DarkOrange; }
                else { t = "DriverFix v" + AppInfo.Version + " - up to date (latest on GitHub: " + _latest.Version + ")"; c = Color.Green; }
                BeginInvoke(new Action(() => { _swInfo.Text = t; _swInfo.ForeColor = c; }));
            }
            catch (Exception e)
            {
                AddLine("Update check failed: " + e.Message);
                BeginInvoke(new Action(() => { _swInfo.Text = "DriverFix v" + AppInfo.Version + " - could not check for updates (no internet?)"; _swInfo.ForeColor = Color.Red; }));
            }
        }

        /// <summary>Button: download the newest DriverFix.exe from GitHub, swap it in and restart.</summary>
        void UpdateSoftware()
        {
            SetBusy(true); _swBtn.Enabled = false; Set("Checking GitHub for a new version...", Color.Black);
            Task.Run(() =>
            {
                try
                {
                    CheckSoftware(true);
                    if (_latest == null || !ReleaseParser.IsNewer(AppInfo.Version, _latest.Version))
                    {
                        BeginInvoke(new Action(() => Set(_latest == null ? "No release found on GitHub" : "Already the latest version (v" + AppInfo.Version + ")", _latest == null ? Color.DarkOrange : Color.Green)));
                        return;
                    }
                    var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                    var tmp = Path.Combine(Path.GetDirectoryName(exe), "DriverFix.new");
                    AddLine("Downloading DriverFix v" + _latest.Version + " ...");
                    BeginInvoke(new Action(() => Set("Downloading v" + _latest.Version + "...", Color.Black)));
                    SelfUpdate.Download(_latest, tmp);
                    SelfUpdate.Swap(exe, tmp);
                    AddLine("Updated to v" + _latest.Version + ". Restarting...");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
                    BeginInvoke(new Action(() => Application.Exit()));
                }
                catch (Exception e)
                {
                    AddLine("Software update failed: " + e.Message);
                    BeginInvoke(new Action(() => Set("Software update FAILED - see log (is the stick read-only?)", Color.Red)));
                }
                finally { BeginInvoke(new Action(() => { SetBusy(false); _swBtn.Enabled = true; })); }
            });
        }

        static string PackRoot => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "drivers");

        void SetBusy(bool busy) { _go.Enabled = !busy; _updateBtn.Enabled = !busy; _swBtn.Enabled = !busy; }

        /// <summary>Button: refresh the offline pack now, with clear feedback in the banner.</summary>
        void UpdateNow()
        {
            SetBusy(true); Set("Updating driver pack... (a few minutes)", Color.Black);
            Task.Run(() =>
            {
                try { AutoUpdatePack(); }
                catch (Exception e) { AddLine("Pack update error: " + e.Message); BeginInvoke(new Action(() => Set("Pack update failed - see log", Color.Red))); }
                BeginInvoke(new Action(() => SetBusy(false)));
            });
        }

        /// <summary>If the PC has internet, refresh the offline pack on the stick (only downloads what is new).</summary>
        void AutoUpdatePack(bool banner = true)
        {
            // After a fix the banner keeps showing the fix result; pack feedback then goes on the pack line only.
            void Say(string t, Color c) { if (banner) Set(t, c); else { _packInfo.Text = t; _packInfo.ForeColor = c; } }
            var packRoot = PackRoot;
            var log = new UiLog(AddLine, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs"));
            if (!new WuaOnlineProvider(log).HasInternet())
            {
                log.Info("No internet: offline pack not updated.");
                BeginInvoke(new Action(() => Say("No internet - pack NOT updated", Color.Red)));
                return;
            }
            log.Info("Internet found - checking for newer drivers for the offline pack (can take a few minutes)...");
            BeginInvoke(new Action(() => Say("Updating driver pack...", Color.Black)));
            var rep = new PackUpdater(new MsCatalogSource(packRoot), log).Run(packRoot, KnownChips.All.Concat(KnownChips.Extras));
            log.Info("Pack update done: " + rep.Downloaded + " downloaded, " + rep.UpToDate + " up to date, " +
                     rep.NotFound + " not in catalog, " + rep.Errors + " errors.");
            try { PackIndex.Build(packRoot).Save(Path.Combine(packRoot, "index.json")); } catch { }
            BeginInvoke(new Action(() =>
            {
                _packInfo.Text = PackStatus.Read(packRoot).Describe();
                if (rep.Errors > 0 && rep.Downloaded == 0 && rep.UpToDate == 0) Say("Pack update failed (" + rep.Errors + " errors) - see log", Color.Red);
                else if (rep.Downloaded > 0) Say("Pack UPDATED: " + rep.Downloaded + " new, " + rep.UpToDate + " already current", Color.Green);
                else if (rep.UpToDate > 0) Say("Pack already up to date (" + rep.UpToDate + " checked)", Color.Green);
                else Say("Nothing found in catalog - see PACK-REPORT.txt", Color.DarkOrange);
            }));
        }

        RunOutcome Execute()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var packRoot = Path.Combine(baseDir, "drivers");
            var log = new UiLog(AddLine, Path.Combine(baseDir, "logs"));
            var indexPath = Path.Combine(packRoot, "index.json");
            // Always rescan the INFs: a driver dropped onto the stick is found without rebuilding index.json.
            PackIndex index;
            if (Directory.Exists(packRoot)) index = PackIndex.Build(packRoot);
            else { log.Warn("No drivers folder next to DriverFix.exe; offline pack is empty."); index = new PackIndex(); }
            try { if (Directory.Exists(packRoot)) index.Save(indexPath); } catch { /* read-only stick */ }
            log.Info("Driver pack entries: " + index.Count);
            var machine = MachineScanner.Read();
            log.Info("PC: " + machine.Describe());
            if (_dryRun) return DryRun(log, index, _includeAudio, _includeOther);
            if (_forceInstall) return ForceInstall(log, machine);
            var online = new FallbackOnlineProvider(new CatalogOnlineProvider(log, machine), new WuaOnlineProvider(log));
            var runner = new FixRunner(new WmiDeviceScanner(), new WifiVerifier(), index,
                new PnpUtil(new ProcessRunner()), online, packRoot, log, null, _includeAudio, _includeOther);
            return runner.Run();
        }

        bool _dryRun;
        internal static bool ForceInstallFlag;
        bool _forceInstall => ForceInstallFlag;

        /// <summary>TEST: real catalog download + pnputil install for the CURRENT WiFi adapter, even though it works.</summary>
        static RunOutcome ForceInstall(ILog log, MachineInfo machine)
        {
            log.Info("INSTALL TEST: downloading and installing the driver for the current WiFi adapter.");
            var devs = new WmiDeviceScanner().ScanWifiPresent();
            if (devs.Count == 0) { log.Warn("No WiFi adapter found."); return RunOutcome.NoWifiHardware; }
            var online = new CatalogOnlineProvider(log, machine);
            var pnp = new PnpUtil(new ProcessRunner());
            var ok = false;
            foreach (var d in devs)
            {
                log.Info("Adapter: " + d.Name + " | " + (d.HardwareIds.Count > 0 ? d.HardwareIds[0] : "-"));
                var work = Path.Combine(Path.GetTempPath(), "wififix_test_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(work);
                var folder = online.Download(d, work);
                if (folder == null) { log.Warn("Download step FAILED: no driver folder."); continue; }
                var infs = Directory.GetFiles(folder, "*.inf", SearchOption.AllDirectories);
                log.Info("Download + extract OK: " + infs.Length + " INF file(s).");
                foreach (var inf in infs)
                {
                    var r = pnp.Install(inf);
                    log.Info("  pnputil " + Path.GetFileName(inf) + " -> " + r);
                    if (r != InstallResult.Failed) ok = true;
                }
            }
            log.Info(ok ? "INSTALL TEST PASSED." : "INSTALL TEST: no INF installed.");
            return ok ? RunOutcome.Fixed : RunOutcome.NotFixed;
        }

        /// <summary>Scan and report only. Installs nothing, downloads nothing.</summary>
        static RunOutcome DryRun(ILog log, PackIndex index, bool includeAudio, bool includeOther)
        {
            log.Info("TEST ONLY - nothing will be installed.");
            var devices = new WmiDeviceScanner().Scan();
            log.Info("Devices with problems: " + devices.Count);
            var missing = 0;
            foreach (var d in devices)
            {
                var isWifi = DeviceFilter.IsMissingWifi(d, index);
                var isAudio = includeAudio && !isWifi && DeviceFilter.IsMissingAudio(d, index);
                var isOther = includeOther && !isWifi && !isAudio && DeviceFilter.IsMissingOther(d, index);
                var wanted = isWifi || isAudio || isOther;
                if (wanted) missing++;
                log.Info("  [" + (isWifi ? "MISSING WIFI" : isAudio ? "MISSING AUDIO" : isOther ? "MISSING OTHER" : "other") + "] code " + d.ProblemCode + ": " + d.Name +
                         " | " + (d.HardwareIds.Count > 0 ? d.HardwareIds[0] : "-") +
                         (wanted ? " | pack match: " + (index.FindInf(d.AllIds) ?? "none") : ""));
                if (wanted)
                    log.Info("      online catalog would use: " + (new CatalogOnlineProvider(log, MachineScanner.Read()).Probe(d) ?? "nothing found"));
            }
            var ver = new WifiVerifier();
            log.Info("WiFi adapter present: " + ver.AnyWifiAdapterPresent());
            log.Info("Internet reachable: " + new WuaOnlineProvider(log).HasInternet());
            return missing > 0 ? RunOutcome.NotFixed : (ver.AnyWifiAdapterPresent() ? RunOutcome.AlreadyWorking : RunOutcome.NoWifiHardware);
        }

        void Finish(RunOutcome o)
        {
            switch (o)
            {
                case RunOutcome.Fixed: Set(_includeAudio || _includeOther ? "Done - drivers installed" : "WiFi working", Color.Green); break;
                case RunOutcome.FixedRebootNeeded: Set("Driver installed - restart the PC", Color.DarkOrange); break;
                case RunOutcome.AlreadyWorking: Set("WiFi already working - nothing to do", Color.Green); break;
                case RunOutcome.NoWifiHardware: Set("No WiFi hardware found", Color.DarkOrange); break;
                default: Set("Not fixed - plug in USB WiFi adapter and run again (see log)", Color.Red); break;
            }
        }

        void Set(string text, Color c) { _banner.Text = text; _banner.ForeColor = c; }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            // Never crash silently: write any unexpected error to logs\crash.log and tell the user.
            Action<Exception> report = ex =>
            {
                try
                {
                    var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "crash.log"), DateTime.Now + " v" + AppInfo.Version + "\r\n" + ex + "\r\n\r\n");
                }
                catch { }
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => { report(e.Exception); MessageBox.Show("DriverFix hit an error: " + e.Exception.Message + "\r\nSaved to logs\\crash.log", "WifiFix"); };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => report(e.ExceptionObject as Exception ?? new Exception("unknown"));
            var dry = Array.IndexOf(args, "/dryrun") >= 0;
            MainForm.ForceInstallFlag = Array.IndexOf(args, "/forceinstall") >= 0;
            var audio = Array.IndexOf(args, "/audio") >= 0;
            var autoUpdate = Array.IndexOf(args, "/noupdate") < 0;
            var other = Array.IndexOf(args, "/other") >= 0;
            Application.Run(new MainForm(dry || MainForm.ForceInstallFlag || Array.IndexOf(args, "/auto") >= 0, dry, audio, autoUpdate, other));
        }
    }
}
