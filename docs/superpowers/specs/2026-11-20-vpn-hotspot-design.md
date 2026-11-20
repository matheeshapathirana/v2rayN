# VPN Hotspot — Design (short)

Date: 2026-11-20 · Size: small feature · Platform: Windows (WPF app) only

## Intent
Let other devices (phones, TVs, consoles) use the VPN by joining a Wi-Fi hotspot run by
v2rayN, with no proxy setup on the device — replacing the need for MyPublicWiFi
("Router Mode, Internet Connection = singbox_tun"). Today v2rayN only shares a proxy port.

## Spike findings (2026-11-19, user's machine)
- With TUN on, `NetworkInformation.GetInternetConnectionProfile()` returns `singbox_tun`,
  and `NetworkOperatorTetheringManager.GetTetheringCapabilityFromConnectionProfile` = Enabled.
- Windows Mobile Hotspot (WinRT tethering) then routes hotspot clients through the VPN:
  user confirmed phones show the VPN exit IP, with MyPublicWiFi and its service stopped.
  Clients get 192.168.137.x.
- `StartTetheringAsync` on the singbox_tun profile threw (inner exception not captured),
  yet the hotspot ended up On. Error handling must re-check `TetheringOperationalState`.
- Classic ICS (`HNetCfg.HNetShare.EnableSharing`) fails with `0x80040201` while the hotspot
  is on, and is not needed. Do not use it.
- Not yet re-verified after a reboot with MyPublicWiFiService disabled (residual IPNAT config
  is a small remaining doubt; first manual test of the build should be done after a reboot).

## Goals
- Start/Stop a Wi-Fi hotspot from v2rayN with a user-set SSID and password (≥ 8 chars).
- Show hotspot state and connected-device count; show a Wi-Fi QR code.
- Hotspot traffic always goes through the VPN.

## Non-goals
- Linux/macOS (Avalonia `v2rayN.Desktop`) and the WebAPI host.
- Our own NAT/DHCP (MyPublicWiFi-style router mode), "WLAN repeater", captive portal,
  per-client blocking, URL logging.

## Anti-goals (would be a failure even if it "works")
- **Hotspot clients reaching the internet without the VPN.** Decided by user: when the VPN
  stops, the hotspot shuts off.
- Breaking or slowing normal TUN start/stop when the hotspot feature is unused.

## Approaches considered
1. **Windows Mobile Hotspot via WinRT tethering API — chosen.** Built into Windows, no new
   dependencies; spike shows it already shares singbox_tun when TUN is the internet profile.
2. Windows hotspot + classic ICS repointed to singbox_tun — dropped: fails with 0x80040201.
3. Own NAT/DHCP like MyPublicWiFi — dropped: much larger, unnecessary given (1).
4. `netsh wlan hostednetwork` — dropped: deprecated, unsupported by most modern drivers.

## Decided behaviour
- **Start allowed only while TUN mode is on and the core is running.** Otherwise the Start
  control is disabled with a hint ("Enable Tun first").
- **VPN stops → hotspot stops** (TUN toggled off, core stopped/crashed, app exit). No
  fallback to the physical connection.
- **Core restart / server switch** (`CoreManager.LoadCore` stops the core and removes and
  recreates singbox_tun): if the hotspot was on, stop it **before** the core stops, and
  start it again **after** the new core is up. This avoids a window where Windows rebinds
  the hotspot to Ethernet.
- **App exit:** stop the hotspot before `CoreStop()` in `AppManager.AppExitAsync`.
- Hotspot on/off is not auto-restored on next launch (my call: simplest and safest; can be
  added later as an option).

## Shape of the build
- **Settings model (ServiceLib):** new `HotspotItem { string Ssid; string Password; }` on
  `Config`, defaulting SSID to the machine name and password to a generated 12-char value.
  Saved with the rest of the config.
- **Hotspot controller (WPF project, `v2rayN/Manager/HotspotManager.cs`):** wraps
  `NetworkOperatorTetheringManager` created from the current internet connection profile;
  `ConfigureAsync(ssid, pass)` via `ConfigureAccessPointAsync`, `StartAsync`, `StopAsync`,
  `State`, `ClientCount` (and `GetTetheringClients()` if cheap). Verifies that the internet
  profile is the singbox_tun adapter before starting, and refuses otherwise.
- **Lifecycle hook:** ServiceLib cannot reference WinRT, so it needs an awaitable seam the
  WPF app plugs into (e.g. a small `IHotspotController`-style interface registered at
  startup, called by `CoreManager` before stop / after start and by `AppExitAsync`). Must
  be awaited, not fire-and-forget like `AppEvents` channels. Null on other platforms.
- **UI (WPF):** a "Hotspot" toggle/button next to "Enable Tun" in `StatusBarView` opening a
  small window: SSID, password (show/hide), Start/Stop, status line, connected count,
  Wi-Fi QR (`WIFI:T:WPA;S:<ssid>;P:<pass>;;`) using the existing QR helper.
- **Strings:** new ResUI keys in the base `.resx` (English); other languages fall back.

## Left to the builder
- Exact seam name/registration point; polling interval for client count (e.g. 3 s while the
  window is open).
- Capture and log the real inner exception from `StartTetheringAsync`; treat "threw but
  state is On" as success.
- Escaping of special characters in the Wi-Fi QR payload.
- Tests: ServiceLib side (config defaults, lifecycle ordering via a fake controller).
  The WinRT wrapper is verified manually on the user's machine.
