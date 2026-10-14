# ASUS ROG (OMNI receiver, Azoth X, Cetra): battery protocol

Status: verified live on this PC (2026-10-14, verdict `confirmed-with-corrections`).

```text
SOURCES: ASUS Gear Link WebHID device pages (OMNI /view/6862 v1.00.24, Azoth X /view/7204 v1.00.36, Cetra /view/7340 v1.00.23) plus G-Helper (seerge/g-helper, AsusKeyboard.cs / AsusMouse.cs / PeripheralsProvider.cs / Azoth.cs) as an independent second source. Verified on hardware twice, independently: OMNI receiver plus Azoth X, GET-only.

== 1. DEVICE MATCHING ==
VID 0x0B05.

OMNI-class receivers (Gear Link lists [6862] and [6864,7508]):
- 0x1ACE ROG OMNI RECEIVER (verified).
- 0x1AD0 ROG SPEEDNOVA 8K RECEIVER and 0x1D54 'Asus Dongle' (Gear Link type OmniDongle; unverified; G-Helper instead treats 0x1AD0 as a Harpe II Ace mouse dongle).
- Control UP 0xFF02 U1, event UP 0xFFC2.

hidPattern '3' = NOT_DEFINED (not a bootloader marker). 0x1ACF (OMNI), 0x1AD1 (SpeedNova 8K) and 0x1C26 ('ROG AZOTH X', hasPowerInfo true; on this PC it was seen with keyboard, mouse and 2 vendor interfaces) are alternate modes of unknown purpose:
- use the UP FF00/FF01/FF02 U1 control collection if present, with the same GET-only commands;
- otherwise skip.

Directly attached devices (USB cable or own dongle) are listed in gearlink_support_devices.json (76 PIDs).
- Control UPs: keyboard 0xFF00, mouse 0xFF01, gamepad 0xFF03, dock 0xFF04.
- Event UPs: 0xFFC0, 0xFFC1, 0xFFC3, 0xFFC4.
- Usage is always 1.

Azoth X PIDs: 0x1C24 USB, 0x1C25 RF (the PID the OMNI slot reports; also a Gear Link RF_MODE page), 0x1C26 (hidPattern 3, role unknown), 0x1C27 Bluetooth (M704_BLUETOOTH=7207).

== 2. INTERFACE SELECTION (Windows) ==
Enumerate with SetupDi (HID GUID).
- Read HidD_GetAttributes and HidP_GetCaps. A zero-access CreateFile is enough for caps.
- Select by UsagePage/Usage, never by the COLxx number.
- Report ID = HidP_GetValueCaps(HidP_Output)[0].ReportID. It CAN be 0 (G-Helper reaches the wired Azoth X 0x1C24 on MI_01 with report 0x00 and 65-byte packets; unverified here).

On this OMNI (verified twice):
- MI_02&COL01 UP FF02 rid 1, in/out 64: dongle control.
- MI_02&COL02 UP FF00 rid 2, 64: keyboard-slot control.
- MI_02&COL03 UP FF01 rid 3, 64: mouse-slot control.
- MI_03&COL03 UP FFC2, in rid 3, 21 bytes: dongle events.
- MI_03&COL04 UP FFC0, in rid 4, 21 bytes: keyboard events.
- MI_03&COL05 UP FFC1, in rid 5, 21 bytes: mouse events.
- Ignore MI_00 (keyboards), MI_01 (mouse; it cannot be opened) and MI_03&COL01/02 (consumer/system).

Open the control collection with CreateFile(GENERIC_READ|GENERIC_WRITE, FILE_SHARE_READ|FILE_SHARE_WRITE, OPEN_EXISTING, FILE_FLAG_OVERLAPPED). A device behind the OMNI is reached through the collection whose output report ID equals the slot's controlReportId.

== 3. FRAMING ==
Output buffer = exactly OutputReportByteLength bytes (64 on the OMNI; 65 = 1+64 for a rid-0 collection):
- [0] = reportId, [1] = cmd, [2] = sub, [3] = idx lo, [4] = idx hi
- [5..20] = 16-byte payload (zeros for every GET)
- rest zero.
SEND WITH WriteFile ONLY. HidD_SetOutputReport fails with ERROR_GEN_FAILURE (31) on every OMNI control collection (verified).

Response = an input report on the SAME handle: [0] = reportId, then d[0..] (d[n] = report[n+1]).
- MATCH: reportId equal AND d[0..3] == cmd, sub, idxLo, idxHi. Exception: the A0 00 scan matches on d[0..1] only.
- REJECT: d[0]==0xFF && d[1]==0xAA (rest zero). Means unsupported command, empty slot, or an unreachable/sleeping device (libratbag ASUS_STATUS_ERROR 0xAAFF).
- Discard other reports, and discard a duplicate of the same header arriving within 100 ms (Gear Link does this).
- Timeout 500-1000 ms, 1 retry. Observed 1.9-5.0 ms.
- Serialize: one command in flight per collection.
- Call HidD_FlushQueue before each write when using open-transact-close, or run one persistent reader per collection.

== 4. RECEIVER COMMANDS, rid 1 on UP FF02 ==
- A1 01 00 00 getFirmwareVersion: n = u32 LE at d[4..7]. Version = '%x.%02x.%02x' of bytes (n>>16, n>>8, n) & FF. Observed 7.00.08.
- A1 00 00 00 getSerialNumber: d[4..15] ASCII, NULs stripped. Observed T4MPKRD01THC.
- A0 00 00 00 scanForPairedDevices: count = d[2] | d[3]<<8. For i < count, e = 4 + 4*i:
  - PID = d[e] | d[e+1]<<8
  - controlReportId = d[e+2] (2 = keyboard, 3 = mouse)
  - notifyReportId = d[e+3] (4 = keyboard events, 5 = mouse events)
  - Type comes from the PID table, else from controlReportId.
  - Observed: 01 00 | 25 1C 02 04.
- Dongle event, input rid 3 on UP FFC2: data C0 00 xx pidLo pidHi (length >= 6) means pairing changed. Rescan.
- FORBIDDEN (writes): A2 00 (pairing mode), A2 01 (unpair), B0 00 (dongle reset).

== 5. KEYBOARD (OMNI rid 2, or direct UP FF00 with that collection's rid) ==
0x12 = GET; 0x50/0x51 = SET (never send).

a) 12 03 00 00 getConnectionType: d[4] = 0 wired, 1 RF 2.4G, 2 BT, 0xFF not present/unusable.
   - Alive = valid reply && d[4] != 0xFF. A reject or timeout also means offline.
   - Seen live 2026-10-14 14:25 with the Azoth X asleep: the A0 00 scan still lists the slot (`01 A0 00 01 00 25 1C 02 04`) and `12 03` answers `02 12 03 00 00 FF`, so the keyboard is reported asleep without sending `12 01`.
   - Its latency matches dongle-local commands, so through the OMNI it probably reflects the dongle's link state.

b) 12 01 00 00 getPowerInfo, needs >= 10 data bytes:
   - d[5] = battery percent 0-100 (Gear Link s[5] = G-Helper response[6]).
   - d[6] = idle enum (0=1 min, 1=2, 2=3, 3=5, 4=10, 255=never).
   - d[7] = power saving (0 off, 1 LED off, 2 dim).
   - d[8] = charging (1 = charging; Gear Link s[8] = G-Helper response[9]).
   - d[9] = low-power threshold % (Gear Link only; G-Helper doubts it).
   - Undocumented: d[4] = 0; d[10] = percent again; d[11..12] = u16 LE that follows battery millivolts (3770-3798 at 45 %; it changes poll to poll, which shows the reply is live).
   - Use d[5] for the percentage. Values > 100 should be treated as unknown (defensive).

c) Optional 12 00 00 00 device info:
   - d[4..7] = FW u32 LE (the dongle FW when wireless).
   - d[8] = profiles, d[9] = effect, d[10] = current profile.
   - d[11..14] = keyboard FW when wireless.

d) Events, input rid 4 on UP FFC0 (or the device's own FFC0):
   - 03 81 03 00 [s]: s != 0 means awake, 0 means asleep/disconnected.
   - 03 81 05 00: plug status changed. Re-poll.
   - '72 00 ...' is NOT a reliable plug signal: an all-zero one was seen during plain typing. Use it at most as a debounced re-poll hint.
   - Everything else is live key-state data (113 packets in 12 min here). Do not open this collection, or drop those packets without logging.
   - Battery is NOT pushed. Gear Link's '12:00'/'12:01' handlers on this path are no-ops. Gear Link polls power every 60 s with a 5 s cache.

e) 12 00 0E 00 (wireless signal) returns FF AA on Azoth X. 12 07 on keyboards is getWindowsLightingSupported (still a GET).

== 6. MOUSE (OMNI rid 3, or direct UP FF01) ==
a) 12 00 02 00 getMouseExistInfo (wireless only; timeout 1000 ms): d[4]==1 means alive, 0 means asleep or absent.

b) 12 07 00 00 getPowerInfo, needs >= 11 data bytes:
   - d[4] = battery %.
   - d[5] = sleep enum.
   - d[6] = low-power threshold.
   - d[7..8] = u16 LE voltage.
   - d[9] = 1 when charging (G-Helper: > 0).
   - d[10] = battery type, d[11] = full-charge effect.
   - Bundle 1.00.36 (needs >= 14): d[12] = wake-on-motion, d[13] = charging lighting.
   - A reply with d[4]==0 && d[9]==0 means asleep/no data (G-Helper), NOT 0 %.

c) 12 00 00 00:
   - d[4..7] = FW (dongle FW when wireless).
   - d[8] = profiles, d[9] = effects, d[10] = current profile minus 1.
   - d[11] = DPI index, d[12..13] = DPI value.
   - d[14..17] = mouse FW when wireless.
   - d[18] = DPI level count.

d) Events, input rid 5 on UP FFC1: 12 08 02 00 [1/0] awake/asleep. Ignore 12 01 (button state).

e) Empty slot (verified twice): 12 00 02 returns 03 12 00 02 00 00; 12 07 returns 03 FF AA.

f) Older own-dongle ROG mice not in the Gear Link table (Keris Wireless 0x1960, Gladius III Wireless 0x197F, ...): G-Helper and OpenRGB use report ID 0x00, 65-byte packets, and 12 07 with battery = packet[5] and charging = packet[10] (i.e. d[4], d[9]). Unverified here.

== 7. ALGORITHM (every 60 s and on alive/plug events) ==
Per OMNI:
1. A0 00 scan.
2. Per slot:
   - keyboard: 12 03, then 12 01 if alive.
   - mouse: 12 00 02, then 12 07 if alive (also apply the 0/0 asleep rule).

Per direct PID: the same on its FF00/FF01 collection with its own rid (possibly 0). USB-wired devices skip the alive check.

Result: percent (-1 = unknown), charging, online/asleep, lastSeen.
- Asleep or offline (0xFF, exist==0, FF AA, timeout, mouse 0/0): keep the last value marked stale.
- Dedupe: the same model wired (e.g. 0x1C24) and via OMNI means prefer the wired entry, but take charging from its own d[8]. Match by USB serial when possible: the wired 0x1C24 here had serial T4MPKRD01THC = the OMNI serial = the A1 00 reply.
- Unverified: whether 60 s polling delays the keyboard's idle sleep. Consider polling less often (e.g. 5 min) and re-polling on 03 81 03 00 awake events if FFC0 is used.

== 8. ROG CETRA OPEN WIRELESS 0B05:1CAC (bundle 7340; table re-verified; no hardware) ==
Transport:
- Collection UP 0xFF00 U1 (hidPattern 2 = CONTROL_ONLY), report ID 0xCC.
- Request: [CC][12][key][00][00] zero-padded to the output length.
- Response: [CC][12][key][00][00][values...], values at d[4..].
- Error: FF AA (12 key).
- Notifications: [CC][22][key][x][x] (only enabled via a SET; not needed).

Keys:
- 0x12 batteryLevel: d[4] = left %, d[5] = right %, d[6] = case %. A value > 100 means unknown.
- 0x11 chargingStatus: d[4] 1 = left, 2 = right, 3 = both; d[5]==1 means case charging.
- 0x01 connectionStatus: d[4]==3 means both earbuds connected.
- 0x13 powerSavingStatus: d[4]==0 means awake.
- 0x00 deviceInfo, 0x07 protocolVersion.
- Never send 0x51.

== 9. COEXISTENCE / SAFETY ==
No ASUS software runs here. With Gear Link or Armoury Crate active, every open handle sees every input report: keep header matching, ignore duplicates within 100 ms, retry once, never open exclusively.

Allowlist exact tuples only: A1 01, A1 00, A0 00 (idx 0); 12 03, 12 01, 12 00 (idx 0 or 2), 12 07 (idx 0); Cetra [CC] 12 {00,01,07,11,12,13}.
```

