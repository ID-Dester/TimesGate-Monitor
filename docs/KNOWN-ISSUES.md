# Known issues

TGm is preview software. This page documents limitations that materially affect normal use and should be read before installing a preview build.

## Times Gate may restart or return to a stock clock face during long sessions

### User-visible behavior

During some long sessions with five Network Text displays, Times Gate has:

- shown damaged, shifted or incomplete text;
- displayed `err at jsondecode!` on one LCD;
- stopped requesting TGm endpoints from one or all LCDs;
- temporarily stopped responding to local health checks;
- returned one or all LCDs to a stock Divoom clock face and later accepted the TGm layout again.

Several long runs completed normally, so the failure is intermittent.

### What is known

- Multiple incidents occurred near **34,600–35,100 requests counted by TGm's local HTTP server** across the active displays.
- A previous experiment that reapplied all widgets after 30,000 requests did **not** prevent a later failure and was removed.
- Using static backgrounds plus Network Text is substantially more stable than regenerating and uploading an image for every sensor change.
- TGm can detect prolonged LCD silence and attempt a full or selective reapply. Recovery normally takes at least the silence-detection window plus the sequential apply time.
- A `200 OK` response only confirms that the local endpoint answered. It does not confirm that Times Gate parsed or rendered the response.

### What is not known

- The server-observed request count is not an internal device counter and does not establish an exact firmware limit.
- It has not been proven whether the physical device reboots, its UI/network process restarts, or only the widget state resets.
- The root cause has not been isolated. Current hypotheses include firmware resource accumulation, JSON/parser state, socket handling, interrupted response bodies, aggregate request rate, repeated widget configuration, power delivery and network instability.

### Current mitigation

TGm uses fixed `Content-Length` responses, separate fields for each row, staggered refresh intervals, per-LCD heartbeat journals, full-device reset detection, selective recovery and an always-available **Apply all** action. These measures improve recovery and diagnostics; they do not prevent the underlying event.

If the stock clock face remains visible, keep TGm running until automatic recovery completes or use **Apply all**. If the device is unreachable, restore it with the official Divoom app or power-cycle it as a last resort.

### How to report a useful incident

Use the **Times Gate restart / display reset** issue form. Include:

- TGm version and Times Gate firmware version;
- refresh-slider setting and approximate session duration;
- whether one LCD or all five were affected;
- whether corrupted text or `err at jsondecode!` appeared first;
- whether Times Gate stopped responding on the LAN;
- VPN, firewall, Wi-Fi, power adapter and cable details;
- the diagnostics ZIP exported after the event.

Never publish your LocalToken. Inspect the diagnostics archive before attaching it.

The detailed Russian investigation handoff is available in [TIMES-GATE-REBOOT-INVESTIGATION-RU.txt](TIMES-GATE-REBOOT-INVESTIGATION-RU.txt).
