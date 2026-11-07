# Contributing to TGm

Thanks for helping improve TGm.

## Before opening a change

- Use a focused branch and keep generated files out of commits.
- Never commit a real LocalToken, LAN address, username, log archive or imported personal media.
- Preserve the unofficial-project disclaimer and third-party attribution.
- Keep the WPF UI process non-elevated. Privileged sensor access belongs only in the narrowly scoped sensor host.

## Build and test

```powershell
dotnet restore TimesGateMonitor.sln
dotnet build TimesGateMonitor.sln -c Release --no-restore
dotnet run --project tests/TimesGateMonitor.Tests -c Release --no-build
```

Hardware changes should also be tested with Fan Control running when available. Protocol or rendering changes require a real-device check because an HTTP success response does not guarantee correct output on every Times Gate firmware.

For restart or stock-clock-face reports, use the dedicated **Times Gate restart / display reset** issue form. Record elapsed time and TGm's request counters independently; do not describe the observed ~35,000-request correlation as an established firmware threshold. See [docs/KNOWN-ISSUES.md](docs/KNOWN-ISSUES.md).

## Pull requests

Describe the user-visible change, tests performed and any UAC, firewall or device impact. Update `CHANGELOG.md` and `THIRD-PARTY-NOTICES.md` when behavior or dependencies change.
