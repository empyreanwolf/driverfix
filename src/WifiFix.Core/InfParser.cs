using System;
using System.Collections.Generic;
using System.Linq;

namespace WifiFix.Core
{
    public static class InfParser
    {
        public static IReadOnlyList<string> GetHardwareIds(string infText)
        {
            // Pass 1: split into sections (comments stripped).
            var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            List<string>? current = null;
            foreach (var raw in infText.Replace("\r", "").Split('\n'))
            {
                var line = StripComment(raw).Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    var name = line.Substring(1, line.Length - 2).Trim();
                    if (!sections.TryGetValue(name, out current))
                        sections[name] = current = new List<string>();
                    continue;
                }
                current?.Add(line);
            }

            // Pass 2: model section names come from [Manufacturer]: "%Mfg%=Base,Deco1,Deco2".
            var modelSections = new List<string>();
            if (sections.TryGetValue("Manufacturer", out var mfg))
            {
                foreach (var l in mfg)
                {
                    var eq = l.IndexOf('=');
                    if (eq < 0) continue;
                    var parts = l.Substring(eq + 1).Split(',').Select(p => p.Trim()).ToArray();
                    if (parts.Length == 0 || parts[0].Length == 0) continue;
                    modelSections.Add(parts[0]);
                    foreach (var deco in parts.Skip(1))
                        modelSections.Add(parts[0] + "." + deco);
                }
            }

            // Pass 3: in model sections, "desc = install, id1, id2".
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in modelSections)
            {
                if (!sections.TryGetValue(name, out var lines)) continue;
                foreach (var l in lines)
                {
                    var eq = l.IndexOf('=');
                    if (eq < 0) continue;
                    var parts = l.Substring(eq + 1).Split(',').Select(p => p.Trim().Trim('"')).ToArray();
                    foreach (var id in parts.Skip(1))
                    {
                        if (id.Length == 0) continue;
                        var up = id.ToUpperInvariant();
                        if (seen.Add(up)) ids.Add(up);
                    }
                }
            }
            return ids;
        }

        /// <summary>DriverVer = MM/DD/YYYY,version → the date, or null if absent/invalid.</summary>
        public static DateTime? GetDriverDate(string infText)
        {
            var m = System.Text.RegularExpressions.Regex.Match(infText,
                @"(?im)^\s*DriverVer\s*=\s*(\d{1,2})/(\d{1,2})/(\d{4})");
            if (!m.Success) return null;
            try { return new DateTime(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)); }
            catch { return null; }
        }

        static string StripComment(string line)
        {
            var inQuote = false;
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] == '"') inQuote = !inQuote;
                else if (line[i] == ';' && !inQuote) return line.Substring(0, i);
            }
            return line;
        }
    }
}
