using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace WifiFix.Core
{
    public record ReleaseInfo(string Version, string DownloadUrl);

    public static class ReleaseParser
    {
        /// <summary>The newest build is published in the repo as latest/version.txt + latest/DriverFix.exe (no GitHub Releases needed).</summary>
        public static string RawBase => "https://raw.githubusercontent.com/" + AppInfo.Repo + "/main/latest/";

        public static ReleaseInfo? FromVersionFile(string text)
        {
            var v = (text ?? "").Trim().TrimStart('v', 'V');
            return Version.TryParse(v, out _) ? new ReleaseInfo(v, RawBase + "DriverFix.exe") : null;
        }

        public static bool IsNewer(string current, string latest) =>
            Version.TryParse(current, out var c) && Version.TryParse(latest, out var l) && l > c;
    }

    public static class SelfUpdate
    {
        const long MinExeBytes = 100_000;

        /// <summary>Checks GitHub for the latest release. Returns null when none/unreachable. Network call.</summary>
        public static ReleaseInfo? CheckLatest()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "DriverFix/" + AppInfo.Version;   // GitHub's API requires a User-Agent
            return ReleaseParser.FromVersionFile(wc.DownloadString(ReleaseParser.RawBase + "version.txt"));
        }

        /// <summary>Downloads the release exe to <paramref name="target"/>. Network call.</summary>
        public static void Download(ReleaseInfo r, string target)
        {
            using var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "DriverFix/" + AppInfo.Version;
            wc.DownloadFile(r.DownloadUrl, target);
        }

        /// <summary>
        /// Puts the downloaded exe in place of the current one. A running exe can be renamed but not overwritten, so the
        /// current one becomes DriverFix.old.exe. A bad download (not an exe, too small) is rejected and nothing changes.
        /// </summary>
        public static void Swap(string currentExe, string downloaded)
        {
            var fi = new FileInfo(downloaded);
            byte[] head = new byte[2];
            using (var fs = fi.OpenRead()) { if (fs.Read(head, 0, 2) != 2) head[0] = 0; }
            if (fi.Length < MinExeBytes || head[0] != 'M' || head[1] != 'Z')
            {
                try { fi.Delete(); } catch { }
                throw new InvalidDataException("The downloaded file is not a valid DriverFix.exe.");
            }
            var old = Path.Combine(Path.GetDirectoryName(currentExe)!, "DriverFix.old.exe");
            if (File.Exists(old)) File.Delete(old);
            File.Move(currentExe, old);
            File.Move(downloaded, currentExe);
        }

        public static void CleanupOld(string exeDir)
        {
            try { var old = Path.Combine(exeDir, "DriverFix.old.exe"); if (File.Exists(old)) File.Delete(old); } catch { }
        }
    }
}
