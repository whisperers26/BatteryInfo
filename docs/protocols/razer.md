# Razer (Basilisk V3 Pro 35K): battery protocol

Status: NOT verified live (device absent); built from sources (2026-10-14, verdict `confirmed-with-corrections`).

```text
RAZER BASILISK V3 PRO 35K PHANTOM: BATTERY AND CHARGING READ SPEC (Windows, C#/.NET). Verified against openrazer, OpenRGB, razer-battery-report, the Synapse 4 JS and Synapse logs. Not re-read live: the device was absent on 2026-10-14.

1. DEVICE MATCHING
- USB VID 0x1532:
  - PID 0x00D6 = mouse on USB cable (never seen on this PC).
  - PID 0x00D7 = HyperSpeed dongle. It enumerates and answers even when the mouse is off.
  - PID 0x00D8 = Bluetooth LE mode. Hardware ID BTHLEDevice\{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&02068e_PID&00d8. In "02068e", 02 = USB-IF vendor-ID source and 068E = Razer's id. Also accept VID&01068E.
- Synapse manifest: productId 214, dongleId 215, bleId 216.
- Phantom Green, White and Black share these PIDs. Optional Get Edition Information = class 0x00, id 0x86, size 0x03. Edition = args[1]: 0 Green, 128 White, 129 Black. This unit returns 128.
- 00D7 interfaces:
  - MI_00 = single mouse TLC (UP 0x0001, U 0x0002) that carries the Razer feature report.
  - MI_01 = COL01 keyboard, COL02 mouse, COL03 consumer, COL04 system, COL05-08 vendor.
  - MI_02 = keyboard.
  - MI_03 and MI_04 = vendor.
- Razer driver package (oem125.inf = RzDevU_00d6_Mou.inf) on HID\Vid_1532&Pid_00d6&MI_00 and Pid_00d7&MI_00:
  - UpperFilters RzDev_00d6.
  - Devnode security D:P(A;;GA;;;SY)(A;;GRGWGX;;;BA)(A;;GRGWGX;;;WD)(A;;GRGWGX;;;RC), so Everyone can open the TLC.
  - It also creates RZCONTROL and RZVIRTUAL\...MI_00&COL03 children.

2. INTERFACE SELECTION / OPEN
- Enumerate GUID_DEVINTERFACE_HID {4D1E55B2-F16F-11CF-88CB-001111000030}. Select the TLC that meets all of:
  - HidD_GetAttributes VID 0x1532, PID 0x00D6 or 0x00D7.
  - Path contains "&mi_00" and does not start with RZVIRTUAL (no "&col").
  - HidP_GetCaps UsagePage 0x0001, Usage 0x0002, FeatureReportByteLength == 91.
  - Feature report ID 0.
  Match by caps, not by exact path. Path seen here: \\?\hid#vid_1532&pid_00d7&mi_00#8&2188b82e&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}.
- Never use the RZVIRTUAL children (...&mi_00&col03&col01/col02). Synapse got "[HID.SEND_FEATURE_REPORT] Incorrect function" on them.
- Open with CreateFileW(path, dwDesiredAccess = 0, FILE_SHARE_READ|FILE_SHARE_WRITE, NULL, OPEN_EXISTING, 0, NULL).
  - IOCTL_HID_SET_FEATURE and IOCTL_HID_GET_FEATURE are FILE_ANY_ACCESS, so a zero-access handle works.
  - Non-elevated access works only because of Razer's devnode security above. Without the Razer driver package, non-elevated opens of mouse TLCs return ERROR_ACCESS_DENIED (5) on this Windows 11 26200 machine (reproduced on the Logitech and ROG mouse TLCs).
  - On error 5, fall back to an elevated helper: a scheduled task with highest privileges, or a LocalSystem service exposing the value over a named pipe.
- I/O: HidD_SetFeature then HidD_GetFeature, each with a 91-byte buffer, buf[0] = 0x00 (report ID 0).
- USB-level equivalent (openrazer): SET_REPORT bmRequestType 0x21, bRequest 0x09, wValue 0x0300, wIndex 0, len 90; then GET_REPORT 0xA1/0x01, wValue 0x0300, wIndex 0, len 90.

3. REPORT LAYOUT
90-byte Razer report. [n] = index in the 91-byte Windows buffer; buffer[0] = report ID 0.
- off 0 [1] status. Host sends 0x00. Device returns: 0x00 new/unprocessed, 0x01 busy, 0x02 success, 0x03 failure, 0x04 timeout/no response (mouse asleep, off or out of range), 0x05 not supported.
- off 1 [2] transaction id, echoed by the device.
- off 2-3 [3-4] remaining packets, big-endian, 0x0000.
- off 4 [5] protocol type, 0x00.
- off 5 [6] data size.
- off 6 [7] command class.
- off 7 [8] command id. Bit 7 set = GET.
- off 8-87 [9-88] arguments[80].
- off 88 [89] CRC = XOR of report bytes 2..87 (buffer[3..88]). Status and txid are excluded. Validated against 706 Synapse frames.
- off 89 [90] reserved, 0x00.
- Accept a reply only when: txid == sent txid, class == sent class, id == sent id, size == sent size, and (for 07/xx) args[0] == 0.
- On status 0x02, also require a valid CRC. If CRC fails, poll again or resend.

4. COMMANDS (read-only; hard allow-list in the app: 07/80, 07/84, and 00/BF only on 00D7)
a) Get Battery Level: class 0x07, id 0x80, size 0x02, arg0 = battery id 0.
   - Request (91 bytes): 00 | 00 1F 00 00 00 02 07 80 | 80 x 00 | 85 00
   - Response: 02 1F 00 00 00 02 07 80, args[0] = 0, args[1] (buffer[10]) = raw 0..255.
   - Percent = (raw * 100) / 255 in integer arithmetic. This equals Synapse's Math.floor(raw/255*100) for every raw.
   - The device encodes integer percent p as raw = ceil(p*255/100), so rounding gives the same result.
   - Observed pairs: 217->85, 220->86, 222->87, 225->88, 227->89, 230->90, 233->91, 235->92, 238->93, 240->94, 243->95, 245->96, 248->97, 250->98, 253->99, 255->100.
b) Get Device Charging Status: class 0x07, id 0x84, size 0x02, arg0 = 0.
   - Request: 00 | 00 1F 00 00 00 02 07 84 | 80 x 00 | 81 00
   - Response args[1] (buffer[10]), Synapse enum:
     - 0 NoCharge_BatteryFull: not on external power; level can be anything.
     - 1 Charging.
     - 2 ChargingFailed.
     - 3 ForeignObjectDetection.
     - 4 FastCharging.
     - 5 FullCharging.
     - 6 ReachChargingLimit.
   - Only 0 and 1 have been observed on this unit.
   - Display: 1/4 = charging, 5/6 = on power (full/held), 2/3 = charging fault, 0 = on battery. Other tools treat any nonzero value as charging.
c) Optional, 00D7 only: Get Multiple Device Wireless Connection Status V2: class 0x00, id 0xBF, size 0x50.
   - Answered by the dongle itself.
   - Request: 00 | 00 1F 00 00 00 50 00 BF | 80 x 00 | EF 00
   - Response args: [count, then count x (status, pidHi, pidLo)]. Status 1 = linked, 0 = not linked; pid 0xFFFF = empty slot.
   - Seen: 02 01 00 D7 00 FF FF (linked) and 02 00 00 D7 00 FF FF (not linked).
   - Use it to report "mouse off/unlinked" without waiting for 07/xx timeouts.
- Send nothing else. Every id with bit 7 = 0 is a SET.

5. TRANSACTION ID
- Use 0x1F, as openrazer, OpenRGB and razer-battery-report do for 00D6/00D7. The device accepts any id.
- 0x1F is NOT unique on this PC. Synapse clients share the MI_00 mailbox, and at least one of them emits 0x1F: replies with txid 31 were logged twice on 00D7. The dual-link mouse class itself uses 0..30.
- Rely on the full match in §3. A foreign reply that matches txid, class, id, size and arg0 is the same query, so its data is equally valid.

6. TIMING / RETRIES
One in-process lock per device; optionally also OpenRGB's named mutex Global\RazerLinkReadWriteGuardMutex with a 500 ms timeout (Synapse does not use it).
- HidD_SetFeature, then wait 10-30 ms. References: openrazer 31 ms, Synapse battery process 25 ms, OpenRGB 2 ms, razer-battery-report 60 ms.
- Poll HidD_GetFeature every 10 ms, up to about 300 ms total.
  - No match (foreign txid/class/id): keep polling.
  - 0x00/0x01: keep polling. Do not resend while busy; receivers drop commands sent while they are relaying.
  - 0x02 + valid CRC: parse.
  - 0x05: unsupported; stop.
  - 0x03/0x04: resend up to 2 more times, about 150 ms apart. Still 0x04: state = "asleep/off/out of range"; keep the last good value and its timestamp.
- Synapse reference: battery process uses 25 ms out/in sleep, 10 gets, 20 resends; lighting engine uses 5 ms.
- Polling period: Synapse polls charging, then battery, every 150 s. Use 60-300 s.
- Re-query on device arrival (CM_Register_Notification on the HID interface GUID).

7. ASLEEP / OFF / CABLE
- Dongle present, mouse off or asleep: the dongle returns 0x04 for both 07/84 and 07/80. Status 0x04 was already present within 25 ms of the send (exact latency unknown). Synapse saw 20 of 20 resends return 0x04, then logged "Power off". 00/BF then shows link status 0.
- External power while on the dongle: on this PC, 07/84 returned 1 (Charging) through PID 215 with levels 87-100. 00D6 never enumerated here.
- If 00D6 ever appears, prefer it over 00D7, but this is unverified.

8. UNSOLICITED EVENTS (optional; transport to user mode unverified)
Synapse receives these 16-byte, report-ID-0x05 input reports from the dongle through its driver / mapping engine:
- 05 31 LL CC PP TT = battery change. LL = raw level (same scaling as 4a), CC = chargingStatus enum, PP = powerSourceStatus (0 = external power removed), TT = deviceType (1 = mouse).
  Examples: 05 31 F5 00 00 01 = 96%; 05 31 FF 01 01 01 = 100%, charging, on power.
- 05 09 SS 01 = wireless link: SS 2 = disconnect, 3 = connect.
- 05 0C VV = external power change (only VV = 0, removed, observed).
- 05 02 XH XL YH YL = DPI change.
- 05 39 MM = scroll mode.

9. BLUETOOTH (00D8)
- Do not use the 90-byte protocol. OpenRGB's BT detector is commented out. Synapse uses its BLE PROTOCOL30 GATT service (52401523-f97c-7f90-0e7f-6c6f4e36db1c) and skips CHECK_BATTERY_STATUS when useBle is set.
- Read the standard Battery Service instead:
  (a) PnP property {104EA319-6EE2-4701-BD47-8DDBF425BBE5},2 (Byte, 0-100) on the BTHLE device node or its children. It works for the classic-BT FreeClip here; unverified for BLE.
  (b) WinRT: BluetoothLEDevice.GetGattServicesForUuidAsync(0x180F) -> characteristic 0x2A19 -> ReadValueAsync(Uncached), 1 byte, 0-100.
- No charging state this way. Unverified for this model.

10. COEXISTENCE
- Synapse 4.0.590 (asInvoker, node-rz-hid / hidapi) and its lighting engine share the same MI_00 mailbox and interleave ("different transId" in its logs).
- The full reply match from §3 and polling without resending while busy make sharing it safe.
- Keep handles short-lived. Never stop Razer services, touch drivers, or change devnode security. Synapse itself is not required, but Razer's driver package is what makes non-elevated access possible.

11. C# OFFSETS (91-byte buffer)
- reportId = b[0] = 0
- status = b[1]; txid = b[2]; remaining = (b[3] << 8) | b[4]; proto = b[5]; size = b[6]; class = b[7]; id = b[8]
- args[i] = b[9 + i]; battery id = b[9]; value = b[10]
- crc = b[89] == XOR(b[3..88]); reserved = b[90]
```

