# Changelog

All notable changes to TGm will be documented in this file. The format is based on Keep a Changelog and version numbers follow Semantic Versioning.

## [Unreleased]

### Added

- Public-release documentation, licensing and repository hygiene.
- Reproducible Windows release packaging and continuous integration.
- Sanitized diagnostic export for issue reports.
- Sanitized dashboard screenshot for the public README.
- Per-LCD heartbeats and selective recovery when one display returns to a Divoom clock face.
- Persistent five-minute heartbeat journals with probable reset and recovery timestamps.
- A 5–30 second sensor refresh slider in application settings.
- A watchdog now logs and restarts the Times Gate connection monitor after an unexpected background failure.
- Runtime recovery now asks Task Scheduler to restart an elevated CPU sensor host after three consecutive IPC failures and retries every 15 seconds until it returns.
- Hardware polling exceptions no longer permanently stop live preview and network values; TGm keeps the last snapshot and continues polling.

### Changed

- Healthy displays no longer receive a device health command every 15 seconds.
- Manual `APPLY SCREEN` now always reapplies the selected LCD.
- The application settings label in the title bar is now the same gear icon used by widget settings.
- The release packager can reuse an already restored repository-local NuGet cache for offline verification.
- The PC LAN address is detected again on every launch and prefers the subnet used by Times Gate.
- Apply All remains available after a successful apply and can forcibly resend all five displays.
- Network-text JSON now uses a fixed `Content-Length` instead of chunked transfer encoding for firmware compatibility.
- Load values refresh every 5 seconds, thermals, power and RAM every 10 seconds, while system values remain at a safer 15-second interval.
- A simultaneous loss of all five LCD heartbeats is restored as one full-device reset instead of five delayed single-screen recoveries.
- Heartbeat diagnostics continue while automatic reconnection is disabled; that setting now controls recovery only.
- Full-device reset detection tolerates the small timing skew between LCD requests and restores all five screens together before selective recovery begins.
- The refresh slider acts as a minimum hardware interval: the 5-second setting preserves the tuned 5/10/15-second defaults, while higher settings slow every sensor field to the selected floor.
- Each displayed row remains an individual network-text field because Times Gate firmware does not render line breaks inside one field.
- Automatic recovery now keeps the last known screen configuration until a forced reapply succeeds, so a timeout on LCD5 cannot remove it from heartbeat monitoring.
- The unproven 30,000-request preventive reapply was removed after endurance logs showed that it did not prevent a later firmware reboot and could itself leave a partially applied dashboard.
- The CPU sensor setup version was advanced so RC10 offers a one-time repair for scheduled tasks that still point to an older TGm build directory.

### Known issues

- During some long Network Text sessions, Times Gate may restart its UI/device process or return to a stock clock face after roughly 35,000 requests observed by TGm's local server. The correlation is repeatable enough to disclose but is not a proven firmware threshold or root cause. Automatic dashboard recovery is best effort and does not prevent the device-side event.

## [0.1.0] - Unreleased

### Added

- Five-screen dashboard editor and live preview.
- CPU, GPU, RAM, system, clock and custom widget support.
- Network-text updates that avoid repeated image uploads.
- Tray lifecycle, autostart and automatic reconnection.
- Optional one-time-UAC CPU sensor host with watchdog recovery.

### Tested

- Eight-hour Times Gate endurance run with Fan Control active.
