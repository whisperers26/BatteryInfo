# Betop / Beitong KP70C2: battery protocol

Status: verified live on this PC (2026-10-14, verdict `confirmed-with-corrections`).

```text
BETOP KP70C2 (and gen-4 Betop "KP" family) BATTERY READ - IMPLEMENTATION SPEC (independently verified 2026-10-14)

0. Summary
- Battery is read over the vendor HID collection of the dongle (or of the wired pad), but ONLY while it enumerates with VID 0x20BC.
- The PC writes the 64-byte output report [02 15 00 x62]. The device answers with a 64-byte input report that starts 02 15.
- buf[2] = battery percent (0-100). (buf[4] & 0x0F)==1 means charging.
- No handshake or mode switch is needed. BetopGame sends the same 02 15 every 500 ms, regardless of its sync state.
- Verified on hardware in two independent sessions (12:12-12:15, and 12:22 with a separate probe written from the spec): byte-identical replies, 42-70 ms latency, no re-enumeration caused, XInput unaffected.
- When the pad auto-sleeps or is switched off, the dongle re-enumerates to VID 0x20DD (observed). That is the "controller off" state. Never write to it.

1. Device matching
- ONLINE, may be queried:
  - VID 0x20BC, PID 0x518D: dongle with the pad linked. Product "BTP-KP70C2 XINPUT DONGLE", manufacturer "BEITONG", bcdDevice 0x0100.
  - VID 0x20BC, PID 0x518C: pad on a USB cable. Bus-reported name "BTP-KP70C2 XINPUT WIRED". Seen in PnP history on 2026-09-11; not probed.
- OFFLINE, never write: VID 0x20DD, PID 0x518D, "BTP-KP70C2 DONGLE" = dongle present, pad asleep/off/not linked.
  - Observed 2026-10-14: 20BC:518D was removed and 20DD:518D arrived on the same port instance (USB\...\9&14BCABE1&0&4) at 12:23:35, about 21.7 min after the pad was powered on. The vendor default config_function_index7.timeout_sleep is 15 (minutes of no input).
  - It is also present from boot until the pad is switched on. Swaps were also seen on 9/11 and 10/4.
  - BetopGame also uses 20DD:518D as its firmware-update target: RegisterAlterDev only when config ForceFWUpdate=1, and the native updater re-enumerates 0x20dd after command 0x14.
  - Report "controller off/asleep". Do not open it for writing.
- SAFETY GATE: 20DD:518D exposes an IDENTICAL vendor collection (UP 0x00FF, U 0x0003, In 64, Out 64, Feature 0, report ID 2 in/out, bcd 0x0100). Caps cannot tell them apart, so the VendorID==0x20BC check is mandatory before any write.
- Interface selection:
  - Enumerate with SetupDiGetClassDevs(HidD_GetHidGuid, DIGCF_PRESENT|DIGCF_DEVICEINTERFACE).
  - Open each candidate with access 0 and FILE_SHARE_READ|FILE_SHARE_WRITE, then call HidD_GetAttributes and HidP_GetCaps.
  - Select the interface where ALL of these hold: VendorID==0x20BC; ProductID in the table; Caps.UsagePage==0x00FF (NOT 0xFF00); Caps.Usage==0x0003; InputReportByteLength==64; OutputReportByteLength==64.
  - On this machine: \\?\hid#vid_20bc&pid_518d&mi_01&col01#b&accf143&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}.
  - Match on caps plus VID, never on the COLxx index.
  - Other collections on the same device, to be ignored: COL02 mouse (cannot be opened), COL03 keyboard (UP 1/U 6, In 27, Out 2), IG_00 XInput HID (UP 1/U 5, In 15).
  - BetopGame registers the same way: RegisterDev2/RegisterAlterDev(0x20bc, PID, usagePage 0xff, usage 0x3, -1, len 64).
- Optional sibling table: same report ID 2 and the same 0x15 builder and parser, taken from BetopGame JS. Only 518C/518D were tested. Format is wiredPID/donglePID, VID 0x20BC.
  - SetHidWriteEnc 0 (plain bytes):
    - KP20D 5158/5159
    - KP20DL 516F/5170
    - KP20EB 5190/5191
    - KP40D 515B/515C
    - KP40MCF 5165/5166
    - KP40MCK 5163/5164
    - KP50C 5149/514A
    - KP50D 5150/5151
    - KP50E 5152/5153
    - KP50B 5133/5134
    - KP70A 512F/5130
    - KP70A1 516D/516E
    - KP70B 515F/5160
    - KP70C 5173/5174
    - KP70C1 518A/518B
    - KP70C2 518C/518D
    - PG01A 5161/5162
  - SetHidWriteEnc 1 (outgoing bytes are substituted through a 256-byte table). Out of scope, skip:
    - KP20A 5126/5128
    - KP40A 5125/5127
    - KP40B (also "KP40A" in kp40v1r.js) 5145/5146
    - KP40D-b1 5154/5155
    - KP40F 515D/515E and 5169/516A
  - Caveats:
    - For many siblings, e.g. KP50C, BetopGame also registers 20DD:wiredPID as a normal device, so the 20DD rule above is KP70C2-specific.
    - The KP50 family also registers 0517:5350 (UP FF00/U1) as its firmware-update device. Never touch it.
    - kp20/kp40/kp401/kp40v1r/kp30 use the whole byte 4 as charge_state, with no mask.

2. Opening
- Call CreateFileW(path, GENERIC_READ|GENERIC_WRITE, FILE_SHARE_READ|FILE_SHARE_WRITE, NULL, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, NULL).
- Shared access works and does not disturb xusb22/XInput on MI_00.
- Verified I/O pattern: overlapped WriteFile/ReadFile, an event, WaitForSingleObject(timeout), then CancelIoEx + GetOverlappedResult(wait=TRUE) on timeout.
- C# options:
  - P/Invoke the verified pattern above.
  - Or use FileStream(SafeFileHandle, FileAccess.ReadWrite, bufferSize: 0 or 1 (unbuffered), isAsync: true). isAsync must match FILE_FLAG_OVERLAPPED. Untested.
- Call HidD_FlushQueue(handle) before each request to drop stale input reports.
- Close the handle after each poll, or keep one reader thread.

3. Request (battery/status) = getBaseInfo
- WriteFile exactly 64 bytes (OutputReportByteLength): byte[0]=0x02 (report ID), byte[1]=0x15, byte[2..63]=0x00.
- Header byte: low nibble = command group. For group 5 (status), the high nibble is the sub-command: 1 = status/battery, 2 = key event, 3/4 = test data, 5 = fw info.
- Group 2 (profile) is different: bits 4-5 = get(1)/set(2), bits 6-7 = type.
- BetopGame sends "0215" through hidapi, which pads it to OutputReportByteLength, so the wire bytes are identical.

4. Response
Read 64-byte input reports and accept the first one where buf[0]==0x02 && buf[1]==0x15. Ignore everything else; while BetopGame is running you will also see 0x25 key replies, 0x11/0x21 sync replies, and so on.
- buf[2] = battery level in percent, 0..100. Evidence: the vendor UI renders "电量：" + parseInt(bat_level) + "%", and its icon is 25*ceil(N/25) in {25, 50, 75, 100}; 0 or values above 100 get no icon. Clamp values above 100 to 100.
- buf[4] & 0x0F = charge_state. 1 = charging ("充电中"), 0 = not charging. No other values have been observed. The high nibble is undocumented; 0x5 was observed.
- buf[7..8] = little-endian faceplate raw value. faceplate id = floor((v+175)/350)+1.
- buf[9..10] = little-endian faceplate mask. Faceplate id is active if mask & (1<<(id-1)).
- Bytes 3, 5, 6 and 11-15 are unknown.
- Observed report, three times at 12:12, 12:15 and 12:22:
  02 15 5B 00 50 01 00 50 01 02 00 FF FF 0B E8 E7 00..00
  Decoded: 91%, not charging, panel 336 -> 2, mask 0x0002.

5. Optional reads (pure GETs, verified)
- [02 55 00..]: reply 02 55.
  - buf[2].buf[3].buf[4] = firmware of the attached device; here the dongle, 1.0.3.
  - buf[5] = fw_conn, observed as 1 with the pad linked. BetopGame calls resetConn when fw_conn becomes 0.
  - BetopGame sends 0x55 only on (re)connect but reacts to fw_conn changes. Unsolicited 02 55 frames on link changes are therefore possible; tolerate them.
  - The reply takes about 11-12 ms, so it is answered locally by the dongle.
- [02 18 00..]: reply 02 18.
  - buf[2..4] = remote (pad) firmware, 1.0.3.
  - Bytes 5..8 = 01 BB CA E7, meaning unknown.
  - The reply takes 44-54 ms. Together with 0x15 (42-70 ms), this indicates the query is relayed to the pad, so the battery value is live rather than cached by the dongle (inference).
- Little endian throughout (PROT_BE=0).

6. Timing, retries and errors
- Use a 500 ms read timeout and up to 2 retries.
- Keep at least 16 ms between writes (BetopGame SetHidWriteDelay 16).
- Poll every 30-60 s.
- Primary "asleep/off" signal: there is no VID 0x20BC vendor collection and 20DD:518D is present. When the pad sleeps, the dongle drops 20BC entirely, so open handles die. Expect ERROR_DEVICE_NOT_CONNECTED (1167), ERROR_INVALID_HANDLE (6) or ERROR_OPERATION_ABORTED (995). Close the handle and re-enumerate.
- Register CM_Register_Notification / RegisterDeviceNotification for HID interface arrival and removal to refresh.
- A 20BC device that times out after the retries means the link is in transition: report "unknown/not linked". Optionally send 02 55 and check fw_conn==0.
- No unsolicited reports arrive while idle: 0 in 400 ms (original run), 0 in 3x300 ms (verifier).
- XInput fallback is useless. While online it reports BatteryType WIRED / BatteryLevel FULL and Flags 0. While the pad sleeps, all slots return 1167.

7. Never send
All of these share report ID 2:
- 0x11 syncMode: sets mode/profile and app channel.
- 0x21 Ping/sync: carries app-channel bits.
- 0x31 exitMode.
- 0x12/0x52/0x92 profile reads: harmless but unneeded.
- 0x22/0x62/0xA2 profile WRITE.
- 0x13 saveAppConfig.
- 0x23 reloadAppConfig.
- 0x33 setRestoreFW (factory restore).
- 0x14 ENTER FIRMWARE UPDATE [02 14 01 00 00 00 TT]: the dongle reboots to 20DD.
- 0x16 vibrate.
- 0x36 setFuncReset.
- 0x17 enterTestMode.
- 0x27 and 0x37 MAC get/set: GET and SET use the same header; an all-zero payload is GET.
- 0x25 getKeyEvent: unneeded.
- 0x35/0x45 getTestData/getTestDataExt.
- Never write anything to VID 0x20DD or to 0x0517:0x5350.
- Hard-whitelist 0x15, plus optionally 0x55 and 0x18, and gate every write on VID==0x20BC.

8. Coexistence with BetopGame
- Windows copies every input report to all open handles. When BetopGame runs at the same time, filter by the header byte.
- Our 0x15 queries are the same ones BetopGame sends.
- BetopGame enters its app mode/profile (0x21/0x11) while open and restores it on exit (0x31). Our app must never do either.
```