## Remaining risks

- Nothing was confirmed on hardware. No VID_1532 or VID&02068E device was present at 12:26 or 12:47 on 2026-10-14, so not a single byte was exchanged. Still unproven live: MI_00 caps (UP 1/U 2, FeatureReportByteLength 91, report ID 0), response CRC validity, how many BUSY polls a live 07/80 takes through the dongle, and the actual 07/80 and 07/84 values.
- Non-elevated open of the Razer MI_00 TLC is an inference, not a test. It rests on the devnode descriptor installed by oem125.inf, the asInvoker manifest and Synapse's successful opens. A Synapse uninstall or driver cleanup removes that descriptor, and the app would then get ERROR_ACCESS_DENIED unless elevated. A Razer driver update could also change it.
- Another Synapse client on this PC also uses txid 0x1F (logged twice). A strict txid+class+id+size+arg0 match is mandatory. Heavy Synapse traffic, especially at startup, can also stretch the poll budget.
- Charging enum values 2-6 have never been observed. Wired mode (00D6) has never enumerated here, so the cable and charging behaviour is unverified.
- Bluetooth (00D8) is unverified. It is unknown whether Windows fills the BT battery DEVPKEY for this BLE mouse, and whether WinRT can read Battery Service 0x180F on a HOGP device Windows has claimed. No charging state is available over BLE. The vendor PROTOCOL30 GATT was not analysed and must not be written to.
- Which user-mode TLC (if any) carries the report-ID-0x05 notification input reports is unknown, so event-driven updates are not yet usable.
- Polling every 60-300 s is assumed harmless to the mouse's sleep and battery. Whether the dongle forwards each query over RF is unknown.
- Vendor data (Synapse JS bundles, lighting manifests, logs, INFs) was read only as data. I saw no prompt-injection text, nothing was downloaded or executed, and no device, driver or setting was changed.

