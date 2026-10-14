# Logitech HID++ (LIGHTSPEED): battery protocol

Status: verified live on this PC (2026-10-14, verdict `confirmed-with-corrections`).

```text
LOGITECH HID++ BATTERY READ: IMPLEMENTATION SPEC (Windows, receiver 046D:C54D)
Verified on this machine on 2026-10-14, by the original research and again by an independent verifier probe (swId 0x0E, 12:19 and 12:28). Each item says whether it was verified on hardware, comes from the vendor code, or comes from open-source references.

1. DEVICE MATCHING
- Receivers: VID 0x046D. This machine has PID 0xC54D (LIGHTSPEED, rev 0x1403).
- Solaar lists these other LIGHTSPEED receivers with the same layout (usb_interface 2): C539, C53A, C53D, C53F, C541, C545, C547.
- Unifying receivers C52B/C532 use the same register layout. Bolt C548 differs; see 4e.
- Enumerate HID interfaces (SetupDiGetClassDevs with the HID GUID, DIGCF_PRESENT|DIGCF_DEVICEINTERFACE). For each one, read HidD_GetAttributes and HidP_GetCaps. Keep the vendor collections:
  * SHORT collection: UsagePage 0xFF00, Usage 0x0001, InputReportByteLength = OutputReportByteLength = 7, report ID 0x10. Path here: \\?\hid#vid_046d&pid_c54d&mi_02&col01#c&1823a09c&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}
  * LONG collection: UsagePage 0xFF00, Usage 0x0002, In/Out length 20, report ID 0x11. Path: ...&mi_02&col02#c&1823a09c&0&0001#...
  * Pair the two collections by their common parent devnode (CM_Get_Parent / DEVPKEY_Device_Parent = USB\VID_046D&PID_C54D&MI_02\B&18153BD2&0&0002; verified). As a fallback, pair by the path with "&colNN" and the final "&000N" removed.
  * Some newer receivers also expose a very-long collection (U:0x0004, report 0x12). This receiver does not, and battery reads do not need it.
- Ignore MI_00 (mouse) and MI_01 (keyboard, consumer, system); they carry no HID++. Opening MI_00 fails with error 5 (access denied), even with zero desired access (verified).
- Wired mode (not verified): on its USB cable the mouse should enumerate as 046D:C0A8 (from the 0x0003 model IDs) with the same FF00/U1 and FF00/U2 collections. Address it with device index 0xFF. Treat it as the same device as receiver slot 1 by matching unitId B4655832, which equals the receiver's extended-pairing serial (verified equal).

2. OPENING AND I/O (verified)
- Open with CreateFile(path, GENERIC_READ|GENERIC_WRITE, FILE_SHARE_READ|FILE_SHARE_WRITE, OPEN_EXISTING, FILE_FLAG_OVERLAPPED). Never request exclusive access. Close the handles promptly on device removal.
- Keep one overlapped ReadFile pending on BOTH handles at all times, sized to InputReportByteLength (7 and 20). Byte 0 of every buffer is the report ID. Every handle has its own input queue and receives every input report, including replies to other software.
- Writes use WriteFile with exactly OutputReportByteLength bytes:
  * Short 0x10 frames (7 bytes) go to the U:0001 handle.
  * Long 0x11 frames (20 bytes) go to the U:0002 handle.
  * A wrong-length write fails locally in both directions (7 bytes on the 20-byte handle, or 20 bytes on the 7-byte handle): WriteFile error 1, nothing is sent (verified).
- The device accepts short and long forms of the same request and gives identical replies (verified).
- REPLIES DO NOT COME BACK ON THE HANDLE YOU WROTE TO (verified):
  * HID++ 2.0 replies and HID++ 2.0 errors (0xFF) are long 0x11 reports and arrive on U:0002, even for short requests.
  * HID++ 1.0 short register replies (0x81) and every 0x8F error are 0x10 reports and arrive on U:0001.
  * GET_LONG_REGISTER (0x83) replies are 0x11 and arrive on U:0002.
- Latency on this hardware:
  * Receiver-answered requests (registers, pings to empty slots): 3.5-4 ms.
  * Device-routed HID++ 2.0 replies: 5-42 ms, typically 9 or about 27 ms.
- HidD_SetOutputReport was not tested. Use WriteFile.

3. FRAME FORMAT
- Short frame: [0]=0x10 [1]=devIdx [2]=subId/featureIndex [3]=address or (function<<4 | swId) [4..6]=params, zero padded.
- Long frame: [0]=0x11 [1]=devIdx [2]=featureIndex [3]=(function<<4 | swId) [4..19]=params.
- devIdx: 0xFF is the receiver itself; 1..6 are pairing slots. This receiver has max_devices = 2.
- Software ID (low nibble of byte 3): any value 0x1..0xF is echoed (verified with 0x0C and 0x0E). Pick a configurable default, for example 0x0C or 0x0E.
  * Never use 0: swId 0 marks unsolicited notifications.
  * Values known to be taken: 0x01 Linux kernel, 0x07 OpenRGB, 0x0A LGSTrayBattery/LGSTrayEx, 0x0B Solaar, 0x0D G HUB, 0x0F device firmware.
  * logi_lamparray_service sets its swId at runtime from a device-object field, so it is not a known constant.
- Serialize: keep at most one outstanding request per receiver.

4. RECEIVER (HID++ 1.0 REGISTERS, devIdx 0xFF): READ-ONLY GETs
Only sub-ID 0x81 (GET_REGISTER) and 0x83 (GET_LONG_REGISTER) are reads. Never send 0x80 or 0x82 (SET). Use a timeout of about 1 s.

a) Notification flags. Send 10 FF 81 00 00 00 00. Reply 10 FF 81 00 r0 r1 r2. The flags are r0<<16 | r1<<8 | r2.
   - Observed 0x000100: WIRELESS on, SOFTWARE_PRESENT (0x000800) off.
   - Do NOT write this register. Solaar and the kernel write 0x000900; that changes state that Logitech software shares.

b) Connection state. Send 10 FF 81 02 00 00 00. Reply 10 FF 81 02 00 CC RR, where CC = count of connected devices.
   - Observed 10 FF 81 02 00 01 00 (verified twice).

c) Receiver info. Send 10 FF 83 B5 03 00 00. Reply 11 FF 83 B5 03 s0 s1 s2 s3 x MAX ...
   - payload = bytes from offset 4; payload[1..4] = serial; payload[6] = max_devices.
   - Observed 11 FF 83 B5 03 A4 08 25 0B 01 02 08 FF 00..: serial A408250B, max_devices 2 (verified).
   - If max_devices is not in 1..6, this implementation scans slots 1..6. Empty slots answer with 0x8F errors, so this is harmless. (Solaar falls back to the product default, 1.)

d) Per slot n = 1..max:
   - PAIRING INFO. Send 10 FF 83 B5 (0x20+n-1) 00 00.
     Reply payload: [0]=0x2n [1]=destination id [2]=interval byte (meaning unverified on LIGHTSPEED; do not display it) [3..4]=wpid (big-endian) [7]&0x0F = HID++ 1.0 device kind (2 = mouse).
     Observed 11 FF 83 B5 20 08 08 40 BD 11 00 02 B1 01 00 00 7F 24 00 00: wpid 40BD, kind 2.
   - EXTENDED PAIRING. Send 10 FF 83 B5 (0x30+n-1) 00 00.
     Reply payload: [1..4] = device serial; [9]&0x0F = power switch location.
     Observed serial B4655832.
   - CODENAME. Send 10 FF 83 B5 (0x40+n-1) 00 00.
     Reply payload: [1] = length, [2..] = ASCII.
     Observed 'X2 SUPERSTRIKE'.
   - EMPTY SLOT: reply 10 FF 8F 83 B5 09 00 (error 0x09 on this receiver; other receivers may return 0x03 or 0x02). Treat any 0x8F reply to a B5 read as "slot empty".
   - Match long-register replies on [2]=0x83, [3]=0xB5 and [4]=the requested sub-register.

e) Bolt receivers (C548), not needed here: pairing info is sub-register 0x50+n and name is 0x60+n read with parameter 0x01 (no -1). The wpid is little-endian at payload[2..3], the kind is payload[1]&0x0F, the name length is payload[2] and the text starts at payload[3].

f) Receiver firmware (optional). Send 10 FF 81 F1 0k 00 00.
   - Observed k=1 -> 07 02 (FW 07.02); k=2 -> 00 11 (build 0011); k=3 -> C2 BE; k=4 -> 02 11 (bootloader 02.11).

g) HID++ 2.0 ping to the receiver: 10 FF 00 1C 00 00 81 returns 10 FF 8F 00 1C 01 00 (INVALID_SUB_ID), which means the receiver speaks HID++ 1.0 only.

5. DEVICE PRESENCE (PING = IRoot.getProtocolVersion; root feature index 0x00, function 1)
- Send 10 nn 00 (0x10|sw) 00 00 MARK, with MARK a random byte from 1 to 255.
- ONLINE HID++ 2.0+: 11 nn 00 (0x10|sw) MAJ MIN MARK ...
  Observed 11 01 00 1E 04 02 A2 (HID++ 4.2). The first ping after idle took 25 ms; later pings 5-8 ms.
- HID++ 1.0 error 10 nn 8F 00 (0x10|sw) EE 00, by error code EE:
  * 0x08 UNKNOWN_DEVICE: nothing paired in that slot (verified for slots 2-6).
  * 0x09 RESOURCE_ERROR or 0x04 CONNECT_FAIL: paired but unreachable (off, asleep or out of range). Treat as offline. This is Solaar and kernel behaviour, NOT observed on this hardware.
  * 0x01 INVALID_SUB_ID: a HID++ 1.0 device is online.
- No reply within about 2.5 s: treat as offline.

6. HID++ 2.0 FEATURE CALLS (devIdx 1..6)
- Request: 10 nn FI (fn<<4|sw) p0 p1 p2. Reply: 11 nn FI (fn<<4|sw) r0..r15, where r0 is the byte at offset 4.
- Use a timeout of 2-3 s (Logitech's service uses 3000 ms; Solaar 4 s).
- getFeature (IRoot, FI = 0x00, fn 0): send 10 nn 00 (0x00|sw) HI LO 00. Reply: r0 = feature index (0 = not supported), r1 = type flags, r2 = version.
  * Verified on device 1: 0001 -> 01 (v2); 0003 -> 02 (v7); 0005 -> 03 (v5); 1000/1001/1F20 -> 00; 1004 -> 06 (v5); 1D4B -> 04 (v0).
  * Indexes are per-device and per-firmware. Look them up after every reconnect, or cache them keyed by unitId plus firmware.
  * Optional sanity check: FEATURE_SET getFeatureId(index) must return the same ID. Verified: getFeatureId(06) -> 10 04 00 05.
- FEATURE_SET 0x0001:
  * fn0 getCount: r0 = count, excluding root. Observed 0x23.
  * fn1 getFeatureId(i): r0..r1 = feature ID (big-endian), r2 = type, r3 = version.
  * An out-of-range index returns 11 nn FF FI 1X 02 (INVALID_ARGUMENT; verified).
- DEVICE_FW_INFO 0x0003:
  * fn0 getDeviceInfo: r0 = entity count, r1..r4 = unitId, r5..r6 = transport bits, r7..r12 = model IDs (2 bytes per set bit, in bit order btid 0x01, btleid 0x02, wpid 0x04, usbid 0x08), r13 = extended model.
    Observed 02 B4 65 58 32 00 0C 40 BD C0 A8 00 00 01 01: unitId B4655832, wpid 40BD, usbPID C0A8.
  * fn1 getFwInfo(e): r0&0x0F = type (0 = main app, 1 = bootloader, 2 = hardware), r1..r3 = ASCII prefix, r4.r5 = version (BCD), r6..r7 = build, r8 = active, r9..r10 = transport PID.
    Observed BL2 73.00 b0011; MPM 42.00 b0011 (active).
- DEVICE_NAME 0x0005:
  * fn0: r0 = name length.
  * fn1(offset): up to 16 chars per call, starting at r0. Loop with offset += 16.
  * fn2: r0 = device type, with 0 keyboard, 1 remote, 2 numpad, 3 mouse, 4 trackpad, 5 trackball, 6 presenter, 7 receiver.
  * Verified: 'PRO X2 SUPERSTRIKE'; type 3.
- Friendly name 0x0007 (fn0 length, fn1(offset), name starts at r1) is absent on this mouse.

7. BATTERY: SELECT A FEATURE
- Devices normally expose only one battery feature. This implementation probes 0x1004, 0x1000, 0x1001, 0x1F20 and uses the first present.
- The sources order these differently: kernel 1000, 1004, 1001, 1F20; Solaar 1000, 1001, 1004, 1F20, Centurion.
- Newer Logitech headsets may use the 'Centurion' protocol battery feature (vendor class centpp_feature_0104, Solaar CENTURION_BATTERY_SOC), which this spec does NOT cover.
- Logitech's lamparray service implements 0x1004, 0x1001, 0x1F20 and Centurion 0x0104.

A) 0x1004 UNIFIED_BATTERY (the one this device uses; index 0x06, v5; verified)
- fn0 getCapabilities: send 10 01 06 (0x00|sw) 00 00 00. Reply 11 01 06 (0x00|sw) 0F 0F 02 00...
  * r0 = supported level bits (bit0 critical, bit1 low, bit2 good, bit3 full).
  * r1 = flags (kernel plus vendor parser): bit0 rechargeable, bit1 state of charge supported, bit5 removable battery capable.
    Bits 2-3 are set on this v5 device; their meaning is unknown and the vendor parser ignores them.
  * r2 (0x02 here) is ignored by the vendor parser.
- fn1 getStatus: send 10 01 06 (0x10|sw) 00 00 00. Reply 11 01 06 (0x10|sw) r0 r1 r2 r3 r4 r5 ...
  * r0 (report byte 4) = state of charge, 0-100%. Valid when capabilities r1 bit1 is set.
  * r1 (byte 5) = level: 8 full, 4 good, 2 low, 1 critical, 0 unknown. Mask with the supported levels.
  * r2 (byte 6) = charging status: 0 discharging; 1 charging; 2 charging slow; 3 charge complete (full); 4 charging error; 5 wireless charging; >= 6 invalid.
    - The source of value 5 and the invalid range is the vendor decoder; the kernel defines 0-4.
    - Solaar's '2 = almost full' is wrong.
    - UI mapping: 1, 2 and 5 = charging; 3 = full; 4 = error (not charging); 0 = discharging; other = unknown.
  * r3 (byte 7) = external power status. Advisory only. Vendor value names are 0 no power, 1 wired, 2 wireless, >= 3 invalid. The vendor parser does not actually read this byte, and the kernel ignores it. Do not use r3 to decide charging.
  * r5 (byte 9) bit0 = removable battery removed. Use it only when capabilities r1 bit5 is set (vendor parser).
  * If state of charge is unsupported, approximate the percent from the level as Solaar does: full 90, good 50, low 20, critical 5, unknown 0.
- Event 0 (unsolicited, byte 3 == 0x00): 11 nn FI 00 r0 r1 r2 r3 ..., same layout as getStatus.
- Verified decode, 15 readings between 12:03 and 12:28: 38 04 00 00 -> 56%, GOOD, discharging, external power 0, byte9 0.
- getStatus may intermittently return HID++1.0 error 0x09 (RESOURCE_ERROR) via the receiver. The kernel treats it as transient: retry later and keep the last value.

B) 0x1000 BATTERY_STATUS (from the sources; not present on this mouse)
- fn0 getBatteryLevelStatus: r0 = level % (0 = unknown), r1 = next level %, r2 = status.
  * Status values: 0 discharging, 1 recharging, 2 charge in final stage (charging), 3 full (show 100%), 4 slow recharge, 5 invalid battery, 6 thermal error, 7 other charging error.
  * The level is only trustworthy while discharging.
- fn1 getBatteryCapability: r0 = number of levels; r1 flags = bit0 disable OSD, bit1 mileage, bit2 rechargeable.
- Event 0 uses the fn0 layout.

C) 0x1001 BATTERY_VOLTAGE (from the sources)
- fn0 getBatteryInfo: r0..r1 = millivolts (big-endian); r2 = flags.
  * Flag bit7 = external power. When bit7 is set, (r2 & 0x07) gives 0 charging, 1 full, 2 not charging (kernel; Solaar masks with 0x03).
  * Flag bit3 = fast charge, bit4 = slow/trickle charge, bit5 = critical.
- Percent with the Linux kernel table: for i = 0..99, if mV >= table[i] return 100-i; otherwise 0. The table was verified identical to kernel master:
  4186,4156,4143,4133,4122,4113,4103,4094,4086,4075,4067,4059,4051,4043,4035,4027,4019,4011,4003,3997,3989,3983,3976,3969,3961,3955,3949,3942,3935,3929,3922,3916,3909,3902,3896,3890,3883,3877,3870,3865,3859,3853,3848,3842,3837,3833,3828,3824,3819,3815,3811,3808,3804,3800,3797,3793,3790,3787,3784,3781,3778,3775,3772,3770,3767,3764,3762,3759,3757,3754,3751,3748,3744,3741,3737,3734,3730,3726,3724,3720,3717,3714,3710,3706,3702,3697,3693,3688,3683,3677,3671,3666,3662,3658,3654,3646,3633,3612,3579,3537
- Solaar instead interpolates linearly between (4186,100), (4067,90), (3989,80), (3922,70), (3859,60), (3811,50), (3778,40), (3751,30), (3717,20), (3671,10), (3646,5), (3579,2), (3500,0).
- Event 0 uses the same layout.

D) 0x1F20 ADC_MEASUREMENT (mostly headsets; from the sources)
- fn0: r0..r1 = millivolts (big-endian); r2 = flags: 0x01 discharging, 0x03 charging, 0x07 full, 0x0F or anything else = unknown.
- Solaar's rule: flags bit0 = 0, or an error reply, means the device is inactive or offline.
- Percent table (kernel 1F20, same lookup; 0 mV = 0%; verified identical to kernel master):
  4030,4024,4018,4011,4003,3994,3985,3975,3963,3951,3937,3922,3907,3893,3880,3868,3857,3846,3837,3828,3820,3812,3805,3798,3791,3785,3779,3773,3768,3762,3757,3752,3747,3742,3738,3733,3729,3724,3720,3716,3712,3708,3704,3700,3696,3692,3688,3685,3681,3677,3674,3670,3667,3663,3660,3657,3653,3650,3646,3643,3640,3637,3633,3630,3627,3624,3620,3617,3614,3611,3608,3604,3601,3598,3595,3592,3589,3585,3582,3579,3576,3573,3569,3566,3563,3560,3556,3553,3550,3546,3543,3539,3536,3532,3529,3525,3499,3466,3433,3399

E) HID++ 1.0 devices (ping answered with error 0x01)
- Register 0x0D: send 10 nn 81 0D 00 00 00. r0 = %, (r2 & 0xF0): 0x30 discharging, 0x50 charging, 0x90 full.
- Register 0x07: send 10 nn 81 07 00 00 00. r0 level: 7 full, 5 good, 3 low, 1 critical. r1 charging: 0x00 discharging, (r1&0x21)==0x21 charging, (r1&0x22)==0x22 full.
- No LIGHTSPEED device needs this path.

8. MATCHING REPLIES, ERRORS AND NOTIFICATIONS (apply to reports from BOTH handles)
- Reply: [1]==devIdx && [2]==FI && [3]==(fn<<4|sw). For ping, also [6]==MARK.
- HID++ 2.0 error (0x11 report): [2]==0xFF && [3]==FI && [4]==(fn<<4|sw). Error code is [5]:
  * 1 unknown, 2 invalid argument (also returned for an out-of-range index; verified), 3 out of range, 4 hardware error, 5 internal/not allowed, 6 invalid feature index (refresh the feature cache), 7 invalid function, 8 busy (retry after about 100 ms, at most 3 times), 9 unsupported.
- HID++ 1.0 error (0x10 report): [2]==0x8F && [3]==FI (or 0x81/0x83) && [4]==(fn<<4|sw) (or the register). Error code is [5], with the meanings in section 5.
  * A HID++ 2.0 request to a device that has gone away returns this receiver-generated 0x8F form. Treat 0x09 and 0x04 as offline/transient and keep the last known value with its timestamp.
- Unsolicited reports, checked in this order:
  1. A 0x10 short report with [2] in 0x40..0x7F is a HID++ 1.0 receiver notification (0x41 connect, 0x40 unpair, ...).
  2. Otherwise, [2] < 0x80 with ([3] & 0x0F) == 0 is a HID++ 2.0 feature event: the event number is [3]>>4; route it by devIdx plus feature index.
  3. Anything with [2] & 0x80 set that is not a match is a register reply or error for someone else.
- Replies carrying another swId belong to other software (G HUB, lamparray, Solaar, LGSTray). Ignore them.
- Flush stale input before each request, or rely on strict matching.

9. PASSIVE NOTIFICATIONS TO SUBSCRIBE TO (no writes needed)
- Battery events: event 0 of 0x1004, 0x1000, 0x1001 or 0x1F20, laid out as in section 7.
- 0x1D4B WIRELESS_DEVICE_STATUS (index 0x04 here), event 0: 11 nn 04 00 status request reason.
  * status 1 = reconnected; request 1 = software reconfiguration needed; reason 1 = power switch.
  * On this event, re-ping and re-read the battery.
- Receiver 0x41 device connection: 10 nn 41 PP II LL HH.
  * PP = protocol: 0x0C, 0x0D, 0x0F or 0x11 = LIGHTSPEED variants; 0x04 = Unifying; 0x10 = Bolt.
  * II: bit6 = 1 means link NOT established (offline); bit5 = encrypted; low nibble = HID++ 1.0 kind.
  * LL HH = wpid least- and most-significant bytes.
  * This needs receiver flag 0x000100, which is already set here.
- 0x40 with address 0x02 means the device was unpaired.
- No unsolicited traffic was seen in about 7.5 minutes of listening (original) plus about 10 s (verifier). Whether the device sends battery events without host 'software present' is unverified.
- Recommended strategy: poll every 60 s, plus at start-up, plus on 0x41 link-up or 0x1D4B. Also handle the events when they arrive.

10. ALGORITHM
- Group the FF00 collections by parent and open U1 and U2.
- Read B5/03 for max_devices.
- For each slot n: read B5/2n (empty slot -> skip), B5/4n (codename) and B5/3n (serial).
- Ping the slot. If online: getFeature for 0x0005, 0x0003, 0x1004, 0x1000, 0x1001, 0x1F20 and 0x1D4B. Read the name and unitId. Read the battery from the selected battery feature and decode it as in section 7.
- If offline (0x8F 09/04 or timeout): show the codename as offline, with the last known battery value and its timestamp.

11. SAFETY WHITELIST (everything this design sends is a read)
- Receiver: 0x81 / 0x83 only.
- IRoot fn0/fn1; 0x0001 fn0/1; 0x0003 fn0/1; 0x0005 fn0/1/2; 0x0007 fn0/1; 0x1000 fn0/1; 0x1001 fn0; 0x1004 fn0/1; 0x1F20 fn0.
- Never send set functions (for example 0x1004 fn2+, 0x1000 fn2+, 0x0007 fn3) or register writes (0x80/0x82). This includes:
  * 10 FF 80 00 .. (notification flags), which Solaar and the kernel send.
  * 10 FF 80 02 02 00 00 ('force arrival announce'), which LGSTrayBattery sends.
- Enforce this in code with a (featureId, function) whitelist, keyed by the looked-up feature ID, not by the index.
```

