using System.Linq;

namespace WifiFix.Core
{
    public record RegSetting(string KeyPath, string Name, int Value);

    /// <summary>Starting the in-place Windows 10 to 11 upgrade with Microsoft's Installation Assistant, with the unsupported-hardware checks switched off.</summary>
    public static class Win11Upgrade
    {
        /// <summary>Windows 11's own setup.exe, started as if upgrading a Server edition, which skips the CPU/TPM/Secure Boot checks.
        /// (Microsoft's Installation Assistant does its own check and ignores the registry bypass, so it cannot be used.)</summary>
        public const string SetupArgs = "/product server";

        /// <summary>The Windows 11 ISO the technician put on the stick: DriverFix-USB\iso\*.iso first, then any *.iso next to DriverFix.exe.</summary>
        public static string? FindIso(string stickRoot)
        {
            foreach (var dir in new[] { System.IO.Path.Combine(stickRoot, "iso"), stickRoot })
            {
                if (!System.IO.Directory.Exists(dir)) continue;
                var f = System.IO.Directory.GetFiles(dir, "*.iso").OrderBy(x => x, System.StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>HKEY_LOCAL_MACHINE values: the upgrade-path allow flag, plus the setup LabConfig bypasses.</summary>
        public static readonly RegSetting[] BypassSettings =
        {
            new RegSetting(@"SYSTEM\Setup\MoSetup", "AllowUpgradesWithUnsupportedTPMOrCPU", 1),
            new RegSetting(@"SYSTEM\Setup\LabConfig", "BypassTPMCheck", 1),
            new RegSetting(@"SYSTEM\Setup\LabConfig", "BypassSecureBootCheck", 1),
            new RegSetting(@"SYSTEM\Setup\LabConfig", "BypassCPUCheck", 1),
            new RegSetting(@"SYSTEM\Setup\LabConfig", "BypassRAMCheck", 1),
        };

        public const string DownloadPage = "https://www.microsoft.com/software-download/windows11";
        public const long WindowsIsoMinBytes = 3L * 1024 * 1024 * 1024;

        /// <summary>An ISO in <paramref name="downloadsDir"/> written after <paramref name="since"/> and at least <paramref name="minBytes"/> long, with no browser partial file (.crdownload / .part / .tmp / .partial) for it.</summary>
        public static string? FindFinishedIso(string downloadsDir, System.DateTime since, long minBytes)
        {
            if (!System.IO.Directory.Exists(downloadsDir)) return null;
            foreach (var f in System.IO.Directory.GetFiles(downloadsDir, "*.iso"))
            {
                var fi = new System.IO.FileInfo(f);
                if (fi.LastWriteTimeUtc < since || fi.Length < minBytes) continue;
                if (new[] { ".crdownload", ".part", ".tmp", ".partial" }.Any(x => System.IO.File.Exists(f + x))) continue;
                return f;
            }
            return null;
        }

        public static bool IsWindows11(int build) => build >= 22000;
    }
}
