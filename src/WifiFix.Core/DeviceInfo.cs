using System;
using System.Collections.Generic;
using System.Linq;

namespace WifiFix.Core
{
    public record DeviceInfo(string Name, string PnpClass, int ProblemCode,
        IReadOnlyList<string> HardwareIds, IReadOnlyList<string> CompatibleIds)
    {
        public IEnumerable<string> AllIds => HardwareIds.Concat(CompatibleIds);
    }

    public static class DeviceFilter
    {
        static readonly string[] WifiWords = { "wireless", "wi-fi", "wifi", "wlan", "802.11", "network controller" };

        /// <summary>A device with no driver (problem code 28) that is, or probably is, a WiFi adapter.</summary>
        public static bool IsMissingWifi(DeviceInfo d, PackIndex index)
        {
            if (d.ProblemCode != 28) return false;
            if (HardwareReport.IsUsbOnly(d)) return false;   // a plug-in USB WiFi dongle is not the PC's own WiFi
            if (d.Name.IndexOf("bluetooth", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (WifiWords.Any(w => d.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            // PCI class 0x0280 = "other network controller", which is how WiFi cards identify themselves.
            if (d.AllIds.Any(i => i.IndexOf("&CC_0280", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            if (LooksAudio(d)) return false; // a pack match for an audio driver must not make it a "WiFi" device
            // Only PCI devices can be guessed as WiFi from a pack match; ACPI/HID are touchpads, sensors etc.
            var netIds = d.AllIds.Where(i => i.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase)).ToList();
            return netIds.Count > 0 && index.FindInf(netIds) != null;
        }

        static readonly string[] AudioWords = { "audio", "sound", "speaker" };

        public static bool LooksAudio(DeviceInfo d) =>
            AudioWords.Any(w => d.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)
            || d.AllIds.Any(i => i.IndexOf("&CC_0403", StringComparison.OrdinalIgnoreCase) >= 0
                              || i.IndexOf("&CC_0401", StringComparison.OrdinalIgnoreCase) >= 0
                              || i.StartsWith(@"HDAUDIO\", StringComparison.OrdinalIgnoreCase));

        /// <summary>A device with no driver (problem code 28) that is, or probably is, an audio device. WiFi/Bluetooth are never audio.</summary>
        public static bool IsMissingAudio(DeviceInfo d, PackIndex index)
        {
            if (d.ProblemCode != 28) return false;
            if (d.Name.IndexOf("bluetooth", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (IsMissingWifi(d, index)) return false;
            // PCI class 0x0403 = HD Audio, 0x0401 = multimedia audio; HDAUDIO\ = codec on the HD Audio bus.
            return LooksAudio(d);
        }

        /// <summary>Any other device with no driver (touchpad, chipset, card reader...). WiFi and audio are handled separately.</summary>
        public static bool IsMissingOther(DeviceInfo d, PackIndex index) =>
            d.ProblemCode == 28 && !IsMissingWifi(d, index) && !LooksAudio(d);
    }
}
