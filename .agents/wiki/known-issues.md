# Known issues and open questions

Behaviour that is implemented from vendor code or open-source references but not yet seen on the
owner's hardware. Back to [AGENTS.md](../../AGENTS.md). Remove an entry once it is verified or fixed.

## Unverified on hardware

- **Razer**: nothing exchanged live; the dongle was unplugged during research. Non-elevated access
  relies on the device permissions Razer's driver package installs; without it the device shows
  "needs access".
- **Charging states**: not observed for Logitech, ROG, Betop or FreeClip; the mappings follow the
  vendor code and references.
- **Asleep/off replies**: seen live for Logitech (`0x8F` error 0x08 on a paired slot) and the ROG
  Azoth X (connection type 0xFF); the Logitech 0x09/0x04 codes other receivers may use are not.
- **ROG beyond the OMNI keyboard**: mice behind the OMNI, the wired Azoth X (report id 0), older
  own-dongle mice and the Cetra Open Wireless are tested with fakes only.
- **Logitech online read through the provider**: the PRO X2 SUPERSTRIKE was asleep during every
  provider run, so feature resolution, the name read and the 0x1004 decode have only run against the
  fake receiver (the same frames were read live during the protocol research).
- **Wired modes**: Logitech mouse on its cable (046D:C0A8), Azoth X on USB (0B05:1C24, report id 0),
  Betop wired pad (20BC:518C), ROG Cetra Open Wireless (0B05:1CAC).
- **SteelSeries**: the Nova 5X headset switched off (link byte `02`) and charging (byte 5 `01`) come
  from HeadsetControl only; the Arctis Nova 5 (1038:2232) is untested.
- **Betop**: charging (byte 4 low nibble 1) comes from BetopGame's UI code only; sibling models other
  than the KP70C2 are untested.
- **Apple**: only the owner's AirPods 4 (ANC) are tested, in their case on its charger and worn with music, for
  10 minutes. Not yet seen:
  - an address change while worn, and the pods taking turns broadcasting;
  - a connect while the app runs, which the "new at connect" rule depends on;
  - a same-model pair nearby;
  - any other model, and the one-battery layout (AirPods Max, Beats), which rests on published captures.

- **Tray app**: a physical click on the Windows 11 tray icon (tests sent the icon's callback message),
  refresh after a real sleep/resume, a taskbar theme change while running, and taskbars on the left,
  top or right (placement is unit-tested only).
- **Tray app beside an elevated copy**: while a copy runs as administrator, starting the app normally
  exits without showing that copy's flyout, because the elevated copy's objects refuse to be signalled
  by a non-elevated launch. Tested on objects given an elevated token's access rules, not on a real
  elevated copy.
- **Sandbox warning**: the final-path check was run by hand on a sandboxed install (it resolves into the
  Claude package's LocalCache) and is unit-tested; the toast itself has not been seen yet.
- **Low-battery toasts**: sent under the Start menu shortcut's app id (`velopack.BatteryInfoApp`), falling back
  to a tray balloon (which Windows deletes from Notification Center once its banner closes) without one. A
  hand-sent toast under that id stayed in Notification Center; one raised by the app itself has not been seen yet.
- **Updating with publish.ps1**: the running tray app closing on its exit event and removing its icon.
  The event is unit-tested and the script was run against a stand-in process, not the real tray app.

## Open questions

- Does polling every 2 minutes delay the Azoth X's idle sleep? The interval is a setting.
- FreeClip: the SPP channel is exclusive per PC; while another app holds it, the HFP value is shown,
  which can overstate by about 5 points. Whether TLV 1 is min(L, R) or the right bud is unknown.
- FreeClip low-battery alert: once an earbud has alerted over SPP, the HFP value does not alert until
  SPP shows that bud charging or above the alert level + 5. If the buds are recharged in the case
  unseen and another app holds SPP for the whole next discharge, that discharge gets no alert. HFP
  cannot re-arm the bud's alert while it is unknown which bud the HFP value follows.
- FreeClip charging flags (TLV 3) have never been non-zero live, and only the FreeClip 2 is tested;
  other FreeBuds models may use another RFCOMM channel.
- Razer: which user-mode HID collection, if any, carries the dongle's battery-change notifications.
- AirPods 4: AndroPods reports that firmware 8B39 sends no Proximity Pairing frames at all. The owner's pair
  does (firmware unknown). A pair that sends none stays at "Waiting for its Bluetooth battery broadcast", unless
  a pair of the same model is in range at -80 dBm or better: its levels are then shown.
- AirPods: whether the broadcast levels keep updating over hours with a non-Apple host. The owner's pair went from
  90 to 80 while worn for 9 minutes; the MagicPods author says status stops updating on Windows.
- AirPods: a same-model pair nearby can be followed instead of the owner's when it appeared at the connect, has
  the same levels, or (with neither) is the strongest at -80 dBm or better, which is the only rule when the app
  starts with the AirPods already connected. Nothing short of Apple's identity key (AAP only) can rule that out.
  Addresses heard when the scan starts never count as new at the connect, but after 10 minutes without a frame
  of that model (for example after sleep) every address in range is new again. The `07 0F 00` frame and
  the `07 19 07` frame from the identity address at connect may carry a usable link; their layouts are unknown.
- AirPods: whether Windows ever gets a level over hands-free (`AT+IPHONEACCEV`). It had none for the owner's
  connected AirPods 4; if it ever does, it is shown while no broadcast is fresh.
