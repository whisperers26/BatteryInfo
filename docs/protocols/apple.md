# Apple AirPods and Beats: battery from the Proximity Pairing broadcast

Status: taken from primary sources, then checked live on 2026-10-15 with the owner's AirPods 4 (ANC) on this PC:
in the case on its charger, taken out, and worn with music playing (section 12). Sources:
- Apple's own code and logs: iOS 26.1 Sharing.framework `SFBLEScanner.mm` and AudioAccessoryServices,
  decompiled (EthanArbuckle/iPhone18-3_26.1_23B85_Restore @ `90aa0cfe`), and iOS 26.5 unified logs that
  print a received frame next to Apple's decode of it (mennovanveenendaal.github.io @ `b11a8f65`, parts 2,
  3 and 5 of "AirPods and the Apple Unified Log").
- Shipping decoders: d4rken-org/capod @ `bca9a076`, SpriteOvO/AirPodsDesktop v0.6.1 @ `56b04001` (a Windows
  app), adolfintel/OpenPods @ `9cd80087`, librepods-org/librepods @ `4589176f`.
- Captures with an independent ground truth: AirPods Pro values confirmed over Apple's accessory protocol
  (f-fritz/ha-airpods-bridge), an AirPods Max USB-C captured on Windows next to the Mac's own reading
  (NRanjan-17/Orbit), and GitHub issue logs of the apps above.
- Papers: Celosia & Cunche, "Discontinued Privacy", PETS 2020; Martin et al., "Handoff All Your Privacy",
  PETS 2019.

Those four decoders were compiled from unmodified upstream source and run over 148 published frames. Their
left, right, case and charging values matched the decode below in all 2780 comparisons. Every difference was
a frame they accept and this decode rejects, or a headset they show as two earbuds.

## 1. Listen only

AirPods and Beats broadcast their battery levels to every Apple device nearby, in a BLE advertisement
called Proximity Pairing. The app only listens for it:

- A WinRT `BluetoothLEAdvertisementWatcher` with `ScanningMode = Passive`, set explicitly and checked before
  every `Start()` (`PassiveAdvertisementListener`). A passive scan never transmits. An active scan would send a
  scan request to each scannable advertiser: AirPods frames arrive as `ScannableUndirected` (seen live from the
  owner's AirPods 4, and from an AirPods Max USB-C in Orbit's captures).
- No connection of any kind: no GATT, no `BluetoothLEDevice`, no RFCOMM, and no Apple Accessory Protocol
  (AAP, L2CAP PSM 0x1001). AAP gives exact values but needs a handshake and writes, and Windows has no
  user-mode L2CAP API for classic Bluetooth anyway.
- The pairing record and the connected flag come from what Windows already holds (Win32
  `BluetoothFindFirstDevice`/`BluetoothGetDeviceInfo` with no inquiry, and the BTHENUM instance ids), as for
  HUAWEI.

So nothing is ever sent to the AirPods, and there is no command allow-list.

## 2. Device matching

- A paired classic device whose Class of Device has the audio/video major class (`(cod >> 8) & 0x1F == 4`)
  and a present `BTHENUM` service node with that address and `_VID&0001004C_PID&xxxx`. Vendor id source 1 is
  Bluetooth SIG, 0x004C is Apple, and the 4 hex digits after `PID&` are the model:

  ```text
  BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&2024\7&3549F023&2&282D7FB83C8B_C00000000
  ```

  The `BTHENUM\DEV_<addr>` node carries no VID/PID; the service nodes do. On this PC the owner's AirPods 4 (ANC)
  have five of them, all `_VID&0001004C_PID&201B` on address `9CA9C520AFB8`: GATT, "AAP Server",
  Service Discovery, "AirPods Hands-Free AG" and "UARPS" (firmware update). The app uses none of them.
