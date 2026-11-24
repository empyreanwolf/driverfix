namespace WifiFix.Core
{
    public record RegSetting(string KeyPath, string Name, int Value);

    /// <summary>Starting the in-place Windows 10 to 11 upgrade with Microsoft's Installation Assistant, with the unsupported-hardware checks switched off.</summary>
    public static class Win11Upgrade
    {
        /// <summary>Microsoft's official redirect to Windows11InstallationAssistant.exe.</summary>
        public const string AssistantUrl = "https://go.microsoft.com/fwlink/?linkid=2171764";
        public const string AssistantArgs = "/SkipEULA";

        /// <summary>HKEY_LOCAL_MACHINE values: the upgrade-path allow flag, plus the setup LabConfig bypasses.</summary>
        public static readonly RegSetting[] BypassSettings =
        {
            new RegSetting(@"SYSTEM\Setup\MoSetup", "AllowUpgradesWithUnsupportedTPMOrCPU", 1),
            new RegSetting(@"SYSTEM\Setup\LabConfig", "BypassTPMCheck", 1),
            new RegSetting(@"SYSTEM\Setup\LabConfig", "BypassSecureBootCheck", 1),
            new RegSetting(@"SYSTEM\Setup\LabConfig", "BypassCPUCheck", 1),
            new RegSetting(@"SYSTEM\Setup\LabConfig", "BypassRAMCheck", 1),
        };

        public static bool IsWindows11(int build) => build >= 22000;
    }
}
