# Known issues and open questions

Behaviour that is implemented from vendor code or open-source references but not yet seen on the
owner's hardware. Back to [AGENTS.md](../../AGENTS.md). Remove an entry once it is verified or fixed.

## Unverified on hardware

- **Razer**: nothing exchanged live; the dongle was unplugged during research. Non-elevated access
  relies on the device permissions Razer's driver package installs; without it the device shows
  "needs access".
- **Charging states**: not observed for Logitech, ROG, Betop or FreeClip; the mappings follow the
  vendor code and references.
- **Asleep/off replies**: Logitech (expected `0x8F` error 0x09/0x04 or timeout) and ROG (expected
  `FF AA` or connection type 0xFF) have not been seen with the device actually asleep.
- **Wired modes**: Logitech mouse on its cable (046D:C0A8), Azoth X on USB (0B05:1C24, report id 0),
  Betop wired pad (20BC:518C), ROG Cetra Open Wireless (0B05:1CAC).

## Open questions

- Does polling every 2 minutes delay the Azoth X's idle sleep? The interval is a setting.
- FreeClip: the SPP channel is exclusive per PC; while another app holds it, the HFP value is shown,
  which can overstate by about 5 points. Whether TLV 1 is min(L, R) or the right bud is unknown.
- Razer: which user-mode HID collection, if any, carries the dongle's battery-change notifications.
