# DriverFix

1-click WiFi (and optional audio / touchpad / Bluetooth) driver installer for refurbished Windows 10 PCs.
Runs from a USB stick: offline driver pack first, then Microsoft Update Catalog lookup when online.

Releases: attach `DriverFix.exe` to a release tagged e.g. `v1.9.2`. Copies of WifiFix on USB sticks check
the latest release and update themselves with the UPDATE SOFTWARE button.

Build: `dotnet build src/WifiFix.App -c Release` (net48 WinForms). Tests: `dotnet test tests/WifiFix.Core.Tests`.
