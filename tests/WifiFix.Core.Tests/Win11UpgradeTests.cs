using System.Linq;
using WifiFix.Core;
using Xunit;

public class Win11UpgradeTests
{
    [Fact]
    public void Bypass_sets_unsupported_cpu_tpm_allow_flag_and_labconfig_bypasses()
    {
        var p = Win11Upgrade.BypassSettings;
        Assert.Contains(p, s => s.KeyPath == @"SYSTEM\Setup\MoSetup" && s.Name == "AllowUpgradesWithUnsupportedTPMOrCPU" && s.Value == 1);
        foreach (var n in new[] { "BypassTPMCheck", "BypassSecureBootCheck", "BypassCPUCheck", "BypassRAMCheck" })
            Assert.Contains(p, s => s.KeyPath == @"SYSTEM\Setup\LabConfig" && s.Name == n && s.Value == 1);
    }

    [Theory]
    [InlineData(19045, false)]   // Windows 10 22H2
    [InlineData(22000, true)]    // Windows 11 21H2
    [InlineData(26100, true)]
    public void Windows11_is_build_22000_or_newer(int build, bool is11) => Assert.Equal(is11, Win11Upgrade.IsWindows11(build));

    [Fact]
    public void Assistant_is_downloaded_from_microsofts_official_link() =>
        Assert.StartsWith("https://go.microsoft.com/", Win11Upgrade.AssistantUrl);

    [Fact]
    public void Assistant_runs_with_eula_skipped() => Assert.Contains("/SkipEULA", Win11Upgrade.AssistantArgs);
}
