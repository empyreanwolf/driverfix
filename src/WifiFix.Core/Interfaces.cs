using System.Collections.Generic;

namespace WifiFix.Core
{
    public record ProcessResult(int ExitCode, string Output);

    public interface IProcessRunner { ProcessResult Run(string exe, string args); }

    public interface IDeviceScanner { IReadOnlyList<DeviceInfo> Scan(); }

    /// <summary>Every WiFi/audio/touchpad/Bluetooth-related device Windows lists, working or not.</summary>
    public interface IHardwareInventory { IReadOnlyList<DeviceInfo> ScanAll(); }

    public interface IOnlineProvider
    {
        bool HasInternet();
        /// <summary>Downloads a driver for the device; returns a folder containing the INF, or null.</summary>
        string? Download(DeviceInfo device, string workFolder);
    }

    public interface IWifiVerifier
    {
        bool AnyWifiAdapterPresent();
        bool WifiAdapterUp();
    }

    public interface ILog { void Info(string message); void Warn(string message); }
}
