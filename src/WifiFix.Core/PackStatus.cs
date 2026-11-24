using System;
using System.IO;
using System.Linq;

namespace WifiFix.Core
{
    public static class AppInfo
    {
        public const string Version = "1.9.13";
        public const string Repo = "empyreanwolf/driverfix";
    }

    /// <summary>What the offline pack on the stick holds and when it was last checked online.</summary>
    public record PackStatus(int Packages, DateTime? LastChecked)
    {
        public static PackStatus Read(string driversRoot)
        {
            var packages = 0; DateTime? last = null;
            try
            {
                var state = Path.Combine(driversRoot, "pack-state.txt");
                if (File.Exists(state)) packages = File.ReadAllLines(state).Count(l => l.Trim().Length > 0);
                var report = Path.Combine(driversRoot, "PACK-REPORT.txt");
                if (File.Exists(report)) last = File.GetLastWriteTime(report);
            }
            catch { /* unreadable stick: show as empty */ }
            return new PackStatus(packages, last);
        }

        public string Describe() =>
            LastChecked == null && Packages == 0
                ? "Offline pack: empty (click UPDATE DRIVER PACK while online)"
                : "Offline pack: " + Packages + " downloaded driver package(s), last checked online " +
                  (LastChecked.HasValue ? LastChecked.Value.ToString("yyyy-MM-dd HH:mm") : "never");
    }
}
