using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WifiFix.Core
{
    /// <summary>Maps hardware IDs to INF paths (relative to the drivers root, '/' separators).</summary>
    public class PackIndex
    {
        readonly Dictionary<string, List<(string Inf, DateTime Date)>> _exact =
            new Dictionary<string, List<(string, DateTime)>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, List<(string Inf, DateTime Date)>> _byBase =
            new Dictionary<string, List<(string, DateTime)>>(StringComparer.OrdinalIgnoreCase);

        public int Count => _exact.Count;

        public void Add(string infRelativePath, IEnumerable<string> hardwareIds, DateTime? driverDate = null)
        {
            var rel = infRelativePath.Replace('\\', '/');
            var date = driverDate ?? DateTime.MinValue;
            foreach (var id in hardwareIds)
            {
                Put(_exact, id, rel, date);
                Put(_byBase, BaseId(id), rel, date);
            }
        }

        static void Put(Dictionary<string, List<(string Inf, DateTime Date)>> map, string key, string rel, DateTime date)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<(string, DateTime)>();
            if (!list.Any(e => e.Inf == rel)) list.Add((rel, date));
        }

        /// <summary>All candidate INFs: exact ID matches first, then base-ID matches; newest driver first within each.</summary>
        public IReadOnlyList<string> FindAll(IEnumerable<string> deviceIds)
        {
            var ids = deviceIds.ToList();
            var result = new List<string>();
            void AddAll(List<(string Inf, DateTime Date)>? entries)
            {
                if (entries == null) return;
                foreach (var e in entries.OrderByDescending(x => x.Date))
                    if (!result.Contains(e.Inf)) result.Add(e.Inf);
            }
            foreach (var id in ids) { _exact.TryGetValue(id, out var l); AddAll(l); }
            foreach (var id in ids) { _byBase.TryGetValue(BaseId(id), out var l); AddAll(l); }
            return result;
        }

        public string? FindInf(IEnumerable<string> deviceIds) => FindAll(deviceIds).FirstOrDefault();

        /// <summary>Strips &amp;SUBSYS_/&amp;REV_/&amp;CC_ suffixes: PCI\VEN_8086&amp;DEV_2723.</summary>
        static string BaseId(string id)
        {
            var cut = id.Length;
            foreach (var marker in new[] { "&SUBSYS_", "&REV_", "&CC_", "&MI_" })
            {
                var i = id.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (i >= 0 && i < cut) cut = i;
            }
            return id.Substring(0, cut);
        }

        public static PackIndex Build(string driversRoot)
        {
            var idx = new PackIndex();
            var root = Path.GetFullPath(driversRoot);
            foreach (var inf in Directory.GetFiles(root, "*.inf", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                var rel = inf.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
                string text;
                try { text = File.ReadAllText(inf); } catch { continue; }
                idx.Add(rel, InfParser.GetHardwareIds(text), InfParser.GetDriverDate(text));
            }
            return idx;
        }

        public void Save(string path)
        {
            var sb = new StringBuilder("{\"version\":1,\"entries\":[\n");
            var first = true;
            foreach (var kv in _exact.OrderBy(k => k.Key, StringComparer.Ordinal))
                foreach (var e in kv.Value)
                {
                    if (!first) sb.Append(",\n");
                    first = false;
                    sb.Append("{\"id\":\"").Append(Esc(kv.Key)).Append("\",\"inf\":\"").Append(Esc(e.Inf)).Append("\"}");
                }
            sb.Append("\n]}\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public static PackIndex Load(string path)
        {
            var idx = new PackIndex();
            var text = File.ReadAllText(path, Encoding.UTF8);
            var rx = new Regex("\"id\":\"((?:[^\"\\\\]|\\\\.)*)\",\"inf\":\"((?:[^\"\\\\]|\\\\.)*)\"");
            foreach (Match m in rx.Matches(text))
                idx.Add(Unesc(m.Groups[2].Value), new[] { Unesc(m.Groups[1].Value) });
            return idx;
        }

        static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        static string Unesc(string s) => Regex.Replace(s, @"\\(.)", "$1");
    }
}
