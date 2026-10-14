# SteelSeries Arctis Nova 5 / 5X: battery protocol

Status: taken from HeadsetControl (`lib/devices/steelseries_arctis_nova_5.hpp` and
`protocols/steelseries_protocol.hpp`), then checked live on this PC (see section 5).

## 1. Device matching

- The wireless base station (2.4 GHz USB dongle) is the HID device; the headset itself is never
  enumerated. VID 0x1038, PID 0x2232 (Arctis Nova 5) or 0x2253 (Arctis Nova 5X).
- The base station exposes three HID collections. Only one carries the protocol:

  | Interface | Caps | Use |
  |---|---|---|
  | MI_03 | UP 0xFFC0, U 0x0001, In 65, Out 65, Feature 515 | **status queries** (this one) |
  | MI_04 | UP 0x000C, U 0x0001, In 5 | consumer keys (volume); ignore |
  | MI_05 | UP 0xFF00, U 0x0001, In 65, Out 65 | ignore |

- Select by caps (UP 0xFFC0, U 0x0001, 65-byte input and output reports), never by COLxx index.
- Open with GENERIC_READ | GENERIC_WRITE and FILE_SHARE_READ | FILE_SHARE_WRITE; SteelSeries GG may
  hold the same collection at the same time, so replies are matched strictly and other frames skipped.

## 2. Request

The status query: a 65-byte output report, report id 0, command `0xB0`, all other bytes zero.

```text
00 B0 00 00 ... 00          (65 bytes)
```

HeadsetControl sends `[00 B0]` through hidapi, which pads to the output report length, so the wire
bytes are identical. It is the only command this app sends.

## 3. Reply

A 65-byte input report. Offsets below are as Windows ReadFile returns them, report id at byte 0
(HeadsetControl's offsets are one less, because hidapi strips report id 0):

| Byte | Meaning |
|---|---|
| 0 | report id `00` |
| 1 | `B0`, echo of the command |
| 2 | link: `02` = headset off or out of range; anything else = connected |
| 3 | unknown; `02` seen live |
| 4 | battery percent, 0 to 100 |
| 5 | `01` = charging (HeadsetControl); `03` seen live on battery; anything else = not charging |
| 6, 7 | ChatMix game / chat volume (not used) |

When byte 2 is `02` the percent is stale and must not be shown.

## 4. Never send

Every other command in this collection changes a setting, and most are followed by a save to the
headset's flash. None may be sent:

| Bytes | Effect |
|---|---|
| `00 09`, `00 35 01` | save settings |
| `00 39 xx` | sidetone level |
| `00 37 xx` | microphone volume |
| `00 AE xx` | mic-mute LED brightness |
| `00 A3 xx` | inactive-time auto power-off |
| `00 27 xx` | volume limiter |
| `00 33 ...` | equalizer bands |

## 5. Live check

2026-10-14, Arctis Nova 5X base station on this PC, headset on and on battery, captured with
`batteryinfo list --vendor SteelSeries --trace` (reply 5 ms after the request):

```text
>> 00 B0 00 00 ... 00
<< 00 B0 03 02 2D 03 64 64 00 ... 00      connected, 45 %, not charging, ChatMix 100/100
```

Not yet seen live: the headset switched off (byte 2 = `02`) and charging (byte 5 = `01`).
