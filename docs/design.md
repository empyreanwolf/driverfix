# WifiFix — 1-click WiFi driver installer (design)

## Intent
After reinstalling Windows 10 on refurbished 6th–12th gen desktops/laptops, the built-in WiFi often has no driver. Today staff plug in a USB WiFi adapter and hunt for drivers manually, which wastes time. WifiFix is a USB-stick tool: one double-click, WiFi works.

## Goals
- One button, nothing to choose. Auto-elevates to admin.
- Detects missing WiFi device(s) by hardware ID (PCI\VEN_xxxx&DEV_xxxx).
- Installs a matching driver silently, then confirms WiFi is up ("WiFi working").
- Covers WiFi chips common in 6th–12th gen PCs: Intel (AC 7265/8260/8265/9260/9560, AX200/201/210/211), Realtek, Qualcomm/Atheros, MediaTek, Broadcom.
- Hybrid: offline pack first; if no match, use the temporary USB adapter's internet to fetch the exact driver.
- Every driver fetched online is saved back to the stick, so the pack grows.
- Pack also carries drivers for common USB WiFi adapter chipsets (Realtek RTL81xx/88xx, MediaTek MT76xx, Ralink) so the adapter works immediately.

## Non-goals
- Not a general driver updater (no GPU/audio/chipset). Bluetooth, LAN: later.
- No Windows 11 / Linux in v1 (Win11 planned next; keep pack folders per-OS so it's an addition, not a rewrite).
- No auto-run on USB insert: Windows 10 blocks AutoRun for USB drives. Launcher is a single double-click.

## Anti-goals (failure even if it "works")
- Needs the user to pick a driver or answer prompts.
- Installs a wrong driver or breaks a working network card.
- Takes longer than manual searching.

## Constraints
- Target: Windows 10 64-bit, fresh install (no extras, possibly no .NET runtime).
- Must run with no internet at all (offline path).
- Driver redistribution: use vendor-published packages only.

## Approaches considered
- A. Offline pack only: fastest, but fails on any chip not in pack, manual upkeep.
- B. Online only via adapter: small and current, but fails if adapter has no driver or connection flaky.
- **C. Hybrid (chosen by user):** offline first, online fallback, saves back to pack.

## Decisions
- Single self-contained `.exe` (C#/.NET, single-file, no runtime needed), one big button, status log below.
- Detection: WMI Win32_PnPEntity, network-class devices with problem code 28 / unknown devices; read hardware + compatible IDs.
- Offline match: index built at pack-build time by parsing every INF's hardware IDs into `index.json`; install via `pnputil /add-driver <inf> /install`.
- Online: use the adapter's connection; ask Windows Update (WUA API) for the driver by hardware ID; fall back to Microsoft Update Catalog. After install, export the driver (`pnputil /export-driver`) into the pack and update `index.json`.
- Pack layout: `drivers/win10/<vendor>/<chip>/*.inf` (OS folder allows `win11` later).
- Log every run to `logs/` on the stick (PC model, chip ID, result) to see which chips are missing.
- Optional: phone USB tethering also works as the temporary internet source (no driver needed).

## Left to the builder
- Exact pack contents/size, catalog scraping details, UI polish, code signing/SmartScreen handling, handling of unsigned drivers.
