using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WifiFix.Core
{
    public record KnownChip(string Vendor, string Name, string[] HardwareIds);

    /// <summary>
    /// WiFi chips commonly found in Intel 7th-14th gen PCs (laptops, desktops, motherboards) plus common USB dongles.
    /// IDs are the base PCI/USB IDs; PackBuilder searches the Microsoft Update Catalog for each and skips what it can't find.
    /// </summary>
    public static class KnownChips
    {
        static KnownChip C(string v, string n, params string[] ids) => new KnownChip(v, n, ids);
        const string I = @"PCI\VEN_8086&DEV_";
        const string R = @"PCI\VEN_10EC&DEV_";
        const string M = @"PCI\VEN_14C3&DEV_";
        const string Q = @"PCI\VEN_168C&DEV_";
        const string B = @"PCI\VEN_14E4&DEV_";

        public static readonly IReadOnlyList<KnownChip> All = new[]
        {
            // Intel - 7th/8th gen era
            C("Intel", "Dual Band Wireless-AC 7265", I + "095A", I + "095B"),
            C("Intel", "Wireless-AC 3165", I + "3165"),
            C("Intel", "Wireless-AC 3168", I + "24FB"),
            C("Intel", "Dual Band Wireless-AC 8260", I + "24F3", I + "24F4"),
            C("Intel", "Wireless-AC 8265", I + "24FD"),
            C("Intel", "Wireless-AC 9260", I + "2526"),
            // Intel - 8th to 11th gen (CNVi)
            C("Intel", "Wireless-AC 9560", I + "9DF0", I + "A370", I + "02F0", I + "06F0", I + "34F0", I + "3DF0"),
            C("Intel", "Wi-Fi 6 AX200", I + "2723"),
            C("Intel", "Wi-Fi 6 AX201", I + "43F0", I + "A0F0", I + "4DF0"),
            // Intel - 11th to 14th gen
            C("Intel", "Wi-Fi 6E AX210", I + "2725"),
            C("Intel", "Wi-Fi 6E AX211", I + "51F0", I + "51F1", I + "54F0", I + "7A70", I + "7AF0", I + "7E40"),
            C("Intel", "Wi-Fi 7 BE200", I + "272B"),
            C("Intel", "Wi-Fi 7 BE201", I + "A840"),
            // Realtek
            C("Realtek", "RTL8188EE", R + "8179"),
            C("Realtek", "RTL8723BE", R + "B723"),
            C("Realtek", "RTL8723DE", R + "D723"),
            C("Realtek", "RTL8821AE", R + "8821"),
            C("Realtek", "RTL8821CE", R + "C821"),
            C("Realtek", "RTL8822BE", R + "B822"),
            C("Realtek", "RTL8822CE", R + "C822"),
            C("Realtek", "RTL8852AE", R + "8852"),
            C("Realtek", "RTL8852BE", R + "B852"),
            C("Realtek", "RTL8852CE", R + "C852"),
            // MediaTek
            C("MediaTek", "MT7921", M + "7961"),
            C("MediaTek", "MT7922", M + "0616"),
            C("MediaTek", "MT7902", M + "7902"),
            C("MediaTek", "MT7630E", M + "7630"),
            C("MediaTek", "MT7662E", M + "7662"),
            // Qualcomm
            C("Qualcomm", "QCA9377", Q + "0042"),
            C("Qualcomm", "QCA6174", Q + "003E"),
            C("Qualcomm", "QCA9565", Q + "0036"),
            C("Qualcomm", "FastConnect 6900", @"PCI\VEN_17CB&DEV_1103"),
            // Broadcom
            C("Broadcom", "BCM4352", B + "43B1"),
            C("Broadcom", "BCM43142", B + "4365"),
            // Common USB dongles
            C("Realtek", "USB RTL8188EUS", @"USB\VID_0BDA&PID_8179"),
            C("Realtek", "USB RTL8188CUS", @"USB\VID_0BDA&PID_8176"),
            C("Realtek", "USB RTL8192CU", @"USB\VID_0BDA&PID_8178"),
            C("Realtek", "USB RTL8812AU", @"USB\VID_0BDA&PID_8812"),
            C("Realtek", "USB RTL8821CU", @"USB\VID_0BDA&PID_C811", @"USB\VID_0BDA&PID_C820"),
            C("Realtek", "USB RTL8822BU", @"USB\VID_0BDA&PID_B812"),
            C("MediaTek", "USB MT7601U", @"USB\VID_148F&PID_7601"),
            C("MediaTek", "USB MT7612U", @"USB\VID_0E8D&PID_7612"),
            C("MediaTek", "USB RT5370", @"USB\VID_148F&PID_5370"),
        };

        /// <summary>
        /// Non-WiFi drivers for the offline pack. These are catalog SEARCH TERMS (not hardware IDs): the folder is the Vendor
        /// ("audio", "touchpad", "bluetooth"). Real hardware IDs are read from the downloaded INFs when the pack is indexed.
        /// </summary>
        public static readonly IReadOnlyList<KnownChip> Extras = new[]
        {
            C("audio", "Realtek High Definition Audio", "Realtek High Definition Audio"),
            C("audio", "Realtek Audio (Dell/HP/Lenovo)", "Realtek Audio"),
            C("audio", "Intel Smart Sound Technology", "Intel Smart Sound Technology"),
            C("audio", "Conexant SmartAudio", "Conexant Audio"),
            C("audio", "IDT High Definition Audio", "IDT High Definition Audio CODEC"),
            C("touchpad", "Synaptics Precision Touchpad", "Synaptics Pointing"),
            C("touchpad", "Synaptics SMBus TouchPad", "Synaptics Mouse"),
            C("touchpad", "ELAN Touchpad", "Elantech"),
            C("touchpad", "Intel Serial IO (I2C touchpad bus)", "Intel Serial IO"),
            C("touchpad", "Alps Pointing Device", "Alps Pointing"),
            C("bluetooth", "Intel Wireless Bluetooth", "Intel Bluetooth"),
            C("bluetooth", "Realtek Bluetooth", "Realtek Bluetooth"),
            C("bluetooth", "Qualcomm Atheros Bluetooth", "Qualcomm Bluetooth"),
            C("bluetooth", "MediaTek Bluetooth", "MediaTek Bluetooth"),
        };

        /// <summary>Filesystem-safe folder name: "Intel net Driver Update (24.40.0.4)" → "Intel_net_Driver_Update_24.40.0.4".</summary>
        public static string SafeName(string title) =>
            Regex.Replace(title, @"[\\/:*?""<>|()\s]+", "_").Trim('_');
    }
}
