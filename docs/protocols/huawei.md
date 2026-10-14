# HUAWEI FreeClip 2 (SPP/RFCOMM): battery protocol

Status: verified live on this PC (2026-10-14, verdict `confirmed-with-corrections`).

```text
HUAWEI FreeClip 2: BATTERY SPEC (tested on this PC 2026-10-14; independently re-verified 12:18-12:22)

1. DEVICE MATCHING
- Classic BT address C0:DA:5E:A8:3E:C7 (ulong 0xC0DA5EA83EC7). Name "HUAWEI FreeClip 2". CoD 0x240418.
- DEVPKEY_Bluetooth_DeviceVIDSource = 1 (Bluetooth SIG). DEVPKEY_Bluetooth_DeviceVID = 0x027D (SIG company id = HUAWEI Technologies Co., Ltd.). DEVPKEY_Bluetooth_DevicePID = 0x4113. These are on the BTHENUM\DEV_<addr> node.
- PnP hardware/instance ID: BTHENUM\{<svc-uuid>}_VID&0001027D_PID&4113\<parent>&<addr>_C00000000.
  - The parent instance part varies: B&23FB693A for most nodes, B&129773BB for the COM4 node.
  - Match on the address suffix C0DA5EA83EC7, not on the parent prefix.
- Generic rule: VID 0x027D with VIDSource 1, or a name starting with "HUAWEI FreeBuds" / "HUAWEI FreeClip" / "HUAWEI FreeLace".
  - Every model in the OpenFreebuds map uses the same battery command.
  - The service/channel and T2 presence vary by model, and only FreeClip 2 was tested.
- AEP id: Bluetooth#Bluetoothac:f2:3c:db:34:60-c0:da:5e:a8:3e:c7. ContainerId {16A0C5CA-1487-5A24-9F43-F6BC5AE5E849}.

2. SERVICE / CHANNEL (SDP attr 0x0004, verified twice)
- 0x1101 "Serial Port": RFCOMM channel 1 (attr 0x0004 = 350C350319010035051900030801). USE THIS ONE.
- 0x111E HFP "Handsfree Unit": channel 29 (0x1D). Owned by Windows.
- 0xFD9A "Private COM": channel 2. 0xFD9A is a Huawei member UUID. Not used.
- 00101020-4C42-0065-636E-614465747942 "Private COM": channel 3. Not used.
- Resolve the service by UUID 0x1101; do not hardcode the channel. OpenFreebuds uses channel 1 for FreeClip, FreeClip 2 and Pro 3, but defaults to 16 for other models.
- The channel number is the byte after the last 0x08 in raw SDP attribute 0x0004. WinRT's ConnectionServiceName is an opaque string ("Bluetooth#Bluetooth<local>-<remote>#RFCOMM:00000000:{uuid}"), NOT the channel number.
- Windows also created COM4 ("Standard Serial over Bluetooth link") for this SPP UUID. A process that opens COM4 is expected to hold channel 1 (not tested).

3. GATING (do this before every connect)
- Connect only when the device is already connected. Check either:
  - WinRT: BluetoothDevice.FromBluetoothAddressAsync(addr).ConnectionStatus == Connected.
  - Win32: BluetoothGetDeviceInfo(IntPtr.Zero, ref BLUETOOTH_DEVICE_INFO).fConnected != 0.
    - The BOOLs are flag values, not 1: fConnected=32, fRemembered=16, fAuthenticated=8. Test for non-zero.
    - Struct size on x64 is 560: Address at offset 8; fConnected at offset 20.
- Connecting RFCOMM to a disconnected device pages it, which can wake the buds or pull them from the phone. Skip it and show "disconnected".

4. TRANSPORT (all proven here)
A) WinRT, TFM net10.0-windows10.0.19041.0, no NuGet:
  1. GetRfcommServicesForIdAsync(RfcommServiceId.SerialPort, BluetoothCacheMode.Cached), then take Services[0].
  2. new StreamSocket().ConnectAsync(svc.ConnectionHostName, svc.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication). Pass ConnectionServiceName as-is. Connect took about 200-260 ms.
  3. Write with DataWriter (StoreAsync).
  4. Read with DataReader, InputStreamOptions.Partial, LoadAsync(1024), with a CancellationToken timeout.
B) BCL only:
  1. new Socket((AddressFamily)32, SocketType.Stream, (ProtocolType)3), where 32 = AF_BTH and 3 = BTHPROTO_RFCOMM.
  2. Connect to a custom EndPoint whose AddressFamily is 32 and whose Serialize() returns a 30-byte SocketAddress (SOCKADDR_BTH, pack 1):
     - [0..1] family 32 (u16 LE)
     - [2..9] btAddr (u64 LE)
     - [10..25] serviceClassId (Guid.ToByteArray layout)
     - [26..29] port (u32 LE)
  3. port=0 with serviceClassId=00001101-0000-1000-8000-00805F9B34FB makes Windows do the SDP lookup (connect 137-140 ms).
  4. port=1 with Guid.Empty also works (Python/Winsock, 38-52 ms).
  5. Receive with ReceiveTimeout = 500 ms in a loop.
- An unpackaged Win32/.NET app needs no capability. MSIX would need DeviceCapability bluetooth.rfcomm.

5. FRAME FORMAT (same in both directions)
- [0] 0x5A (magic)
- [1..2] LEN, u16 big-endian = number of bytes from offset 3 up to (not including) the CRC = 1 + 2 + sum of (2 + len_i) over the TLVs.
- [3] 0x00 = unsliced.
  - Gadgetbridge also handles 0x01/0x02/0x03 (first/middle/last slice). Those are followed by one slice-index byte, which is counted in LEN.
  - Never seen from earbuds. Drop such frames.
- [4] service id
- [5] command id
- [6..] TLVs: type (1 byte), length, value.
  - The length is a Huawei VarInt (7 bits per byte, MSB = continuation, most-significant group first), per Gadgetbridge HuaweiTLV.
  - Every value seen is under 128 bytes, so the length is 1 byte in practice.
- [3+LEN .. 3+LEN+1] CRC-16/XMODEM, big-endian: poly 0x1021, init 0x0000, no reflection, xorout 0. It covers bytes [0 .. 3+LEN-1], magic included.
- Total frame length = LEN + 5.
- CRC check vectors (all verified):
  - 5A0009000108010002000300 gives FBB9
  - 5A0009000108010140030100 gives ED2E
  - 5A0014000108010140020310203003030001000402140A gives 1461
  - 5A00050001080100 gives F53D

6. BATTERY REQUEST (the only bytes ever sent)
- Service 0x01 (DeviceConfig), command 0x08 (BatteryLevel).
- Default (OpenFreebuds), empty TLVs 1, 2, 3: 5A 00 09 00 01 08 01 00 02 00 03 00 FB B9 (14 bytes).
- Also proven (Gadgetbridge), TLV 1 only: 5A 00 05 00 01 08 01 00 F5 3D (10 bytes).
- Both return the identical full response. The request TLV list does not filter the reply.
- No handshake or init is needed. Send immediately after connect, without waiting for the unsolicited preamble. Replies came 21-91 ms after TX.

7. BATTERY RESPONSE (svc 0x01, cmd 0x08; also the notification svc 0x01, cmd 0x27, assumed same TLVs)
- Observed bytes: 5A 00 1B 00 01 08 | 01 01 4B | 02 03 4C 4B 64 | 03 03 00 00 00 | 04 02 14 0A | 05 02 00 00 | 06 01 0A | AB 24 (32 bytes, LEN 0x1B).
- T1, len 1: device aggregate %, 0..100.
  - In every sample it equalled min(L, R), which was also R (R <= L each time), so it is unknown whether T1 is min or the right bud.
  - Re-checked 2026-10-14 13:27 through the provider: L 64 / R 63 / case 100 with T1 = 63, still both min(L, R) and R.
  - Show T2 values. Use T1 only when T2 is missing.
- T2, len 3: [left %, right %, case %], range 0..100. Observed 80/78/100 down to 76/75/100 over 20 min.
- T3, len 3: charging flags [left, right, case]; 1 = charging, 0 = not.
  - Gadgetbridge maps status[i]==1 to charging per index (0=L, 1=R, 2=case). OpenFreebuds uses "any byte == 1".
  - Only 0/0/0 has been seen on hardware.
- T4, len 2: [0x14, 0x0A] = 20, 10. Probably low/critical thresholds; unverified.
- T5, len 2: [0, 0]. Unknown.
- T6, len 1: [10]. Unknown.
- Single-battery (legacy) devices send only T1 and a 1-byte T3 (OpenFreebuds fixture 5A0009000108010140030100ED2E).
- Parse rules:
  - Parse by type; order is not guaranteed; ignore unknown TLVs.
  - Show L/R/case only if T2 has exactly 3 bytes; otherwise show T1 as one value.
  - Treat values above 100 as unknown.
  - If T2 is missing, use T1.
- Errors: Huawei replies may carry TLV 0x7F with a 4-byte big-endian code.
  - 0x000186A0 (100000) means success; any other value is an error.
  - Seen in Gadgetbridge watch code; never observed from these earbuds.

8. UNSOLICITED FRAMES (route by svc/cmd and ignore anything not 01/08 or 01/27)
- On every RFCOMM open, even with nothing sent:
  - 5A00030001063EBD (svc 01, cmd 06, no TLVs) about 50 ms after connect, then again about 120 ms after.
  - Then a 44-byte 0x2B/0xAC frame of 12 one-byte TLVs (types 1..11, 14) about 400 ms after:
    5A0027002BAC0101010201010301000401000501000601000701000801000901000A01000B01000E01003FFE
- If a request is sent right after connect, the battery reply can come before or between these frames.
- During idle: 0x2B/0x36 dual-connect events, e.g. 5A000C002B3605076034DB3CF2AC03D9A5.
  - TLV 5 = the PC's BT address (ac:f2:3c:db:34:60) byte-reversed, plus a state byte (03, then 09).
- There are no sequence numbers. Match the response by (svc, cmd) = (0x01, 0x08).
- The parser must resync: find 0x5A, wait for 3 header bytes, wait for LEN+5 bytes, check the CRC. On a bad CRC, drop 1 byte and rescan.

9. TIMING / RETRIES
- Read timeout: 1-3 s. Observed replies were 21-91 ms. OpenFreebuds uses 3 s × 5 attempts.
- On timeout, retry once, then fall back to HFP.
- Polling on one held-open link works: repeated queries 5-7 s apart were all answered, and an idle link stayed up 60 s+ with no keepalive.
- 0x0127 battery-change notifications were not observed: the battery did not change during the 60 s listen.
- Recommended: short-lived connect / query / close every 60-120 s while connected. Alternatively, hold the link and also handle 0x0127, accepting that this blocks other SPP clients.

10. COEXISTENCE
- Works while A2DP, AVRCP and HFP are all connected.
- Only one RFCOMM client per (device, channel) per PC. A second connect fails with WSAEADDRINUSE (10048) on Winsock; reproduced, and the first link kept working.
- If OpenFreebuds, Huawei PC software, or a program holding COM4 owns the channel, connect fails: show the HFP value and retry later.
- The phone's AI Life app uses its own ACL link; not tested.

11. WINDOWS FALLBACK: HFP aggregate battery (approximate)
- Node: BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_VID&0001027D_PID&4113\B&23FB693A&0&C0DA5EA83EC7_C00000000. Class System, "HUAWEI FreeClip 2 Hands-Free AG".
- Key: fmtid {104EA319-6EE2-4701-BD47-8DDBF425BBE5}, pid 2. DEVPROP_TYPE_BYTE (0x03), size 1, 0..100.
- Accuracy: it read 80 throughout, including after fresh headset reports at 12:19:36 and 12:22:34. Over the same period SPP said 80 falling to 75 (L76/R75 at the end).
- At 13:27 HFP read 70 while SPP said L 64 / R 63, so the overstatement grew to about 6 points. While one SPP link was held, a second connect failed with WSAEADDRINUSE (10048) and the provider's HFP fallback worked.
  - It rounds UP and can overstate by 5+ points.
  - Step size is unverified, since only 80 was seen. Likely +IPHONEACCEV 0..9 shown as (n+1)*10.
  - No L/R/case, no charging state. Show it as "~80%".
- pid 7 on the same fmtid is a FILETIME for the last headset report. It updates every ~3 min.
- The key is NOT present (Empty/null) on the AEP, the DeviceContainer, or the BTHENUM\DEV_ device node. It exists only on the 0x111E child.
- WinRT, any of these:
  - DeviceInformation.FindAllAsync("System.Devices.DeviceInstanceId:~~\"C0DA5EA83EC7\"", new[]{"{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2"}, DeviceInformationKind.Device). The value is a boxed byte.
  - All HFP nodes: AQS System.Devices.DeviceInstanceId:~<"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}" (escape the backslash in C#).
  - DeviceInformation.CreateFromIdAsync(instanceId, props, DeviceInformationKind.Device).
  - AQS "<key>:<>[]" throws "Full text search is not supported".
- Live updates: DeviceInformation.CreateWatcher(sameAqs, {key2, key7}, Kind.Device). Updated carries only the changed keys, so call info.Update(update) to merge.
- Win32: CM_Locate_DevNodeW(out devInst, instanceId, 0), then CM_Get_DevNode_PropertyW(devInst, ref DEVPROPKEY{fmtid, 2}, out type, buf, ref size, 0). Returns cr=0, type=0x3, size=1, data=0x50.

12. BLE / GATT
- Not usable. There is no paired LE endpoint for FreeClip 2; BluetoothLEDevice.FromBluetoothAddressAsync(0xC0DA5EA83EC7) returns null.

13. GEN 1 vs GEN 2 (OpenFreebuds)
- "HUAWEI FreeClip" maps to the FreeBuds Pro 3 driver. "HUAWEI FreeClip 2" has a dedicated OfbDriverHuaweiFreeClip2 with _spp_service_port = 1 (upstream verified).
- Both use SPP port 1 and the same battery handler: 0x0108 with TLVs 1, 2, 3, plus notify 0x0127.
- For battery, the two generations need identical code.
```

