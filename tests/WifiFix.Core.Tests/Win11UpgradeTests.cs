using System;
using System.IO;
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
    public void Setup_runs_in_server_mode_to_skip_hardware_checks() => Assert.Equal("/product server", Win11Upgrade.SetupArgs);

    [Fact]
    public void Finds_iso_in_iso_folder_first_then_stick_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "w11_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "iso"));
        try
        {
            Assert.Null(Win11Upgrade.FindIso(root));
            File.WriteAllText(Path.Combine(root, "root.iso"), "x");
            Assert.EndsWith("root.iso", Win11Upgrade.FindIso(root));
            File.WriteAllText(Path.Combine(root, "iso", "Win11_25H2.iso"), "x");
            Assert.EndsWith("Win11_25H2.iso", Win11Upgrade.FindIso(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