## Remaining risks

- Asleep/off behaviour is still unobserved: the mouse stayed online through both investigations, 12:03-12:28. The 0x8F 0x09/0x04 handling and 0x41 link-down handling come from Solaar and the kernel only. Re-test with the mouse switched off by running verify\v_min.py, which is read-only.
- Charging path is unobserved: r2 = 1/2/5 and r3 (external power) have never been seen on this hardware. The vendor's own parser derives 'external power' oddly from r2 and never reads byte 7, so the meaning of r3 is uncertain. To check, plug in the charging cable or put the mouse on a wireless charging pad and re-run v_min.py.
- SoC stayed exactly 56% for 25 minutes. Device-routed latency (7-28 ms, against 3.5-4 ms for receiver-answered requests) shows the request reaches the mouse, but I cannot prove the firmware updates SoC often; it may report in coarse steps.
- No physical or Windows-side ground truth for the percentage exists: G HUB is not installed and Windows exposes no battery property for LIGHTSPEED devices. Cross-validation rests on the kernel, LGSTrayBattery and Logitech's lamparray-service decoder all agreeing on report bytes 4/5/6.
- The vendor (logi_lamparray_service) swId is a runtime value, not a constant. If it ever equals the app's swId, the strict matcher could accept the service's reply as ours. Both would be valid battery reads, so the impact is low; keep the swId configurable.
- 0x1004 capability flag bits 2-3 and byte r2 are still undocumented, and Logitech's own service ignores them. A future firmware could give them meaning.
- Newer Logitech headsets may use the Centurion battery protocol (vendor centpp_feature_0104), which this spec does not cover.
- Wired mode (046D:C0A8, devIdx 0xFF) is not verified.
- Downloaded sources (Linux kernel, Solaar, LGSTrayBattery) and decompiled vendor code were treated as data only. None of them asked to run, install or send anything.