## Remaining risks

- Charging flags (T3) have never been non-zero on hardware: all readings were 0/0/0. The per-index mapping [L, R, case] rests only on Gadgetbridge's source. The user needs to dock a bud, or charge the case, while a link is held to confirm it.
- T1 semantics are unresolved: min(L,R) and 'right bud' fit every sample equally. The UI should not rely on T1 when T2 is present.
- The case level stayed at 100 throughout. It may be the last value cached while the case is closed or out of range. What T2 reports for a docked bud or for an out-of-range case (0, last value, or 0xFF) is untested.
- Behaviour when the buds are asleep, in the case, or out of range was not tested, deliberately, to avoid paging the device. The gate (fConnected / ConnectionStatus) must stay in place. Connect errors and timeouts for that case are unknown.
- The 0x0127 battery-change notification was never observed. Its format is assumed identical to 0x0108, based on Gadgetbridge (id_change 0x27, 'Same format') and OpenFreebuds.
- The HFP fallback overstates the level: it read 80 while SPP said 75, even after fresh headset reports. Its step size is unverified. Treat it as approximate only.
- The SPP channel is exclusive per PC (10048 reproduced). The app and OpenFreebuds, Huawei PC software, or a COM4 user block each other. Prefer short-lived connections.
- The 'generic Huawei' matching rule (other FreeBuds/FreeLace models) is untested. Channel and service UUID differ by model; OpenFreebuds defaults to channel 16. Only FreeClip 2 on channel 1 / UUID 0x1101 is proven.
- The 0x0106 and 0x2BAC frames sent on connect are not understood. Ignoring them caused no problems on this firmware, but future firmware might expect a reply.
- Never send other OpenFreebuds commands (0x2Bxx, 0x011F, 0x0125, ...). Many of them are writes. Only 0x0108 is known to be a pure read.
- Vendor and web content (OpenFreebuds, Gadgetbridge, Bluetooth SIG YAML) contained no instructions aimed at the agent. Nothing was installed or downloaded. The .NET build restored from an empty local feed and used the Windows SDK ref pack already in the NuGet cache.

