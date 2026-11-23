using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WifiFix.Core
{
    public enum HwKind { WiFi, Audio, Touchpad, Bluetooth }

    /// <summary>Plain-language report of the PC's own WiFi, audio, touchpad and Bluetooth hardware and whether each has a driver.</summary>
    public static class HardwareReport
    {
        static bool Has(DeviceInfo d, string prefix) => d.HardwareIds.Any(i => i.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        /// <summary>True when the device sits only on the USB bus (a plug-in dongle, headset or mouse), not on the PC's own PCI/ACPI hardware.</summary>
        public static bool IsUsbOnly(DeviceInfo d) => Has(d, @"USB\") && !Has(d, @"PCI\");

        static readonly Regex TouchpadName = new Regex("touchpad|synaptics|elan|alps|i2c hid|precision", RegexOptions.IgnoreCase);

        public static HwKind? Classify(DeviceInfo d)
        {
            var name = d.Name ?? "";
            if (d.PnpClass == "Bluetooth" || name.IndexOf("bluetooth", StringComparison.OrdinalIgnoreCase) >= 0)
                return (Has(d, @"USB\") || Has(d, @"PCI\")) ? HwKind.Bluetooth : (HwKind?)null;   // Microsoft's virtual enumerators have neither
            if (IsUsbOnly(d)) return null;                                                          // plug-in dongles/headsets are not the PC's own
            if (Has(d, @"PCI\") && (d.AllIds.Any(i => i.IndexOf("&CC_0280", StringComparison.OrdinalIgnoreCase) >= 0)
                || (d.PnpClass != "MEDIA" && Regex.IsMatch(name, "wi-?fi|wireless|wlan|802\\.11|network controller", RegexOptions.IgnoreCase))))
                return HwKind.WiFi;
            if (DeviceFilter.LooksAudio(d) && (Has(d, @"PCI\") || Has(d, @"HDAUDIO\") || Has(d, @"INTELAUDIO\") || Has(d, @"ACPI\")))
                return HwKind.Audio;
            if (TouchpadName.IsMatch(name)) return HwKind.Touchpad;
            return null;
        }

        public static List<string> Build(IEnumerable<DeviceInfo> all)
        {
            var found = all.Select(d => new { d, k = Classify(d) }).Where(x => x.k != null).ToList();
            var lines = new List<string>();
            foreach (HwKind kind in Enum.GetValues(typeof(HwKind)))
            {
                var mine = found.Where(x => x.k == kind).Select(x => x.d)
                    .GroupBy(d => d.Name).Select(g => g.OrderByDescending(d => d.ProblemCode).First()).ToList();
                var label = kind == HwKind.WiFi ? "WiFi" : kind.ToString();
                if (mine.Count == 0) lines.Add(label + ": none found on this PC" + (kind == HwKind.WiFi ? " (plug-in USB WiFi adapters are not counted)" : ""));
                else foreach (var d in mine) lines.Add(label + ": " + d.Name + " - " + (d.ProblemCode == 0 ? "driver OK" : "DRIVER MISSING"));
            }
            return lines;
        }
    }
}