## Sources

- https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/base.py (frame sizes, request/ping matching, error handling, SOLAAR_SOFTWARE_ID=0x0B and the list of other tools' swIds)
- https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp20.py (get_name, get_ids, get_firmware, decipher_battery_status/voltage/unified, decipher_adc_measurement, estimate_battery_level_percentage)
- https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp10.py (read_register 0x81/0x83, battery registers 0x07/0x0D parsing)
- https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp10_constants.py (HID++1.0 error codes, notification flags, registers, InfoSubRegisters 0x03/0x2N/0x3N/0x4N, device kinds)
- https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp20_constants.py (HID++2.0 error codes, DEVICE_KIND)
- https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/receiver.py (pairing info parsing, max_devices, notify_devices)
- https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/base_usb.py (C54D listed as lightspeed receiver, usb_interface 2)
- https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/notifications.py (0x41 link flags, feature event handling incl. 0x1D4B)
- https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/common.py (BatteryStatus, Notification sub-ids)
- https://github.com/torvalds/linux/blob/master/drivers/hid/hid-logitech-hidpp.c (report IDs/lengths, HID++1.0/2.0 error codes, 0x1000/0x1001/0x1004/0x1F20 decoding and voltage curves)
- https://github.com/torvalds/linux/blob/master/drivers/hid/hid-logitech-dj.c (0x41 connection notification layout, protocol types 0x0C/0x0D/0x0F/0x11 = Lightspeed, C54D = recvr_type_gaming_hidpp_ls_1_3, kernel writes reg 0x00 = 0x000900)
- https://github.com/libratbag/libratbag/blob/master/src/hidpp20.c (HID++2.0 framing cross-check)
- https://lkml.iu.edu/hypermail/linux/kernel/2101.0/01436.html (Unified Battery 0x1004 kernel patch rationale)
- https://lekensteyn.nl/files/logitech/logitech_hidpp10_specification_for_Unifying_Receivers.pdf (fetched; text not extractable locally)
- Local: C:\WINDOWS\System32\DriverStore\FileRepository\logi_lamparray_usb.inf_amd64_dc165490f32791ad (logi_lamparray lower filter on USB\VID_046D&PID_C54D; service binary RTTI shows a HID++ stack with feature_1004/1001/1F20/1895/0003)