## Sources

- https://github.com/melianmiko/OpenFreebuds (openfreebuds/driver/huawei/package.py, utils.py, handler/battery.py, driver/generic.py, driver/per_model/free_clip_2.py, test/test_battery.py, test/test_freeclip2.py, driver/constants.py, docs/devices/HUAWEI_FreeClip_2.md); copies in ...\re\huawei\src_ofb
- https://raw.githubusercontent.com/melianmiko/OpenFreebuds/main/openfreebuds/driver/huawei/driver/per_model/free_clip_2.py
- https://raw.githubusercontent.com/melianmiko/OpenFreebuds/main/openfreebuds/driver/huawei/test/test_battery.py
- https://codeberg.org/Freeyourgadget/Gadgetbridge (commit 75337cf73aa5e9e8d0d89c54256b41d422a886f0): devices/huawei/HuaweiPacket.java, HuaweiTLV.java, packets/DeviceConfig.java (BatteryLevel id 0x08, id_change 0x27), service/devices/huawei/HuaweiFreebudsSupport.java (SPP UUID 0x1101), requests/GetBatteryLevelRequest.java, AsynchronousResponse.java; copies in ...\re\huawei\src_gb
- https://gadgetbridge.org/gadgets/headphones/huawei/
- https://bitbucket.org/bluetooth-SIG/public/raw/main/assigned_numbers/uuids/member_uuids.yaml (0xFD9A = Huawei Technologies Co., Ltd.)
