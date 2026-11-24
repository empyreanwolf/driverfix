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
        readonly Button _w11Btn = new Button();
        readonly Label _swInfo = new Label();
        ReleaseInfo? _latest;
        bool _autoUpdate;

        public MainForm(bool auto, bool dry = false, bool audio = false, bool autoUpdate = true, bool other = false)
        {
            // Look: Radix Themes (slate neutrals, green accent, 8px radius). Radix itself is a React library and cannot run in
            // WinForms, so its colour scale, radius and spacing are reproduced here.
            Text = "DriverFix v" + AppInfo.Version + " - detect PC and install drivers";
            Width = 700; Height = 700; StartPosition = FormStartPosition.CenterScreen;
            BackColor = Radix.Slate1; ForeColor = Radix.Slate12; Font = new Font("Segoe UI", 9.5f); Padding = new Padding(16);

            foreach (var c in new[] { _other, _audio, _dry, _update }) { c.Dock = DockStyle.Top; c.Height = 28; c.ForeColor = Radix.Slate12; c.FlatStyle = FlatStyle.System; }
            _other.Text = "Also fix touchpad / mouse / Bluetooth / other (optional)"; _other.Checked = other;
            _update.Text = "Also update the driver pack automatically after fixing (when online)"; _update.Checked = autoUpdate;
            _audio.Text = "Also fix audio drivers (optional)"; _audio.Checked = audio;
            _dry.Text = "Test only (scan and report, install nothing)"; _dry.Checked = dry;

            _go.Text = "Detect PC and install drivers"; _go.Font = new Font("Segoe UI", 20, FontStyle.Bold);
            _go.Dock = DockStyle.Top; _go.Height = 84; _go.Click += (s, e) => Start();
            Radix.Style(_go, Radix.Green9, Color.White, Radix.Green10);

            _banner.Dock = DockStyle.Top; _banner.Height = 48; _banner.TextAlign = ContentAlignment.MiddleCenter;
            _banner.Font = new Font("Segoe UI", 13, FontStyle.Bold); _banner.Text = "Click the green button";
            _log.Multiline = true; _log.ReadOnly = true; _log.ScrollBars = ScrollBars.Vertical; _log.BorderStyle = BorderStyle.FixedSingle;
            _log.BackColor = Color.White; _log.ForeColor = Radix.Slate12; _log.Dock = DockStyle.Fill; _log.Font = new Font("Consolas", 10);

            _packInfo.Dock = DockStyle.Bottom; _packInfo.Height = 24; _packInfo.ForeColor = Radix.Slate11; _packInfo.Text = PackStatus.Read(PackRoot).Describe();
            _swInfo.Dock = DockStyle.Bottom; _swInfo.Height = 24; _swInfo.ForeColor = Radix.Slate11; _swInfo.Text = "DriverFix v" + AppInfo.Version + " - checking for a newer version...";

            _updateBtn.Text = "Update drivers"; _swBtn.Text = "Update software";
            foreach (var b2 in new[] { _updateBtn, _swBtn }) { b2.Font = new Font("Segoe UI", 11, FontStyle.Bold); b2.Dock = DockStyle.Fill; b2.Margin = new Padding(0, 0, 8, 0); Radix.Style(b2, Radix.Slate4, Radix.Slate12, Radix.Slate5); }
            _swBtn.Margin = new Padding(0);
            _w11Btn.Text = "Force Windows 11 upgrade"; _w11Btn.Font = new Font("Segoe UI", 11, FontStyle.Bold); _w11Btn.Dock = DockStyle.Fill; _w11Btn.Margin = new Padding(0, 8, 0, 0);
            Radix.Style(_w11Btn, Radix.Slate4, Radix.Slate12, Radix.Slate5); _w11Btn.Click += (s, e) => UpgradeToWindows11();
            _updateBtn.Click += (s, e) => UpdateNow(); _swBtn.Click += (s, e) => UpdateSoftware();
            var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 112, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 8, 0, 0) };
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            bottom.Controls.Add(_updateBtn, 0, 0); bottom.Controls.Add(_swBtn, 1, 0);
            bottom.Controls.Add(_w11Btn, 0, 1); bottom.SetColumnSpan(_w11Btn, 2);

            // Docking: the control added LAST sits outermost, so the two buttons are added after the info lines.
            Controls.Add(_log); Controls.Add(_packInfo); Controls.Add(_swInfo); Controls.Add(bottom);
            Controls.Add(_banner); Controls.Add(_update); Controls.Add(_other); Controls.Add(_audio); Controls.Add(_dry); Controls.Add(_go);
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
                if (_latest == null) { t = "DriverFix v" + AppInfo.Version + " - no published version found on GitHub (" + AppInfo.Repo + ")"; c = Color.FromArgb(0xF7, 0x6B, 0x15); }
                else if (ReleaseParser.IsNewer(AppInfo.Version, _latest.Version)) { t = "DriverFix v" + AppInfo.Version + " - NEW version " + _latest.Version + " available: click UPDATE SOFTWARE"; c = Color.FromArgb(0xF7, 0x6B, 0x15); }
                else { t = "DriverFix v" + AppInfo.Version + " - up to date (latest on GitHub: " + _latest.Version + ")"; c = Radix.Green10; }
                BeginInvoke(new Action(() => { _swInfo.Text = t; _swInfo.ForeColor = c; }));
            }
            catch (Exception e)
            {
                AddLine("Update check failed: " + e.Message);
                BeginInvoke(new Action(() => { _swInfo.Text = "DriverFix v" + AppInfo.Version + " - could not check for updates (no internet?)"; _swInfo.ForeColor = Color.FromArgb(0xE5, 0x48, 0x4D); }));
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
                        BeginInvoke(new Action(() => Set(_latest == null ? "No published version found on GitHub" : "Already the latest version (v" + AppInfo.Version + ")", _latest == null ? Color.FromArgb(0xF7, 0x6B, 0x15) : Radix.Green10)));
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
                    BeginInvoke(new Action(() => Set("Software update FAILED: " + e.Message, Color.FromArgb(0xE5, 0x48, 0x4D))));
                }
                finally { BeginInvoke(new Action(() => { SetBusy(false); _swBtn.Enabled = true; })); }
            });
        }

        /// <summary>Button: switch off the unsupported-hardware checks, mount the Windows 11 ISO from the stick and start its setup in server mode (in-place upgrade, files kept).</summary>
        void UpgradeToWindows11()
        {
            int build = 0;
            try { using (var k = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")) int.TryParse(k?.GetValue("CurrentBuildNumber") as string, out build); } catch { }
            if (Win11Upgrade.IsWindows11(build)) { Set("This PC already runs Windows 11", Radix.Green10); return; }
            var iso = Win11Upgrade.FindIso(AppDomain.CurrentDomain.BaseDirectory);
            if (iso == null)
            {
                using (var dlg = new OpenFileDialog { Title = "Choose the Windows 11 ISO (or put it in the iso folder on the stick)", Filter = "Windows 11 ISO|*.iso", InitialDirectory = AppDomain.CurrentDomain.BaseDirectory })
                {
                    if (dlg.ShowDialog() != DialogResult.OK) { Set("Put the Windows 11 ISO in the 'iso' folder next to DriverFix.exe, then click again", Color.FromArgb(0xF7, 0x6B, 0x15)); return; }
                    iso = dlg.FileName;
                }
            }
            if (MessageBox.Show("Start the Windows 11 upgrade on this PC using:\n" + Path.GetFileName(iso) + "\n\nFiles and apps are kept. The PC restarts several times. Hardware checks (CPU / TPM / Secure Boot) are bypassed, which Microsoft does not support.",
                "Force Windows 11 upgrade", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            SetBusy(true); Set("Preparing Windows 11 upgrade...", Color.Black);
            var isoPath = iso;
            Task.Run(() =>
            {
                try
                {
                    foreach (var r in Win11Upgrade.BypassSettings)
                        using (var k = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64).CreateSubKey(r.KeyPath))
                            k.SetValue(r.Name, r.Value, Microsoft.Win32.RegistryValueKind.DWord);
                    AddLine("Windows 11 hardware checks bypassed.");
                    AddLine("Mounting " + Path.GetFileName(isoPath) + " ...");
                    BeginInvoke(new Action(() => Set("Mounting the Windows 11 ISO...", Color.Black)));
                    var res = new ProcessRunner().Run("powershell.exe", "-NoProfile -Command \"$i = Mount-DiskImage -ImagePath '" + isoPath.Replace("'", "''") + "' -PassThru; ($i | Get-Volume).DriveLetter\"");
                    var letter = (res.Output ?? "").Trim();
                    if (letter.Length != 1 || !File.Exists(letter + @":\setup.exe")) throw new InvalidDataException("Could not open the ISO (is it a Windows 11 ISO?).");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(letter + @":\setup.exe", Win11Upgrade.SetupArgs) { UseShellExecute = true, WorkingDirectory = letter + @":\" });
                    AddLine("Windows 11 setup started. Follow the Microsoft window.");
                    BeginInvoke(new Action(() => Set("Windows 11 setup started - follow the Microsoft window", Radix.Green10)));
                }
                catch (Exception e)
                {
                    AddLine("Windows 11 upgrade failed: " + e.Message);
                    BeginInvoke(new Action(() => Set("Windows 11 upgrade FAILED: " + e.Message, Color.FromArgb(0xE5, 0x48, 0x4D))));
                }
                finally { BeginInvoke(new Action(() => SetBusy(false))); }
            });
        }

        static string PackRoot => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "drivers");

        void SetBusy(bool busy) { _go.Enabled = !busy; _updateBtn.Enabled = !busy; _swBtn.Enabled = !busy; _w11Btn.Enabled = !busy; }

        /// <summary>Button: refresh the offline pack now, with clear feedback in the banner.</summary>
        void UpdateNow()
        {
            SetBusy(true); Set("Updating driver pack... (first time: 10-30+ min)", Color.Black);
            Task.Run(() =>
            {
                try { AutoUpdatePack(); }
                catch (Exception e) { AddLine("Pack update error: " + e.Message); BeginInvoke(new Action(() => Set("Pack update failed - see log", Color.FromArgb(0xE5, 0x48, 0x4D)))); }
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
                BeginInvoke(new Action(() => Say("No internet - pack NOT updated", Color.FromArgb(0xE5, 0x48, 0x4D))));
                return;
            }
            log.Info("Internet found - checking for newer drivers for the offline pack (can take a few minutes)...");
            BeginInvoke(new Action(() => Say("Updating driver pack...", Color.Black)));
            log.Info("First run downloads a lot and can take 10-30+ minutes. Later runs only fetch what is new.");
            var updater = new PackUpdater(new MsCatalogSource(packRoot), log);
            var r1 = updater.Run(packRoot, KnownChips.All.Concat(KnownChips.Extras));     // named chips
            var r2 = updater.Sweep(packRoot, SweepQueries.All);                          // everything else current, by vendor
            var rep = new UpdateReport(r1.Downloaded + r2.Downloaded, r1.UpToDate + r2.UpToDate, 0, r1.Errors + r2.Errors);
            log.Info("Pack update done: " + rep.Downloaded + " new, " + rep.UpToDate + " already up to date" +
                     (rep.Errors > 0 ? ", " + rep.Errors + " failed" : "") + ".");
            try { PackIndex.Build(packRoot).Save(Path.Combine(packRoot, "index.json")); } catch { }
            BeginInvoke(new Action(() =>
            {
                _packInfo.Text = PackStatus.Read(packRoot).Describe();
                if (rep.Errors > 0 && rep.Downloaded == 0 && rep.UpToDate == 0) Say("Pack update failed (" + rep.Errors + " errors) - see log", Color.FromArgb(0xE5, 0x48, 0x4D));
                else if (rep.Downloaded > 0) Say("Pack UPDATED: " + rep.Downloaded + " new, " + rep.UpToDate + " already current", Radix.Green10);
                else if (rep.UpToDate > 0) Say("Pack already up to date (" + rep.UpToDate + " checked)", Radix.Green10);
                else Say("No drivers could be downloaded - check the internet and try again", Color.FromArgb(0xF7, 0x6B, 0x15));
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
            var scanner = new WmiDeviceScanner();
            var runner = new FixRunner(scanner, new WifiVerifier(), index,
                new PnpUtil(new ProcessRunner()), online, packRoot, log, null, _includeAudio, _includeOther, scanner);
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
                case RunOutcome.Fixed: Set(_includeAudio || _includeOther ? "Done - drivers installed" : "WiFi working", Radix.Green10); break;
                case RunOutcome.FixedRebootNeeded: Set("Driver installed - restart the PC", Color.FromArgb(0xF7, 0x6B, 0x15)); break;
                case RunOutcome.AlreadyWorking: Set("WiFi already working - nothing to do", Radix.Green10); break;
                case RunOutcome.NoWifiHardware: Set("No WiFi hardware found", Color.FromArgb(0xF7, 0x6B, 0x15)); break;
                default: Set("Not fixed - plug in USB WiFi adapter and run again (see log)", Color.FromArgb(0xE5, 0x48, 0x4D)); break;
            }
        }

        void Set(string text, Color c) { _banner.Text = text; _banner.ForeColor = c; }
    }

    /// <summary>Radix Themes colour scale (slate + green, light mode) and a flat rounded button style.</summary>
    static class Radix
    {
        public static readonly Color Slate1 = Color.FromArgb(0xFC, 0xFC, 0xFD), Slate4 = Color.FromArgb(0xE8, 0xE8, 0xEC),
            Slate5 = Color.FromArgb(0xE0, 0xE1, 0xE6), Slate11 = Color.FromArgb(0x60, 0x64, 0x6C), Slate12 = Color.FromArgb(0x1C, 0x20, 0x24),
            Green9 = Color.FromArgb(0x30, 0xA4, 0x6C), Green10 = Color.FromArgb(0x2B, 0x9A, 0x66);

        public static void Style(Button b, Color back, Color fore, Color hover)
        {
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.UseVisualStyleBackColor = false;
            b.BackColor = back; b.ForeColor = fore; b.Cursor = Cursors.Hand;
            b.FlatAppearance.MouseOverBackColor = hover; b.FlatAppearance.MouseDownBackColor = hover;
            b.SizeChanged += (s, e) => Round(b, 8);
            Round(b, 8);
        }

        static void Round(Control c, int r)
        {
            if (c.Width < 2 * r || c.Height < 2 * r) return;
            var p = new System.Drawing.Drawing2D.GraphicsPath(); var d = r * 2;
            p.AddArc(0, 0, d, d, 180, 90); p.AddArc(c.Width - d, 0, d, d, 270, 90);
            p.AddArc(c.Width - d, c.Height - d, d, d, 0, 90); p.AddArc(0, c.Height - d, d, d, 90, 90); p.CloseAllFigures();
            c.Region = new Region(p);
        }
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
