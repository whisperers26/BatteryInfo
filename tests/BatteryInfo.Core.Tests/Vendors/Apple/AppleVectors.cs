namespace BatteryInfo.Core.Tests.Vendors.Apple;

/// <summary>Published Proximity Pairing frames (docs/protocols/apple.md) as WinRT hands them over: from the 0x07 type
/// byte, without the company id. Each says where it was captured and the values its source gives. "edited" is a real
/// capture with one byte changed, for a case no capture shows; "tail-zeroed" means the source zeroed bytes 11-26.
/// Charging is expected only where its level is known.</summary>
internal static class AppleVectors
{
    internal sealed record Earbuds(string Name, string Hex, int ModelId, int? Left, int? Right, int? Case,
        bool LeftCharging, bool RightCharging, bool CaseCharging);

    internal sealed record OneBattery(string Name, string Hex, int ModelId, int Level, bool Charging);

    internal sealed record Rejected(string Name, string Hex);

    public static readonly Earbuds[] EarbudFrames =
    [
        // real-tail-decrypted: iPhone (iOS 26.5) unified log, AirPods Pro firmware 6F21
        // https://github.com/mennovanveenendaal/mennovanveenendaal.github.io/blob/b11a8f6590c44c7e679441dad6fd10274427d4d2/_posts/2026/2026-07-11-AirPods%20and%20the%20Apple%20Unified%20Log.%20Part%202.%20Opening%20the%20Charging%20Case.md#L34 (audioaccessoryd decode, 'Adv Data')
        new("AppleLog_AirPodsPro_RightPodInCase_LeftUnknown",
            "07 19 01 0E 20 04 F9 99 31 00 00 00 DF FF 61 CA BF D7 70 FF 0B 00 10 B1 13 A9 E3", 0x200E, null, 90, 90, false, true, false),
        // edited: iPhone (iOS 26.5) unified log, AirPods Pro firmware 6F21
        // https://github.com/mennovanveenendaal/mennovanveenendaal.github.io/blob/b11a8f6590c44c7e679441dad6fd10274427d4d2/_posts/2026/2026-07-11-AirPods%20and%20the%20Apple%20Unified%20Log.%20Part%205.%20Media%20play%20event.md#L85
        new("AppleLog_AirPodsPro_UnequalPods_RightSends_Edited",
            "07 19 01 0E 20 0B A9 89 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00", 0x200E, 100, 90, 90, false, false, false),
        // real-tail-zeroed: ESP32 (ESPHome) scanner, owner's AirPods Pro 1
        // https://github.com/f-fritz/ha-airpods-bridge/blob/93e923352bdd91e6bc8cc519b15400b4e6fc2299/tests/decoder_test.cpp#L64-L70
        new("HaBridge_AirPodsPro_RightPodSends_UnequalAapConfirmed",
            "07 19 01 0E 20 13 A9 AA 02 00 09 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00", 0x200E, 100, 90, 100, true, false, false),
        // real-tail-zeroed: ESP32 (ESPHome) scanner, owner's AirPods Pro 1
        // https://github.com/f-fritz/ha-airpods-bridge/blob/93e923352bdd91e6bc8cc519b15400b4e6fc2299/tests/decoder_test.cpp#L64-L70
        new("HaBridge_AirPodsPro_LeftPodSends_UnequalAapConfirmed",
            "07 19 01 0E 20 73 9A 9A 33 00 09 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00", 0x200E, 100, 90, 100, true, false, false),
        // real: Android (capod), owner's AirPods Pro with known keys
        // https://github.com/d4rken-org/capod/blob/bca9a076e3d4b1eaddf87d30db6bce7e63f99ca1/app/src/test/java/eu/darken/capod/pods/core/apple/ble/devices/airpods/AirPodsProTest.kt#L214-L251
        new("Capod_AirPodsPro_DecryptTest_RightPodSends_Unequal",
            "07 19 01 0E 20 51 9A 98 33 00 04 0C 14 E0 EB 43 3F 4B 22 C0 A9 ED CB 33 E7 09 71", 0x200E, 90, 100, 80, false, true, false),
        // real: macOS/CoreBluetooth (AirBattery), AirPods Pro (2020 model)
        // https://github.com/majidsii/AirBattery/blob/c99a55b11403a6724da3a5e8e52e34d0a90d697e/crates/protocol-airpods/tests/parser.rs#L72-L95
        new("AirBattery_AirPodsPro_LeftRemoved_RightCharging",
            "07 19 01 0E 20 31 AA A2 01 00 04 46 FB 26 B8 D7 7A E0 4E 8C 3E A8 A3 3A 8A AB 9E", 0x200E, 100, 100, 20, false, true, false),
        // real-tail-zeroed: ESP32 (ESPHome) scanner, owner's AirPods Pro 1
        // https://github.com/f-fritz/ha-airpods-bridge/blob/93e923352bdd91e6bc8cc519b15400b4e6fc2299/tests/decoder_test.cpp#L72-L76
        new("HaBridge_AirPodsPro_CaseAndLeftCharging",
            "07 19 01 0E 20 13 AA EA 02 00 09 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00", 0x200E, 100, 100, 100, true, false, true),
        // edited: derived from HaBridge_AirPodsPro_CaseAndLeftCharging
        // https://github.com/f-fritz/ha-airpods-bridge/blob/93e923352bdd91e6bc8cc519b15400b4e6fc2299/tests/decoder_test.cpp#L72-L76
        new("Edited_AirPodsPro_CaseChargingOnly",
            "07 19 01 0E 20 13 AA CA 02 00 09 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00", 0x200E, 100, 100, 100, false, false, true),
        // real: Android 17 (Pixel 7), owner's AirPods 2, out of the case and connected
        // https://github.com/Ovalvoi/AndroPods/blob/5bb491ef6b2482d74d61ad499105e9b5a40b6f22/docs/TECHNICAL.md#L66-L76
        new("AndroPods_AirPods2_RightPodSends_Unequal",
            "07 19 01 0F 20 01 A9 8F 01 00 04 C0 D1 B9 25 94 25 F3 48 1F 56 89 E3 4E 40 39 EF", 0x200F, 100, 90, null, false, false, false),
        // real-tail-zeroed: Windows 11, WinRT watcher (AirPodsDesktop 0.4.1), a stranger's AirPods 2 on a train
        // https://github.com/SpriteOvO/AirPodsDesktop/issues/117 (log line 22:31:57.720)
        new("Apd117_AirPods2_Stranger_LeftPodSends_Windows",
            "07 19 01 0F 20 2B 43 8F 00 00 05 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00", 0x200F, 30, 40, null, false, false, false),
        // real: Android (capod), a stranger's AirPods 3
        // https://github.com/d4rken-org/capod/blob/bca9a076e3d4b1eaddf87d30db6bce7e63f99ca1/app/src/test/java/eu/darken/capod/pods/core/apple/ble/devices/airpods/AirPodsGen3Test.kt#L47-L67
        new("Capod_AirPods3_StrangerAtBusStop",
            "07 19 01 13 20 2B 88 8F 01 00 08 E2 0E 84 37 C5 98 16 D4 B7 37 ED 23 8B 08 EA A1", 0x2013, 80, 80, null, false, false, false),
        // real: Android (capod debug log from the #225 reporter)
        // https://github.com/d4rken-org/capod/issues/225#issuecomment-2367904957
        new("Capod225_AirPods4_BothInEar",
            "07 19 01 19 20 2B 33 8F 11 00 04 59 D4 57 20 0F 1C 13 38 B2 00 74 E9 DD 70 D7 A5", 0x2019, 30, 30, null, false, false, false),
        // real: Android (capod debug log from the #226 reporter)
        // https://github.com/d4rken-org/capod/issues/226#issuecomment-2367946440
        new("Capod226_AirPods4Anc_RightPodSends_Unequal",
            "07 19 01 1B 20 0B 9A 8F 10 00 04 43 DF EC 1D D3 F1 C3 F4 A1 9B 29 26 B9 E7 3A A0", 0x201B, 90, 100, null, false, false, false),
        // real: Android (capod 'core.log' posted by the owner of AirPods 5 with the wireless charging case)
        // https://github.com/user-attachments/files/32449307/core.log line 167 (posted in https://github.com/d4rken-org/capod/issues/713#issuecomment-5754734264)
        new("Capod713_AirPods5WirelessCase_LeftPodSends_InCaseUnequal",
            "07 19 01 30 20 25 9A B9 53 00 00 6D FD 2E D1 14 79 67 58 A8 D7 2C 26 2E B0 16 21", 0x2030, 100, 90, 90, true, true, false),
        // real: Android (capod 'core.log', AirPods 5 with the wireless charging case)
        // https://github.com/user-attachments/files/32449307/core.log line 220
        new("Capod713_AirPods5WirelessCase_RightWorn_LeftCharging",
            "07 19 01 30 20 13 A9 A9 13 00 04 46 84 C6 81 98 F3 9B 4D EA 95 8C CA 1D 6B E7 F5", 0x2030, 100, 90, 90, true, false, false),
        // real: Android (capod 'core.log' posted by the owner of AirPods 5 without the wireless charging case)
        // https://github.com/user-attachments/files/32382937/core.log line 27 (posted in https://github.com/d4rken-org/capod/issues/713#issuecomment-5730495175)
        new("Capod713_AirPods5_InCase_AllCharging",
            "07 19 01 36 20 15 99 F9 51 00 04 45 C5 C4 F9 1E 09 A6 66 64 52 15 DE FF 5E 1F 27", 0x2036, 90, 90, 90, true, true, true),
        // real: Android (capod log from the owner of the AirPods Pro 2)
        // https://github.com/d4rken-org/capod/issues/31#issuecomment-1256791084
        new("Capod31_AirPodsPro2_InCase_AllCharging",
            "07 19 01 14 20 55 88 F9 51 00 04 20 50 03 CA D5 C9 AC 0F FA 84 78 94 5A 4D DF F5", 0x2014, 80, 80, 90, true, true, true),
        // real: Android (capod log from the #164 reporter's AirPods Pro 2 USB-C)
        // https://github.com/d4rken-org/capod/blob/bca9a076e3d4b1eaddf87d30db6bce7e63f99ca1/app/src/test/java/eu/darken/capod/pods/core/apple/ble/devices/airpods/AirPodsPro2UsbcTest.kt#L49-L71
        new("Capod164_AirPodsPro2Usbc_RightInCaseCharging",
            "07 19 01 24 20 53 AA 98 32 00 05 49 0A B8 BF 8E 29 D8 70 12 0D A7 0C CE 77 56 00", 0x2024, 100, 100, 80, false, true, false),
        // real: macOS (CoreBluetooth), owner's AirPods Pro 3, connected, RSSI -44
        // https://github.com/suryacks/Earshot/blob/7277730d78f37aa3b37384986b20bf523275e697/docs/PROTOCOL.md#L41-L51
        new("Earshot_AirPodsPro3_BothInEar_macOS",
            "07 19 01 27 20 2B 99 8F 11 00 09 09 5A 5C FF 46 52 E7 00 00 00 76 1C B2 37 22 24", 0x2027, 90, 90, null, false, false, false),
        // real: Android (capod), transcribed by the capod author from the owner's screenshots
        // https://github.com/d4rken-org/capod/blob/bca9a076e3d4b1eaddf87d30db6bce7e63f99ca1/app/src/test/java/eu/darken/capod/pods/core/apple/ble/devices/beats/PowerBeatsProTest.kt#L42-L70
        new("Capod303_PowerbeatsPro_BothOutOfCase",
            "07 19 01 0B 20 21 AA 8F 02 44 24 6D 3E CD 38 A6 F9 6C 6A EC 95 65 AF 97 08 95 49", 0x200B, 100, 100, null, false, false, false),
        // real: Android (capod), Beats Fit Pro owner in capod #33
        // https://github.com/d4rken-org/capod/issues/33#issuecomment-1256235651
        new("Capod33_BeatsFitPro_RightUnknown",
            "07 19 01 12 20 20 FA 8F 01 11 24 9B 9B 23 52 60 5A 8C 32 1C A5 C2 81 51 82 AF C8", 0x2012, 100, null, null, false, false, false),
        // real: Android (capod), a stranger's device; model inferred from the PID
        // https://github.com/d4rken-org/capod/issues/31#issuecomment-1252168911
        new("Capod31_BeatsStudioBuds_SeenInTheWild",
            "07 19 01 11 20 2B 88 0F 00 11 05 77 7B 0E E5 B8 AD 03 57 2F FA AA 2C F5 65 0F 52", 0x2011, 80, 80, null, false, false, false),
    ];

