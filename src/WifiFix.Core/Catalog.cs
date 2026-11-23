using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace WifiFix.Core
{
    public record MachineInfo(string Manufacturer, string Model, string Serial)
    {
        public string Describe()
        {
            var name = string.Join(" ", new[] { Manufacturer, Model }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
            if (name.Length == 0) return "Unknown PC";
            return string.IsNullOrWhiteSpace(Serial) ? name : name + " (S/N " + Serial.Trim() + ")";
        }
    }

    public record CatalogRow(string UpdateId, string Title, string Products, string Classification, string LastUpdated, string Version);

    /// <summary>Parses Microsoft Update Catalog pages (search results and the download dialog).</summary>
    public static class CatalogParser
    {
        static readonly Regex RowRx = new Regex(
            @"<tr[^>]*\bid\s*=\s*['""]([0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})_R\d+['""][^>]*>(.*?)</tr>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        static readonly Regex CellRx = new Regex(@"<td[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        static readonly Regex TagRx = new Regex(@"<[^>]+>");
        static readonly Regex UrlRx = new Regex(@"downloadInformation\[\d+\]\.files\[\d+\]\.url\s*=\s*['""]([^'""]+)['""]");

        public static IReadOnlyList<CatalogRow> ParseSearch(string html)
        {
            var rows = new List<CatalogRow>();
            foreach (Match m in RowRx.Matches(html))
            {
                var cells = CellRx.Matches(m.Groups[2].Value).Cast<Match>().Select(c => Clean(c.Groups[1].Value)).ToList();
                if (cells.Count < 6) continue;
                rows.Add(new CatalogRow(m.Groups[1].Value, cells[1], cells[2], cells[3], cells[4], cells[5]));
            }
            return rows;
        }

        /// <summary>Newest Windows 10 driver row, or null.</summary>
        public static CatalogRow? PickBest(IEnumerable<CatalogRow> rows) =>
            rows.Where(r => r.Products.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0
                         && r.Classification.IndexOf("Driver", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(r => Version.TryParse(r.Version, out var v) ? v : new Version(0, 0))
                .ThenByDescending(r => DateTime.TryParse(r.LastUpdated, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var d) ? d : DateTime.MinValue)
                .FirstOrDefault();

        public static IReadOnlyList<string> ParseDownloadUrls(string html) =>
            UrlRx.Matches(html).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();

        static string Clean(string cellHtml) =>
            Regex.Replace(WebUtility.HtmlDecode(TagRx.Replace(cellHtml, " ")), @"\s+", " ").Trim();
    }

    /// <summary>Tries the first provider; only if it finds nothing, tries the second.</summary>
    public class FallbackOnlineProvider : IOnlineProvider
    {
        readonly IOnlineProvider _first, _second;
        public FallbackOnlineProvider(IOnlineProvider first, IOnlineProvider second) { _first = first; _second = second; }
        public bool HasInternet() => _first.HasInternet() || _second.HasInternet();
        public string? Download(DeviceInfo device, string workFolder) =>
            _first.Download(device, workFolder) ?? _second.Download(device, workFolder);
    }
}
