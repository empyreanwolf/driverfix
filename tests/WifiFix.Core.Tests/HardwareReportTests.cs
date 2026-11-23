using System.Linq;
using WifiFix.Core;
using Xunit;

public class HardwareReportTests
{
    static DeviceInfo D(string name, string cls, int code, params string[] ids) => new DeviceInfo(name, cls, code, ids, new string[0]);
    static PackIndex Idx() => new PackIndex();

    [Fact]
    public void Internal_pci_wifi_working_is_reported_ok()
    {
        var l = HardwareReport.Build(new[] { D("Intel(R) Wi-Fi 6 AX201 160MHz", "Net", 0, @"PCI\VEN_8086&DEV_06F0") });
        Assert.Contains("WiFi: Intel(R) Wi-Fi 6 AX201 160MHz - driver OK", l);
    }

    [Fact]
    public void Usb_wifi_dongle_is_ignored_and_pc_reports_none()
    {
        var l = HardwareReport.Build(new[] { D("Realtek 8811CU Wireless LAN 802.11ac USB NIC", "Net", 0, @"USB\VID_0BDA&PID_C811") });
        Assert.Contains(l, x => x.StartsWith("WiFi: none found on this PC"));
    }

    [Fact]
    public void Virtual_wifi_direct_adapter_is_not_hardware()
    {
        var l = HardwareReport.Build(new[] { D("Microsoft Wi-Fi Direct Virtual Adapter", "Net", 0, @"{5d624f94}\VWIFIMP_WFD") });
        Assert.Contains(l, x => x.StartsWith("WiFi: none found on this PC"));
    }

    [Fact]
    public void Missing_audio_is_flagged()
    {
        var l = HardwareReport.Build(new[] { D("Multimedia Audio Controller", "", 28, @"PCI\VEN_8086&DEV_A0C8&CC_0403") });
        Assert.Contains("Audio: Multimedia Audio Controller - DRIVER MISSING", l);
    }

    [Fact]
    public void No_audio_hardware_says_none_found()
    {
        var l = HardwareReport.Build(new DeviceInfo[0]);
        Assert.Contains("Audio: none found on this PC", l);
        Assert.Contains("Touchpad: none found on this PC", l);
        Assert.Contains("Bluetooth: none found on this PC", l);
    }

    [Fact]
    public void Usb_headset_is_not_the_pcs_audio()
    {
        var l = HardwareReport.Build(new[] { D("USB Audio Device", "MEDIA", 0, @"USB\VID_1234&PID_0001") });
        Assert.Contains("Audio: none found on this PC", l);
    }

    [Fact]
    public void Bluetooth_radio_counts_but_microsoft_enumerator_does_not()
    {
        var l = HardwareReport.Build(new[] {
            D("Intel(R) Wireless Bluetooth(R)", "Bluetooth", 0, @"USB\VID_8087&PID_0026"),
            D("Microsoft Bluetooth Enumerator", "Bluetooth", 0, @"BTH\MS_BTHBRB") });
        Assert.Contains("Bluetooth: Intel(R) Wireless Bluetooth(R) - driver OK", l);
        Assert.DoesNotContain(l, x => x.Contains("Enumerator"));
    }

    [Fact]
    public void Touchpad_found_by_name()
    {
        var l = HardwareReport.Build(new[] { D("Synaptics SMBus TouchPad", "Mouse", 0, @"ACPI\SYN1234") });
        Assert.Contains("Touchpad: Synaptics SMBus TouchPad - driver OK", l);
    }

    [Fact]
    public void Missing_wifi_on_usb_bus_is_not_the_pcs_problem() =>
        Assert.False(DeviceFilter.IsMissingWifi(D("Wireless LAN 802.11ac USB NIC", "", 28, @"USB\VID_0BDA&PID_C811"), Idx()));
}