    public static readonly OneBattery[] OneBatteryFrames =
    [
        // real: Android (OpenPods logcat), owner's AirPods Max
        // https://github.com/adolfintel/OpenPods/issues/124 ('Log of 54% battery level on iPhone' section)
        new("OpenPods124_AirPodsMax_54pct",
            "07 19 01 0A 20 02 05 80 04 0F 44 A7 60 9B F8 3C FD B1 D8 1C 61 EA 82 60 A3 2C 4E", 0x200A, 50, false),
        // real-tail-zeroed: Windows, WinRT BluetoothLEAdvertisementWatcher (Orbit), owner's AirPods Max USB-C, ScannableUndirected
        // https://github.com/NRanjan-17/Orbit/blob/dbbe2ea575138b9919582ef1883c4558c81da6a6/tests/Orbit.Tests/Fixtures/tagged-session-max-usbc.csv#L65
        new("Orbit_AirPodsMaxUsbc_37pct_Windows",
            "07 19 01 1F 20 01 03 80 02 12 C4 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00", 0x201F, 30, false),
        // real-tail-zeroed: Windows, WinRT watcher (Orbit), owner's AirPods Max USB-C plugged in by USB-C
        // https://github.com/NRanjan-17/Orbit/blob/dbbe2ea575138b9919582ef1883c4558c81da6a6/tests/Orbit.Tests/Fixtures/charging-session-max-usbc.csv#L2-L3
        new("Orbit_AirPodsMaxUsbc_Charging65pct_Windows",
            "07 19 01 1F 20 01 06 90 00 12 C4 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00", 0x201F, 60, true),
        // real: Android (capod), contributed with capod commit aeaf92d7e6 (2026-04-09); capture setup not stated
        // https://github.com/d4rken-org/capod/blob/bca9a076e3d4b1eaddf87d30db6bce7e63f99ca1/app/src/test/java/eu/darken/capod/pods/core/apple/ble/devices/airpods/AirPodsMax2Test.kt#L11-L31
        new("Capod_AirPodsMax2_Worn",
            "07 19 01 2D 20 2B F6 8F 03 14 05 E7 17 3C 7D 65 B8 F3 94 45 12 90 EB E3 24 D3 3E", 0x202D, 60, false),
        // real: Android (OpenPods logcat), owner's BeatsX
        // https://github.com/adolfintel/OpenPods/issues/105 (logcat 12:15:32.356)
        new("OpenPods105_BeatsX",
            "07 19 01 05 20 00 F5 0F 01 01 00 6D CE C0 04 22 0A 85 31 2D 82 6B 42 80 01 20 1A", 0x2005, 50, false),
        // real: Android (OpenPods), owner's Beats Flex
        // https://github.com/adolfintel/OpenPods/issues/105#issuecomment-852369639
        new("OpenPods105_BeatsFlex",
            "07 19 01 10 20 0A F4 8F 00 01 00 C4 71 9F 9C EF A2 E3 BA 66 FE 1D 45 9F C9 2F A0", 0x2010, 40, false),
        // real: Android (capod), user-supplied ('Thanks to @docgalaxyblock', capod commit 34bf2311b4)
        // https://github.com/d4rken-org/capod/blob/bca9a076e3d4b1eaddf87d30db6bce7e63f99ca1/app/src/test/java/eu/darken/capod/pods/core/apple/ble/devices/beats/PowerBeats4Test.kt#L12-L31
        new("Capod_Powerbeats4_PlayingMusic",
            "07 19 01 0D 20 00 02 80 01 00 05 07 C5 E1 9C BD 9A 05 0E AE 9E 56 53 2F F9 75 4A", 0x200D, 20, false),
    ];