- This PID equals the model id in the broadcast (section 4). That was seen on Windows for 0x2014, 0x201B,
  0x201F, 0x2024 and 0x2027 (AirPodsDesktop issues #46, #93 and #140, Orbit, earshot), and Apple's bluetoothd
  logs `PID 0x200E ... VID 0x004C, VS 1` next to the advertised bytes `0e 20`.
- The name shown is Windows' device name, without a trailing `" - Find My"`. The name is never used for
  matching: the owner can rename the AirPods.

## 3. Receiving

WinRT's `BluetoothLEManufacturerData.Data` excludes the 2-byte company id, so `Data[0]` is the Continuity
message type. Checked live on this PC on 2026-10-15: a passive watcher received 1537 Apple entries from nearby
phones and Macs in 30 minutes, all starting with a type byte (`10` Nearby Info, `09` AirPlay, `12` Find My,
`0C` Handoff), for example `10 06 0C 19 0B 3D 57 68`. No AirPods were in range.

`Data` is a list of `[type][length][length bytes]` messages. A Proximity Pairing status message fills the
whole 31-byte legacy advertisement (2 bytes of AD header, 2 of company id, 27 of message), so in practice it is
the only one. The decoder still walks the list and checks every Apple entry.

Accept a message only when all hold, otherwise ignore it (trace only):

| Byte | Value |
|---|---|
| 0 | `07` Proximity Pairing |
| 1 | `19`: 25 more bytes, 27 in total |
| 2 | `01`: the status frame |

Other `07` frames have other layouts and must not be read at these offsets. Some were seen in captures:
- `07 19 07 ...`: a one-off frame from the identity address when a pod connects.
- `07 0F 00 ...`: a short frame that carries a public address.
- `07 11 06 ...`: Apple's AccessoryStatus layout.
- `07 13 01 ...`: counterfeit AirPods.

Apple's own parser accepts a sub-type 1 frame of 23 bytes or more. The exact length 0x19 is this app's
stricter choice, the same as AirPodsDesktop's and capod's genuine-model checks.

## 4. Status message layout

Offsets count from the type byte (`Data[i]`). capod counts from byte 2 (its `data[0]` is our `Data[2]`), and
sources that include the company id count 2 more.

| Byte | Meaning | Used |
|---|---|---|
| 0-2 | `07 19 01`, see section 3 | check |
| 3-4 | model id, little-endian: `14 20` is 0x2014 | match |
| 5 | status. Bit 5 (`0x20`) set: the pod sending this frame is the **left** one | yes |
| 6 | low nibble: the sending pod's level; high nibble: the other pod's level | yes |
| 7 | low nibble: case level; `0x10` sending pod charging, `0x20` other pod charging, `0x40` case charging | yes |
| 8 | lid: bits 0-2 open counter, `0x08` closed | trace |
| 9 | colour | no |
| 10 | connection/audio state (`05` while music plays) | trace |
| 11-26 | 16 bytes, AES-128-ECB encrypted with a key only an AAP session gives | never |

Many sources print the model as `0x0E20` (the same two bytes read big-endian); Apple's parser reads
`*(bytes + 3)` as a 16-bit value and logs `PID 8206`, which is 0x200E.

## 5. Decoding

### 5.1 Left and right

```text
fromLeft  = Data[5] & 0x20
this      = Data[6] & 0x0F      thisCharging  = Data[7] & 0x10
other     = Data[6] >> 4        otherCharging = Data[7] & 0x20
left  = fromLeft ? this : other         right  = fromLeft ? other : this
```

Apple maps it the same way: `SFBLEScanner.mm` turns status bit 5 into its "left" flag (L4584,
`(4 * status) & 0x180 ^ 0x100`) and gives the low nibble and `0x10` to the sending bud (L7364-7407). Ground
truth with unequal pods:
- Apple's bluetoothd logs `Battery L -100% R -95%`, `Prim Right` for the advertised bytes `0b a9`.
- ha-airpods-bridge has two frames taken at the same moment, `13 A9` from the right pod and `73 9A` from the
  left one. Both decode to left 100 / right 90, the values its owner confirmed over AAP.

LinuxPods swaps the nibbles, which contradicts both.

### 5.2 Levels

| Nibble | Meaning |
|---|---|
| 0-9 | n × 10 % to n × 10 + 9 %: the nibble is the level divided by 10, rounded down |
| A | 100 % |
| F | not reported (the pod is out of range, or the case while both pods are out of it) |
| B-E | never seen in any capture; treated as not reported |

Rounding down is confirmed by these pairs, nibble on the left:
- 9 for 95 %, 97 % and 98 % (Apple's log, the capod decrypt test, ha-bridge).
- 8 for 86 %, 6 for 65 %, 5 for 54 %, 4 for 44 % and 3 for 37 %.

The app shows n × 10 %, the lower bound, marked approximate, so a low-battery alert never comes late.

### 5.3 Case and charging

- Case level is the low nibble of `Data[7]`, the same for both pods, and case charging is `0x40`. The case has no
  broadcast of its own: a pod reports it only while that pod is inside. With both pods out it reads F, even with
  the lid open (seen live on the owner's AirPods 4: one pod in the case was enough, an open empty case was not).
  The app keeps the last case level read from a pod in the case for 6 hours, across reconnects, and says when it
  was read; a closed case's level only changes while it charges. A level from a pick on signal alone is never
  kept that way.
- A set charging bit means charging. A clear bit only means "not reported as charging": the frame does not
  separate a worn pod from one that is full in the case. So a clear bit maps to `Unknown`, never to
  "discharging".
- Beats Solo Buds (0x2026) have a case without a battery, so their case nibble is never shown.

### 5.4 One-battery models

AirPods Max and the Beats headphones and neckbands have one battery. It is in the low nibble of `Data[6]`
whatever bit 5 says. The high nibble and the case nibble read `0` on AirPods Max and Max USB-C (`F` on Max 2,
Flex and BeatsX); reading them as earbuds would show "0 %". Charging (`Data[7] & 0x10`) is used for the AirPods
Max family only. On the Max USB-C it was seen to drop out for about 16 s during a cable charge, so a charging
bit is held for 30 s. For the Beats models charging is not reported.

## 6. Models

Two bytes on air, the PID they make, and the layout: earbuds with a case, or one battery. Layouts come from
capod's device classes and Bluetooth-Battery-Meter's model configs, which agree on every row.

| Bytes 3-4 | PID | Model | Layout |
|---|---|---|---|
| `02 20` | 0x2002 | AirPods (1st generation) | earbuds |
| `0F 20` | 0x200F | AirPods (2nd generation) | earbuds |
| `13 20` | 0x2013 | AirPods (3rd generation) | earbuds |
| `19 20` | 0x2019 | AirPods 4 | earbuds |
| `1B 20` | 0x201B | AirPods 4 (ANC) | earbuds |
| `30 20` | 0x2030 | AirPods 5 | earbuds |
| `36 20` | 0x2036 | AirPods 5 | earbuds |
| `0E 20` | 0x200E | AirPods Pro | earbuds |
| `14 20` | 0x2014 | AirPods Pro 2 | earbuds |
| `24 20` | 0x2024 | AirPods Pro 2 (USB-C) | earbuds |
| `27 20` | 0x2027 | AirPods Pro 3 | earbuds |
| `0A 20` | 0x200A | AirPods Max | one battery, charging |
| `1F 20` | 0x201F | AirPods Max (USB-C) | one battery, charging |
| `2D 20` | 0x202D | AirPods Max 2 | one battery, charging |
| `0B 20` | 0x200B | Powerbeats Pro | earbuds |
| `1D 20` | 0x201D | Powerbeats Pro 2 | earbuds |
| `2F 20` | 0x202F | Powerbeats Fit | earbuds (no capture) |
| `12 20` | 0x2012 | Beats Fit Pro | earbuds |
| `11 20` | 0x2011 | Beats Studio Buds | earbuds |
| `16 20` | 0x2016 | Beats Studio Buds+ | earbuds |
| `26 20` | 0x2026 | Beats Solo Buds | earbuds, no case battery |
| `03 20` | 0x2003 | Powerbeats3 | one battery |
| `05 20` | 0x2005 | BeatsX | one battery |
| `06 20` | 0x2006 | Beats Solo3 | one battery |
| `09 20` | 0x2009 | Beats Studio3 | one battery |
| `0C 20` | 0x200C | Beats Solo Pro | one battery |
| `0D 20` | 0x200D | Powerbeats 4 | one battery |
| `10 20` | 0x2010 | Beats Flex | one battery |
| `17 20` | 0x2017 | Beats Studio Pro | one battery |
| `25 20` | 0x2025 | Beats Solo 4 | one battery |
| `38 20` | 0x2038 | Beats 360 | one battery |

AirPods 5 has two ids, one per case type; both are shown as "AirPods 5". iOS lists more Apple audio PIDs with a
case (0x201E, 0x2020, 0x2028) whose broadcast was never captured. A paired Apple device with a PID not in the
table is shown with "model not supported yet" instead of a guessed layout.

## 7. Which broadcast is ours

The broadcast cannot be tied to the paired AirPods with certainty:
- Each pod advertises from its own random resolvable address, which changes about every 15 minutes and often
  when the pods leave the case or reconnect (the owner's left pod kept its address when taken out). Windows does
  not resolve it to the paired address.
- Proving ownership needs the identity key, which only AAP can read.
- Strangers' AirPods of the same model are often received more strongly than the owner's own pods: -42 to
  -68 dBm against -74 to -100 dBm in two AirPodsDesktop traces. So neither a hard signal floor nor "the
  strongest" works on its own.

The tracker (`AppleBroadcastTracker`, one per paired device) uses these rules:

1. Only status frames whose model id equals the paired device's PID count. The scan runs while any Apple audio
   device is paired, so the addresses already around before the AirPods connect are known. Addresses first heard
   in the first 10 s after the scan (re)starts count as already around.
2. Once an address is chosen by continuity or at the connect, it is kept while it is heard, whatever its signal
   strength. One chosen as the strongest gives way, with its levels dropped, as soon as an address matching
   continuity or the connect has 2 frames. A chosen address silent for 30 s is dropped and the tracker chooses
   again.
3. A new address needs 2 frames within the last 30 s, then the first rule that matches picks it:
   1. **Continuity**: every level it reports, and at least one, is within one 10 % step of the last levels shown
      by an address chosen by continuity or at the connect. This follows the pods through an address change, and
      from one pod to the other. Levels shown from a strongest pick never become the reference, and are dropped
      when it goes silent.
   2. **Born at connect**: it was first heard between 60 s before and 30 s after Windows reported the AirPods
      connected. New addresses appear when the pods leave the case and connect.
   3. **Strongest**: otherwise the strongest one at -80 dBm or better (smoothed: 0.7 × old + 0.3 × new). It
      waits until every address in range had time to be heard: 5 s when the connect moment is unknown (the app
      started with the AirPods connected, and the CLI), 30 s after a known connect, 10 s after the followed
      address went silent.

   When more than one address matches the same rule, the strongest wins.
4. On every connect the choice and the levels start afresh.

A same-model pair nearby with the same levels can still be taken for the owner's, and no rule can rule that out
without AAP.

## 8. What is shown

| Situation | Reading |
|---|---|
| Paired, not connected | none: the device is hidden until it connects again; its last values stay in the history |
| Connected, the followed address heard within 3 min | Online, levels in 10 % steps, approximate. A pod's level is kept 3 min and the case's 10 min after the last frame that reported it; after that the case shows the level remembered from the last time a pod was in it (up to 6 h), with "Case read at hh:mm" |
| Connected, nothing heard yet, or nothing for 3 min | Online with no level and a note ("Waiting for its Bluetooth battery broadcast" or "No battery broadcast for N min"); the flyout keeps the last levels, dimmed. Windows' own value instead if it has one |
| Connected, the scan could not start or stopped on its own, no Windows value | Error: "Bluetooth LE scan failed: <reason>", until any Apple advertisement arrives; the scan is retried a minute later |
| Two connected devices with the same PID | Error: they cannot be told apart |
| PID not in section 6 | Error: model not supported yet |

The CLI reads once, so its first poll waits up to 10 s for the first broadcast. The app never waits: the provider
asks for a refresh when a value arrives.

## 9. Windows' own value

Windows' Bluetooth audio gateway (`BTAGService.dll` on this PC) handles Apple's hands-free battery extension
(`AT+XAPL`, `AT+IPHONEACCEV`, reply `+XAPL=iPhone`). No capture shows AirPods sending it to Windows, and AndroPods
reports that Android, which also answers `+XAPL=iPhone`, never gets a level from AirPods 2. On this PC Windows held
no value for the owner's connected AirPods 4 (ANC), in the case or worn. The app reads the battery property
anyway, read-only (`BluetoothBatteryProperty`), and shows it, marked "approx. from Windows", only while no
broadcast value is fresh.

## 10. Not used

- The encrypted tail and AAP (section 1). The `07 0F 00` and sub-type 6 frames may carry exact levels in the
  clear, but their layouts are unconfirmed, so they are only traced.
- The in-ear, lid and audio-state bits. Their meanings differ between sources and some go stale on non-Apple
  hosts.

## 11. Open questions

- Whether the levels keep updating over hours with a non-Apple host. The owner's AirPods 4 kept broadcasting
  while worn with music, and their levels dropped from 90 to 80 within 9 minutes (section 12), but the MagicPods
  author says status stops updating on Windows.
- AndroPods reports that AirPods 4 on firmware 8B39 send no `07` frames at all. The owner's AirPods 4 (ANC) do;
  their firmware version is not known here.
- Whether Windows ever gets an AirPods level over hands-free (section 9).

## 12. Live check

```text
batteryinfo apple-scan 30                    # every Apple 07 frame in range, decoded, with address and RSSI
batteryinfo list --vendor Apple --trace      # the provider's choice and reading
```

The trace prints bytes 0-10 of each frame; the encrypted tail is left out.

2026-10-15, the owner's AirPods 4 (ANC), connected to this PC: 488 frames in 10 minutes, all
`ScannableUndirected`, replayed in `tests/.../Apple/Fixtures/airpods4-anc-live.txt`:

```text
5341481C2754  -37 dBm  07 19 01 1B 20 35 99 F0 53 00 04   left pod in the case:  L 90 + R 90 +, case 0 +
4D00295F4A98  -40 dBm  07 19 01 1B 20 55 99 F0 52 00 04   right pod in the case: the same, swapped
5341481C2754  -39 dBm  07 19 01 1B 20 35 99 F1 53 00 04   43 s later: case 10 +
5341481C2754  -53 dBm  07 19 01 1B 20 23 99 8F 13 00 04   pods taken out: case not reported
5341481C2754  -65 dBm  07 19 01 1B 20 2B 88 8F 13 00 05   worn, music playing (byte 10 = 05): L 80, R 80
```

- In the case both pods broadcast, from two addresses, with this and other swapped. Out of the case only the
  left pod's address was heard, and it was the same address as in the case.
- Worn, frames came a median 0.4 s and at most 7.6 s apart, at -50 to -85 dBm. One frame arrived with no
  signal value (-127).
- `batteryinfo list --vendor Apple` found the pair connected, waited for the 5 s settle, followed the stronger
  pod and printed `Left ~90% charging | Right ~90% charging | Case ~0% charging` from the broadcast. Windows' own
  battery value was empty.
