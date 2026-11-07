# TGm 0.1.0-preview.1

First public preview of the unofficial TGm monitor for Divoom Times Gate.

## Highlights

- Five independently configurable Times Gate displays.
- Live CPU, GPU, RAM and system values delivered through Network Text.
- GPU, CPU, CORE, RAM, SYSTEM, SMALL_CLOCK and CUSTOM_WIDGET presets.
- PNG, JPG, BMP and animated GIF imports.
- Per-widget colors, header symbols and value rows.
- Optional elevated CPU sensor helper while the main UI remains non-elevated.
- Tray lifecycle, autostart, pause, reconnect, per-LCD recovery and Apply all.
- LocalToken stored in Windows Credential Manager.

## Important preview limitation

During some long Network Text sessions, Times Gate has returned to a stock clock face after roughly 35,000 requests recorded by TGm's local server. The count is a correlation, not a proven firmware threshold. The exact root cause remains unknown. TGm attempts automatic recovery but cannot prevent the device-side event. Read [Known issues](KNOWN-ISSUES.md) before installing.

## Packages

- `TGm-0.1.0-preview.1-win-x64.zip`: self-contained portable build for Windows 10/11 x64.
- `TGm-0.1.0-preview.1-win-x64-setup.exe`: per-user installer produced by the release workflow.
- `.sha256`: checksum for verifying the unsigned preview package.

Preview builds are unsigned. Windows SmartScreen may show an unknown-publisher warning.

## Minimum setup

Run `TGm.exe` as a normal user, enter the Times Gate IPv4 address and LocalToken, choose widgets, then use **Apply all**. Extended CPU sensors are optional and can request a one-time UAC approval from the gear menu.