## Remaining risks

- After my last probe read the pad was asleep: the dongle has been 20DD:518D since 12:23:35. A wake-up (pressing the Home button) could not be tested, so 20DD->20BC re-arrival and the first 0x15 after wake were not re-observed in this session. The original run inferred 20DD->20BC re-arrival on power-on at 12:01:54 from PnP timestamps.
- The 20BC->20DD switch was 82 s after my last (read-only) query. Timing, plus identical earlier queries that caused no switch, point to idle auto-sleep (default timeout_sleep 15 min) or a manual power-off, but the cause cannot be proven without watching the pad.
- Charging was not exercised. charge_state==1 comes from the UI code only. Other values (e.g. 'full') and wired mode (20BC:518C) are untested on hardware.
- Battery scaling is supported only by the vendor UI (shows N% directly) and a single observed value of 91. The full range (0-100 vs. coarse steps, behaviour near empty) was not observed.
- Whether 20DD:518D also serves as a bootloader is unresolved: it has the same product family name and bcd 0x0100 as the normal dongle, but BetopGame's updater targets it. Treat it as untouchable.
- The dongle may push unsolicited 02 55 frames (fw_conn changes) on link transitions, since BetopGame relies on fw_conn changes but polls 0x55 only once. Not observed.
- C# FileStream over a HID handle is untested. Only raw overlapped P/Invoke was verified.
- Sibling models are unverified on hardware. Several of them treat 20DD:wiredPID as a normal operating device, and the SetHidWriteEnc=1 models obfuscate outgoing bytes.
- The original probe script betop_battery_probe.py has no VID gate. Running it now, with the dongle in 20DD mode, would write to the 20DD device. Do not reuse it unmodified.
- No public open-source implementation of the 0x15 battery command exists. The second sources are the vendor JS (read independently), the LKML naming of 20bc:5133 as BTP-KP50B, PnP history, and XInput. The physical LED state was not observed.
- All vendor JS/asar content and web results were treated as data. No prompt-injection content was encountered.

