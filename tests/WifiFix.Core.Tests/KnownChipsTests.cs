using System.Linq;
using System.Text.RegularExpressions;
using WifiFix.Core;
using Xunit;

public class KnownChipsTests
{
    static readonly Regex IdRx = new(@"^(PCI\\VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}|USB\\VID_[0-9A-F]{4}&PID_[0-9A-F]{4})$");

    [Fact]
    public void All_ids_are_well_formed_uppercase() =>
        Assert.All(KnownChips.All.SelectMany(c => c.HardwareIds), id => Assert.Matches(IdRx, id));

    [Fact]
    public void No_duplicate_ids_across_chips()
    {
        var ids = KnownChips.All.SelectMany(c => c.HardwareIds).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Covers_intel_wifi_from_7th_to_14th_gen()
    {
        var names = KnownChips.All.Where(c => c.Vendor == "Intel").Select(c => c.Name).ToList();
        foreach (var n in new[] { "Wireless-AC 8265", "Wireless-AC 9560", "Wi-Fi 6 AX200", "Wi-Fi 6 AX201", "Wi-Fi 6E AX210", "Wi-Fi 6E AX211", "Wi-Fi 7 BE200" })
            Assert.Contains(n, names);
        // The AX211 seen on the Dell Latitude 5330 (12th gen).
        Assert.Contains(@"PCI\VEN_8086&DEV_51F0", KnownChips.All.SelectMany(c => c.HardwareIds));
    }

    [Fact]
    public void Has_all_five_wifi_vendors_and_usb_adapters()
    {
        var vendors = KnownChips.All.Select(c => c.Vendor).Distinct().ToList();
        foreach (var v in new[] { "Intel", "Realtek", "MediaTek", "Qualcomm", "Broadcom" }) Assert.Contains(v, vendors);
        Assert.Contains(KnownChips.All, c => c.HardwareIds.Any(i => i.StartsWith(@"USB\")));
    }

    [Fact]
    public void FolderName_is_filesystem_safe()
    {
        Assert.Equal("Intel_net_Driver_Update_24.40.0.4", KnownChips.SafeName("Intel net Driver Update (24.40.0.4)"));
        Assert.DoesNotMatch(new Regex(@"[\\/:*?""<>|]"), KnownChips.SafeName(@"a\b/c:d*e?f""g<h>i|j"));
    }

    [Fact]
    public void Extras_cover_audio_touchpad_and_bluetooth_with_their_own_folders()
    {
        var kinds = KnownChips.Extras.Select(c => c.Vendor).Distinct().ToList();
        foreach (var k in new[] { "audio", "touchpad", "bluetooth" }) Assert.Contains(k, kinds);
        Assert.Contains(KnownChips.Extras, c => c.Name.Contains("Realtek") && c.Vendor == "audio");
        Assert.Contains(KnownChips.Extras, c => c.Name.Contains("Synaptics") && c.Vendor == "touchpad");
        Assert.Contains(KnownChips.Extras, c => c.Name.Contains("Intel") && c.Vendor == "bluetooth");
    }

    [Fact]
    public void Extras_have_search_terms_and_no_duplicates()
    {
        var terms = KnownChips.Extras.SelectMany(c => c.HardwareIds).ToList();
        Assert.All(terms, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        Assert.Equal(terms.Count, terms.Distinct().Count());
    }
}
