# Public release checklist

This checklist separates automated release preparation from the actions that require the repository owner or real hardware.

## Automated gates

- [x] Release configuration builds with warnings treated as errors.
- [x] All unit-test harness groups pass.
- [x] NuGet lock files are committed and CI restores in locked mode.
- [x] Generated artifacts, local package caches, logs and media are ignored.
- [x] Known LocalToken and personal LAN values are absent from commit candidates.
- [x] MIT project license and third-party notices are present.
- [x] Portable package includes TGm, sensor host, removal script and SHA-256.
- [x] Diagnostic export removes tokens, IP suffixes, profile names and media paths.
- [x] CI and tag-driven prerelease workflows are defined.
- [x] The intermittent long-session restart/reset is disclosed in both README files, packaged known-issues documentation and release notes.
- [x] A dedicated device-reset issue form records duration, request-count evidence, firmware and environment without treating the observed correlation as a proven threshold.

## Owner decisions before publishing

- [ ] Choose the final GitHub owner/repository name and add it to project metadata.
- [ ] Enable GitHub private vulnerability reporting.
- [x] Add screenshots that contain no third-party artwork, usernames, tokens or personal LAN addresses.
- [ ] Review the public author/copyright wording.
- [ ] Decide whether the first release is `0.1.0-preview.1` or another version.
- [ ] Replace the placeholder repository URL in GitHub's **About** panel after creating the public repository.

## Release-candidate validation

- [ ] Install on a clean Windows 11 x64 user account.
- [ ] Test Windows 10 x64 if it remains a supported target.
- [ ] Verify installer upgrade over the previous preview.
- [ ] Verify uninstall removes autostart, Credential Manager token and sensor task while preserving layouts by default.
- [ ] Verify optional `-RemoveUserData` removes only TGm-owned profile folders.
- [ ] Confirm one normal launch and one reboot/start-with-Windows cycle without repeated UAC.
- [ ] Confirm CPU sensors recover after toggling access and after a sensor-host watchdog restart.
- [ ] Run a real Times Gate smoke test with Fan Control active and a VPN active.
- [x] Confirm selective recovery after one physical LCD is switched back to the stock Divoom face.
- [ ] Inspect an exported diagnostics ZIP manually before attaching it to an issue.
- [ ] Run at least one long-session test at the intended default refresh interval and record the result as evidence, not as proof that the known restart issue is fixed.

## Distribution

- [ ] Consider Authenticode signing. Unsigned previews should be labeled clearly and accompanied by SHA-256.
- [ ] Publish only through the chosen GitHub repository.
- [ ] Keep the unofficial-project disclaimer visible in README and application settings.
- [ ] Do not upload a build until the repository owner explicitly approves publication.
- [ ] Follow `docs/GITHUB-PUBLISHING-GUIDE-RU.md`; do not create a tag until the initial public source commit and CI run are clean.
