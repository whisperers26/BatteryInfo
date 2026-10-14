# Device battery protocols

Reverse-engineered and hardware-checked on 2026-10-14. Each file is the implementation
reference for one vendor provider in `src/BatteryInfo.Core/Vendors/`.

| Vendor | File | Live-verified |
|---|---|---|
| Logitech HID++ 2.0 | [logitech.md](logitech.md) | yes (PRO X2 SUPERSTRIKE, 56 %) |
| ASUS ROG | [rog.md](rog.md) | yes (Azoth X via OMNI, 45 %) |
| HUAWEI FreeClip 2 | [huawei.md](huawei.md) | yes (L 77 / R 77 / case 100) |
| Betop / Beitong | [betop.md](betop.md) | yes (KP70C2, 91 %) |
| Razer | [razer.md](razer.md) | no (dongle unplugged) |
| SteelSeries Arctis Nova 5 / 5X | [steelseries.md](steelseries.md) | yes (Nova 5X, 45 %) |

Every provider sends only the read/query commands these files allow-list. Several neighbouring
commands write settings or reboot a device into firmware-update mode; never add one.