    public static readonly Rejected[] RejectedFrames =
    [
        // real: Android (capod), AirPods 4 on connect
        // https://github.com/d4rken-org/capod/blob/bca9a076e3d4b1eaddf87d30db6bce7e63f99ca1/app/src/test/java/eu/darken/capod/pods/core/apple/ble/devices/airpods/AirPodsGen4Test.kt#L48-L58
        new("Reject_SubType07_IdentityAddressFrame",
            "07 19 07 19 20 35 DF DE 46 76 CE 66 5C F4 6E BA B6 AC 1B 69 4C A7 D8 09 BF 9A 04"),
        // edited: derived from Capod_AirPodsPro_DecryptTest_RightPodSends_Unequal
        // https://github.com/d4rken-org/capod/blob/bca9a076e3d4b1eaddf87d30db6bce7e63f99ca1/app/src/test/java/eu/darken/capod/pods/core/apple/ble/devices/airpods/AirPodsProTest.kt#L216
        new("Reject_SubType00_Len19_Edited",
            "07 19 00 0E 20 51 9A 98 33 00 04 0C 14 E0 EB 43 3F 4B 22 C0 A9 ED CB 33 E7 09 71"),
        // edited: derived from Capod_AirPodsPro_DecryptTest_RightPodSends_Unequal
        // https://github.com/d4rken-org/capod/blob/bca9a076e3d4b1eaddf87d30db6bce7e63f99ca1/app/src/test/java/eu/darken/capod/pods/core/apple/ble/devices/airpods/AirPodsProTest.kt#L216
        new("Reject_SubType06_Len19_Edited",
            "07 19 06 0E 20 51 9A 98 33 00 04 0C 14 E0 EB 43 3F 4B 22 C0 A9 ED CB 33 E7 09 71"),
        // real: Android 17 (Pixel 7), owner's AirPods 2 while connected
        // https://github.com/Ovalvoi/AndroPods/blob/5bb491ef6b2482d74d61ad499105e9b5a40b6f22/app/src/main/java/com/ovalvoi/andropods/ble/ProximityPayload.kt#L36-L48
        new("Reject_Len0F_SubType00_PublicAddressFrame",
            "07 0F 00 0F 20 D0 DA D7 0F 14 48 15 E2 E2 23 02 00"),
        // real: macOS (CoreBluetooth), an unknown nearby Apple device, RSSI -41
        // https://github.com/suryacks/Earshot/blob/7277730d78f37aa3b37384986b20bf523275e697/docs/PROTOCOL.md#L53-L54
        new("Reject_Len11_SubType06_Earshot",
            "07 11 06 29 20 0B 28 FF FF 51 0B 00 00 00 00 7D F2 02 00"),
        // real: a counterfeit 'AirPods Pro 2', user-supplied on the capod Discord
        // https://github.com/d4rken-org/capod/blob/bca9a076e3d4b1eaddf87d30db6bce7e63f99ca1/app/src/test/java/eu/darken/capod/pods/core/apple/ble/devices/misc/FakeAirPodsPro2Test.kt#L36-L60
        new("Reject_Len13_CloneUserSupplied",
            "07 13 01 14 20 75 AA 72 39 00 00 6F E4 E4 93 30 00 30 30 30 30"),
        // real-truncated: iPhone (iOS 26.5) unified log; the log formatter cut the AMfD after byte 6
        // https://github.com/mennovanveenendaal/mennovanveenendaal.github.io/blob/b11a8f6590c44c7e679441dad6fd10274427d4d2/_posts/2026/2026-07-11-AirPods%20and%20the%20Apple%20Unified%20Log.%20Part%205.%20Media%20play%20event.md#L85
        new("Reject_Truncated_AppleLogHead",
            "07 19 01 0E 20 0B A9"),
        // edited: derived from Capod_AirPodsPro_DecryptTest_RightPodSends_Unequal
        // https://github.com/d4rken-org/capod/blob/bca9a076e3d4b1eaddf87d30db6bce7e63f99ca1/app/src/test/java/eu/darken/capod/pods/core/apple/ble/devices/airpods/AirPodsProTest.kt#L216
        new("Reject_Truncated_26Bytes_Edited",
            "07 19 01 0E 20 51 9A 98 33 00 04 0C 14 E0 EB 43 3F 4B 22 C0 A9 ED CB 33 E7 09"),
        // real: theengs decoder test input (provenance not stated)
        // https://github.com/theengs/decoder/blob/08531d205d491457d9d8ccf4e0ecdfe90b60c513/tests/BLE/test_ble.cpp#L717
        new("Reject_FindMy_Type12_Len19",
            "12 19 00 3D 99 67 E0 D6 7B F5 56 17 93 90 43 E4 8F D6 76 21 44 DA 3E 35 16 03 00"),
        // real: theengs decoder test input, an iPhone (provenance not stated)
        // https://github.com/theengs/decoder/blob/08531d205d491457d9d8ccf4e0ecdfe90b60c513/tests/BLE/test_ble.cpp#L749
        new("Reject_NearbyInfo_Type10",
            "10 06 5B 1E 02 71 1C 91"),
        // real: theengs decoder test input (provenance not stated)
        // https://github.com/theengs/decoder/blob/08531d205d491457d9d8ccf4e0ecdfe90b60c513/tests/BLE/test_ble.cpp#L718
        new("Reject_HandoffPlusNearbyInfo_TwoTlvs",
            "0C 0E 00 A7 58 2C D6 4F FF 2F E8 30 46 C9 9F 5B 10 06 5A 19 E9 66 70 D8"),
        // real: captured on this PC on 2026-10-15 by a passive watcher, a nearby iPhone's Nearby Info
        new("LivePc_NearbyInfo_Type10",
            "10 06 0C 19 0B 3D 57 68"),
    ];

    public static TheoryData<string> EarbudNames => new(EarbudFrames.Select(f => f.Name));

    public static TheoryData<string> OneBatteryNames => new(OneBatteryFrames.Select(f => f.Name));

    public static TheoryData<string> RejectedNames => new(RejectedFrames.Select(f => f.Name));

    public static byte[] Bytes(string hex) => Convert.FromHexString(hex.Replace(" ", "", StringComparison.Ordinal));
}