## Remaining risks

- Asleep/off behaviour is still unobserved on hardware. During my 12-minute monitor the user typed intermittently (113 key-state packets; longest idle gap about 3 min, below the 5-min idle setting). No 03 81 03 00 event occurred, and no 0xFF, FF AA or timeout was seen. It is still unknown whether a sleeping Azoth X yields 12 03 = 0xFF, a reject or a timeout, or whether the A0 scan still lists it.
- 12 03 latency (1.9-2.9 ms) equals dongle-local commands, while 12 01/12 00 take 3.9-5.0 ms. This suggests the OMNI answers 12 03 from its own link state. If so, liveness should also rely on 12 01 succeeding (no FF AA or timeout).
- Possible battery drain: it is not known whether polling 12 01 over RF every 30-60 s resets the keyboard's idle-sleep timer. Gear Link only polls while its page is open.
- Charging flag d[8] was 0 throughout. Charging=1 was never observed, because the keyboard was not cabled and I could not ask the user to plug it in. The flag rests on two code sources (Gear Link s[8], G-Helper response[9]) only.
- Absolute battery accuracy was not compared with the physical device (no OLED/Armoury reading available). Only 45 % was seen. The d[11..12] millivolt field (3770-3798) is plausible for about 45 % Li-ion but undocumented.
- The wired 0x1C24 path (MI_01, report ID 0, 65-byte buffer per G-Helper), 0x1C26, 0x1C27 BT, 0x1AD0/0x1D54 receivers, the mouse-through-OMNI path and the Cetra 0x1CAC protocol are all unverified on hardware. Gear Link and G-Helper disagree on what 0x1AD0 is (an OMNI-type receiver vs a Harpe II Ace mouse dongle).
- The keyboard FFC0 event collection streams live key-state data even with no vendor software running. Opening it is a privacy hazard. My monitor counted those packets without storing them. It logged only one all-zero 72 00 packet.
- The protocol comes from a remotely hosted, versioned web app (Gear Link 1.00.24/1.00.36/1.00.23). Future bundles or firmware may change layouts; re-check /view/<pid>/manifest.json if parsing breaks.
- Dedupe by USB serial rests on one PnP history observation: the 0x1C24 and 0x1ACE serials are both T4MPKRD01THC. Other bundles or receivers may differ.
- No prompt-injection content was found in the Gear Link bundles, the G-Helper/libratbag sources or the device responses. All of it was treated as data.

