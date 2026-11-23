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
        static readonly Regex TagRx = new Regex("\"tag_name\"\\s*:\\s*\"v?([^\"]+)\"");
        static readonly Regex UrlRx = new Regex("\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"");

        /// <summary>Reads a GitHub "latest release" JSON: version from the tag, and the DriverFix.exe asset URL.</summary>
        public static ReleaseInfo? Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var tag = TagRx.Match(json);
            if (!tag.Success) return null;
            var url = UrlRx.Matches(json).Cast<Match>().Select(m => m.Groups[1].Value)
                .FirstOrDefault(u => u.EndsWith("/DriverFix.exe", StringComparison.OrdinalIgnoreCase));
            return url == null ? null : new ReleaseInfo(tag.Groups[1].Value, url);
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
            return ReleaseParser.Parse(wc.DownloadString("https://api.github.com/repos/" + AppInfo.Repo + "/releases/latest"));
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
