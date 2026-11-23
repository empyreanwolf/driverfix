using WifiFix.Core;
using Xunit;

public class DeviceFilterTests
{
    static PackIndex Index()
    {
        var i = new PackIndex();
        i.Add("intel/a.inf", new[] { @"PCI\VEN_8086&DEV_2723" });
        return i;
    }

    static DeviceInfo Dev(string name, int code, params string[] ids) =>
        new DeviceInfo(name, "", code, ids, new string[0]);

    [Fact]
    public void Code28_network_controller_is_missing_wifi() =>
        Assert.True(DeviceFilter.IsMissingWifi(Dev("Network Controller", 28, @"PCI\VEN_1234&DEV_0001"), Index()));

    [Fact]
    public void Code28_bluetooth_is_ignored() =>
        Assert.False(DeviceFilter.IsMissingWifi(Dev("Bluetooth Radio", 28, @"PCI\VEN_8086&DEV_2723"), Index()));

    [Fact]
    public void Working_device_code0_is_ignored() =>
        Assert.False(DeviceFilter.IsMissingWifi(Dev("Intel(R) Wi-Fi 6 AX200 160MHz", 0, @"PCI\VEN_8086&DEV_2723"), Index()));

    [Fact]
    public void Code28_unnamed_device_with_id_in_index_is_missing_wifi() =>
        Assert.True(DeviceFilter.IsMissingWifi(Dev("Unknown device", 28, @"PCI\VEN_8086&DEV_2723&SUBSYS_1&REV_1A"), Index()));

    [Fact]
    public void Code28_unnamed_pci_network_class_0280_is_missing_wifi() =>
        Assert.True(DeviceFilter.IsMissingWifi(Dev("Unknown device", 28, @"PCI\VEN_9999&DEV_0001&SUBSYS_1&REV_01", @"PCI\VEN_9999&DEV_0001&CC_028000"), Index()));

    [Fact]
    public void Code28_ethernet_class_0200_is_ignored() =>
        Assert.False(DeviceFilter.IsMissingWifi(Dev("Unknown device", 28, @"PCI\VEN_10EC&DEV_8168&CC_020000"), Index()));

    [Fact]
    public void Audio_code28_by_name_is_missing_audio()
    {
        Assert.True(DeviceFilter.IsMissingAudio(Dev("Multimedia Audio Controller", 28, @"PCI\VEN_8086&DEV_51C8"), Index()));
        Assert.True(DeviceFilter.IsMissingAudio(Dev("Intel Smart Sound Technology", 28, @"PCI\VEN_8086&DEV_51C8"), Index()));
    }

    [Fact]
    public void Audio_code28_by_pci_class_0403_or_0401_is_missing_audio()
    {
        Assert.True(DeviceFilter.IsMissingAudio(Dev("Unknown device", 28, @"PCI\VEN_8086&DEV_51C8&CC_040300"), Index()));
        Assert.True(DeviceFilter.IsMissingAudio(Dev("Unknown device", 28, @"PCI\VEN_8086&DEV_51C8&CC_040100"), Index()));
    }

    [Fact]
    public void Audio_code28_hdaudio_codec_id_is_missing_audio() =>
        Assert.True(DeviceFilter.IsMissingAudio(Dev("Unknown device", 28, @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0256&SUBSYS_1028"), Index()));

    [Fact]
    public void Working_audio_wifi_and_video_are_not_missing_audio()
    {
        Assert.False(DeviceFilter.IsMissingAudio(Dev("Realtek High Definition Audio", 0, @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0256"), Index()));
        Assert.False(DeviceFilter.IsMissingAudio(Dev("Network Controller", 28, @"PCI\VEN_8086&DEV_2723&CC_028000"), Index()));
        Assert.False(DeviceFilter.IsMissingAudio(Dev("Video Controller", 28, @"PCI\VEN_10DE&DEV_1C82&CC_030000"), Index()));
    }

    [Fact]
    public void Code28_video_controller_is_ignored() =>
        Assert.False(DeviceFilter.IsMissingWifi(Dev("Video Controller", 28, @"PCI\VEN_10DE&DEV_1C82"), Index()));
}