## Sources

- https://gearlink.asus.com/ (index, assets/devices-DTVeArx5.js: VID 2821, control UPs 0xFF00-0xFF04, event UPs 0xFFC0-0xFFC4, OMNI PIDs [6862], [6864,7508])
- https://gearlink.asus.com/view/6862/manifest.json (ROG OMNI RECEIVER page v1.00.24, bundle 6862-1784796679-1b7a55)
- https://gearlink.asus.com/view/6862/main-1.00.24-6862-1784796679-1b7a55.js and chunk-1.00.24-6862-1784796679-1b7a55-DcJLazzH.js (dongle, keyboard, mouse protocol classes, PID table, response matcher)
- https://gearlink.asus.com/view/7204/manifest.json and main-1.00.36-7204-1788853886-139979.js (ROG AZOTH X USB page, newer mouse power fields)
- https://gearlink.asus.com/view/7205/manifest.json, https://gearlink.asus.com/view/7206/manifest.json (Azoth X RF_MODE / hidPattern 3)
- https://gearlink.asus.com/view/7340/manifest.json and main-1.00.23-1774496770.js + bundle-CS4SCXaq.js (ROG CETRA OPEN WIRELESS protocol table, report 0xCC)
- https://gearlink.asus.com/view/6863/manifest.json (OMNI PID 0x1ACF hidPattern 3)
- https://gearlink.asus.com/external-configs/hid-whitelist.json
- https://raw.githubusercontent.com/libratbag/libratbag/master/src/asus.c (OMNI transport = report ID 0x03, 0x1212/0x02 signature query, ASUS_STATUS_ERROR 0xAAFF)
- https://raw.githubusercontent.com/libratbag/libratbag/master/src/asus.h
- https://raw.githubusercontent.com/libratbag/libratbag/master/src/driver-asus.c
- https://gitlab.com/CalcProgrammer1/OpenRGB/-/raw/master/Controllers/AsusAuraUSBController/AsusAuraMouseController/AsusAuraMouseController.cpp (legacy ROG mouse framing: report 0x00, 0x12 GET family)
- https://gitlab.com/CalcProgrammer1/OpenRGB/-/raw/master/Controllers/AsusAuraUSBController/AsusAuraMouseController/AsusAuraMouseDevices.h (legacy ROG wireless mouse PIDs)