## Sources

- https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.h (USB_DEVICE_ID_..._PHANTOM_GREEN_EDITION_WIRED 0x00D6 / _WIRELESS 0x00D7, RAZER_REPORT_LEN 0x5A, RAZER_NEW_MOUSE_RECEIVER_WAIT_US 31000)
- https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.c (razer_get_report index 0 + 31 ms wait for 00D6/00D7; charge_level/charge_status transaction_id 0x1F; razer_send_payload retry/status handling; returns arguments[1])
- https://github.com/openrazer/openrazer/blob/master/driver/razercommon.c and razercommon.h (SET_REPORT 0x21/0x09 wValue 0x300, GET_REPORT 0xA1/0x01, CRC bytes 2..87, struct razer_report, status codes 0x01-0x05)
- https://github.com/openrazer/openrazer/blob/master/driver/razerchromacommon.c (razer_chroma_misc_get_battery_level = 0x07/0x80 size 2; get_charging_status = 0x07/0x84 size 2)
- https://gitlab.com/CalcProgrammer1/OpenRGB/-/blob/master/Controllers/RazerController/RazerDevices.h (RAZER_BASILISK_V3_PRO_35K_PG_WIRED/WIRELESS/BLUETOOTH_PID 0x00D6/0x00D7/0x00D8, RAZER_BLUETOOTH_VID 0x068E)
- https://gitlab.com/CalcProgrammer1/OpenRGB/-/blob/master/Controllers/RazerController/RazerControllerDetect.cpp (interface 0, usage page 0x01, usage 0x02; BT detector commented out)
- https://gitlab.com/CalcProgrammer1/OpenRGB/-/blob/master/Controllers/RazerController/RazerController/RazerController.cpp (razer_usb_query: match txid/class/id, skip NEW/BUSY, resend; 2 ms waits)
- https://gitlab.com/CalcProgrammer1/OpenRGB/-/blob/master/Controllers/RazerController/RazerDevices.cpp (00D7 entry transaction id 0x1F)
- https://gitlab.com/CalcProgrammer1/OpenRGB/-/blob/master/Controllers/RazerController/RazerDeviceGuard.cpp (Global\RazerLinkReadWriteGuardMutex)
- https://github.com/xzeldon/razer-battery-report (src/controller.rs, src/devices.rs, src/manager.rs: Windows hidapi, interface 0 UP1/U2, report id 0, 91-byte feature, 60 ms wait, txid 0x1F for 00D6/00D7)
- https://github.com/spozer/razer-battery-checker (src/device_controller.py)
- https://github.com/Fan4Metal/razer_tray
- https://github.com/libusb/hidapi/blob/master/windows/hid.c (zero-access open fallback for mouse/keyboard collections)
- https://github.com/libusb/hidapi/discussions/403 (maintainer: Generic Desktop mouse/keyboard collections not directly accessible on Windows)
- https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/keyboard-and-mouse-hid-client-drivers (system opens keyboard and mouse collections for exclusive use)
- https://insider.razer.com/razer-support-45/razer-basilisk-v3-pro-not-charging-40862 (user report: Windows shows battery % for Basilisk V3 Pro in BT mode)
- https://github.com/openrazer/openrazer/issues/2122
- Local: C:\Program Files\Razer\RazerAppEngine\app-4.0.590\resources\app.asar (electron/UsbRzDeviceAction.js, electron/Protocol/protocol25.js + protocol25Const.js, electron/modules/noble/*, node_modules/node-rz-hid/build/Release/HID.node)
- Local Synapse web cache (Service Worker CacheStorage) of https://apps.razer.com/background-manager/assets/index-78aa1c14.js, rzDevice25DualLinkMouse-3cea4b9e.js, otfm-0754ec90.js, https://apps.razer.com/synapse/products/214/mw/main.fdc48eb54a035e8eecc8.js, 763.5c898b681d0d0507af62.js, manifest.json
- Local: C:\Program Files (x86)\Razer\PID00d6Drv\Drivers\x64\*.inf and PID00d8Drv\Drivers\x64\*.inf (hardware IDs for 00d6/00d7/00d8, RZVIRTUAL, BTHLE HID)
- Local: C:\Users\liubo\AppData\Local\Razer\RazerAppEngine\User Data\Logs\main.log and products_214_mw {EE48AE9F-CBA0-5933-9456-DC3B4FC45071}1-5.log (real request/response headers, timeouts, notifications)
