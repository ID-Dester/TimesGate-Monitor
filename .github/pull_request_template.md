## Summary

Describe the user-visible change and why it is needed.

## Verification

- [ ] `dotnet build TimesGateMonitor.sln -c Release --no-restore`
- [ ] `dotnet run --project tests/TimesGateMonitor.Tests -c Release --no-build`
- [ ] Real Times Gate check, or not applicable
- [ ] Fan Control compatibility check, or not applicable

## Safety

- [ ] No LocalToken, personal LAN address, username, log archive or imported media is committed
- [ ] UAC, firewall, installer and data-migration impact is documented
- [ ] Dependency/license changes are reflected in `THIRD-PARTY-NOTICES.md`