## Sources

- C:\betopgame\d1n0Ib34\resources\app\20260909.asar -> kp70c2.js (extracted to C:\Users\liubo\AppData\Local\Temp\claude\D--PersonalProjects-BatteryInfo\bc52183e-49c2-40b8-8ad6-b36f8af15f12\scratchpad\re\betop\asar\20260909\kp70c2.js): APP_DEF/model, PIDs, getBaseInfo/getFWInfo/getRemoteInfo, _datacb parser, initJoypad RegisterDev2/RegisterAlterDev, syncConfigTimer, exitMode, MAC get/set
- ...\asar\20260909\joyconn.js: hextoChar/hextoShort (little-endian, PROT_BE=0)
- ...\asar\20260909\third\gen4\static\js\app.0ca8d140.js: battery UI (quantity=bat_level %, transitionQuantity ceil(N/25), 1==chargeState -> charging)
- ...\asar\20260909\third\gen4\gen3.js: window.ForceFWUpdate from config
- ...\asar\20260909\kp20*.js, kp40*.js, kp50*.js, kp70*.js, pgv1.js: sibling PID table, CONFIG_REPORT_ID 2, SetHidWriteEnc values, identical 0x15 parser
- C:\betopgame\d1n0Ib34\resources\app\20260909.node (Ghidra via REA): JoypadConnHelper dispatcher, embedded hidapi, firmware-update routine FUN_1007f7d0 (0x14 command, hid_enumerate(0x20dd)), write-substitution table at 0x102a4f40
- C:\betopgame\d1n0Ib34\resources\app\main.js and 20260909.asar launcher.js (loader: requires <ver>.asar/launcher.js and native <ver>.node)
- Windows PnP properties (Get-PnpDevice / Get-PnpDeviceProperty: BusReportedDeviceDesc, LastArrivalDate/LastRemovalDate) and Kernel-PnP/Configuration event log
- Live probe logs: C:\Users\liubo\AppData\Local\Temp\claude\D--PersonalProjects-BatteryInfo\bc52183e-49c2-40b8-8ad6-b36f8af15f12\scratchpad\re\betop\probe\probe_log_20261014_121229.txt (+ _121526/_121527/_121528)
- No web sources used: no public documentation of this Betop protocol was needed, because all of it came from the shipped app and the hardware
