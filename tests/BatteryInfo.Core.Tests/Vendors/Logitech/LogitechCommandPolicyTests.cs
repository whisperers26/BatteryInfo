using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Tests.Fakes;
using BatteryInfo.Core.Vendors.Logitech;

namespace BatteryInfo.Core.Tests.Vendors.Logitech;

public class LogitechCommandPolicyTests
{
    private const string ReceiverParent = @"USB\VID_046D&PID_C54D&MI_02\B&18153BD2&0&0002";

    private readonly LogitechCommandPolicy _policy = new();
    private readonly HidDeviceInfo _short;
    private readonly HidDeviceInfo _long;
    private readonly HidDeviceInfo _otherReceiverShort;

    public LogitechCommandPolicyTests()
    {
        var backend = new FakeHidBackend();
        _short = backend.Add(0x046D, 0xC54D, 0xFF00, 0x0001, 7, 7, mi: 2, parent: ReceiverParent).Info;
        _long = backend.Add(0x046D, 0xC54D, 0xFF00, 0x0002, 20, 20, mi: 2, parent: ReceiverParent).Info;
        _otherReceiverShort = backend.Add(0x046D, 0xC54D, 0xFF00, 0x0001, 7, 7, mi: 2, parent: @"USB\VID_046D&PID_C54D&MI_02\B&99999999&0&0003").Info;
    }

    private static byte[] Bytes(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    private static byte[] Long(string hex)
    {
        var report = new byte[HidppProtocol.LongLength];
        Bytes(hex).CopyTo(report, 0);
        return report;
    }

    [Theory]
    [InlineData("10 FF 83 B5 03 00 00")] // receiver info
    [InlineData("10 FF 83 B5 20 00 00")] // pairing info slot 1
    [InlineData("10 FF 83 B5 25 00 00")] // pairing info slot 6
    [InlineData("10 FF 83 B5 30 00 00")] // extended pairing slot 1
    [InlineData("10 FF 83 B5 40 00 00")] // codename slot 1
    [InlineData("10 FF 81 00 00 00 00")] // notification flags (read)
    [InlineData("10 FF 81 02 00 00 00")] // connection state
    [InlineData("10 FF 81 F1 01 00 00")] // receiver firmware
    [InlineData("10 01 81 0D 00 00 00")] // HID++ 1.0 battery charge
    [InlineData("10 01 81 07 00 00 00")] // HID++ 1.0 battery status
    public void Receiver_and_device_register_reads_are_allowed(string hex)
    {
        Assert.True(_policy.IsAllowed(_short, Bytes(hex)));
    }

    [Theory]
    [InlineData("10 FF 80 00 00 09 00")] // SET notification flags (Solaar, kernel)
    [InlineData("10 FF 80 02 02 00 00")] // force arrival announce (LGSTrayBattery)
    [InlineData("10 FF 82 B5 03 00 00")] // SET_LONG_REGISTER
    [InlineData("10 FF 80 B2 01 00 00")] // pairing lock open
    [InlineData("10 FF 83 B2 00 00 00")] // a long register other than B5
    [InlineData("10 FF 83 B5 50 00 00")] // B5 sub-register outside the allow-list
    [InlineData("10 FF 81 B2 00 00 00")] // a short register outside the allow-list
    [InlineData("10 01 81 00 00 00 00")] // a receiver register addressed to a device
    [InlineData("10 01 83 B5 03 00 00")] // a long register read addressed to a device
    [InlineData("10 FF 8F 00 1C 09 00")] // an error frame
    public void Register_writes_and_unknown_registers_are_refused(string hex)
    {
        Assert.False(_policy.IsAllowed(_short, Bytes(hex)));
    }

    [Theory]
    [InlineData("10 01 00 0C 10 04 00")] // IRoot getFeature(0x1004)
    [InlineData("10 01 00 1C 00 00 A2")] // IRoot ping
    [InlineData("10 FF 00 1C 00 00 A2")] // ping of a device on its own cable
    public void Iroot_reads_are_allowed_without_any_lookup(string hex)
    {
        Assert.True(_policy.IsAllowed(_short, Bytes(hex)));
    }

    [Theory]
    [InlineData("10 01 00 2C 00 00 00")] // IRoot fn2
    [InlineData("10 01 00 10 00 00 A2")] // software id 0 marks notifications
    [InlineData("10 00 00 1C 00 00 A2")] // device index 0
    [InlineData("10 07 00 1C 00 00 A2")] // device index 7
    public void Other_iroot_frames_are_refused(string hex)
    {
        Assert.False(_policy.IsAllowed(_short, Bytes(hex)));
    }

    [Fact]
    public void A_feature_index_nobody_resolved_is_refused()
    {
        Assert.False(_policy.IsAllowed(_short, Bytes("10 01 06 1C 00 00 00")));
    }

    [Fact]
    public void Resolved_unified_battery_allows_get_capabilities_and_get_status_only()
    {
        _policy.RegisterFeature(_short, 0x01, 0x06, HidppFeature.UnifiedBattery);

        Assert.True(_policy.IsAllowed(_short, Bytes("10 01 06 0C 00 00 00")));
        Assert.True(_policy.IsAllowed(_short, Bytes("10 01 06 1C 00 00 00")));
        Assert.False(_policy.IsAllowed(_short, Bytes("10 01 06 2C 00 00 00")));
        Assert.False(_policy.IsAllowed(_short, Bytes("10 01 06 3C 00 00 00")));
    }

    [Fact]
    public void Registration_covers_both_collections_of_the_receiver_and_long_frames()
    {
        _policy.RegisterFeature(_short, 0x01, 0x06, HidppFeature.UnifiedBattery);

        Assert.True(_policy.IsAllowed(_long, Long("11 01 06 1C")));
    }

    [Fact]
    public void Registration_is_per_device_index_and_per_receiver()
    {
        _policy.RegisterFeature(_short, 0x01, 0x06, HidppFeature.UnifiedBattery);

        Assert.False(_policy.IsAllowed(_short, Bytes("10 02 06 1C 00 00 00")));
        Assert.False(_policy.IsAllowed(_otherReceiverShort, Bytes("10 01 06 1C 00 00 00")));
    }

    [Fact]
    public void Forgetting_a_device_refuses_its_features_again()
    {
        _policy.RegisterFeature(_short, 0x01, 0x06, HidppFeature.UnifiedBattery);

        _policy.ForgetDevice(_long, 0x01);

        Assert.False(_policy.IsAllowed(_short, Bytes("10 01 06 1C 00 00 00")));
    }

    [Theory]
    [InlineData(HidppFeature.FeatureSet, new[] { 0, 1 })]
    [InlineData(HidppFeature.DeviceFwInfo, new[] { 0, 1 })]
    [InlineData(HidppFeature.DeviceName, new[] { 0, 1, 2 })]
    [InlineData(HidppFeature.BatteryStatus, new[] { 0, 1 })]
    [InlineData(HidppFeature.BatteryVoltage, new[] { 0 })]
    [InlineData(HidppFeature.UnifiedBattery, new[] { 0, 1 })]
    [InlineData(HidppFeature.AdcMeasurement, new[] { 0 })]
    [InlineData(HidppFeature.WirelessDeviceStatus, new int[0])]
    [InlineData((ushort)0x1814, new int[0])] // CHANGE_HOST: never resolved, but if it were, nothing is allowed
    public void Each_feature_allows_exactly_its_read_functions(ushort featureId, int[] allowed)
    {
        _policy.RegisterFeature(_short, 0x01, 0x09, featureId);

        for (var function = 0; function < 16; function++)
        {
            var frame = HidppProtocol.FeatureRequest(0x01, 0x09, function, 0x0C);
            Assert.Equal(allowed.Contains(function), _policy.IsAllowed(_short, frame));
        }
    }

    [Fact]
    public void Frames_for_other_vendors_or_collections_are_refused()
    {
        var backend = new FakeHidBackend();
        var razer = backend.Add(0x1532, 0x00D7, 0xFF00, 0x0001, 7, 7).Info;
        var mouseCollection = backend.Add(0x046D, 0xC54D, 0x0001, 0x0002, 7, 7, parent: ReceiverParent).Info;

        Assert.False(_policy.IsAllowed(razer, Bytes("10 FF 83 B5 03 00 00")));
        Assert.False(_policy.IsAllowed(mouseCollection, Bytes("10 FF 83 B5 03 00 00")));
    }

    [Theory]
    [InlineData("11 FF 83 B5 03 00 00")] // long report id on a 7-byte frame
    [InlineData("10 FF 83 B5 03 00")]    // truncated
    [InlineData("")]
    public void Malformed_frames_are_refused(string hex)
    {
        Assert.False(_policy.IsAllowed(_short, Bytes(hex)));
    }

    [Fact]
    public void Register_reads_must_be_short_frames()
    {
        Assert.False(_policy.IsAllowed(_long, Long("11 FF 83 B5 03")));
    }

    [Fact]
    public void Feature_index_0_cannot_be_registered()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _policy.RegisterFeature(_short, 0x01, 0x00, HidppFeature.UnifiedBattery));
    }

    [Fact]
    public void An_index_cannot_silently_change_feature()
    {
        _policy.RegisterFeature(_short, 0x01, 0x06, HidppFeature.UnifiedBattery);

        Assert.Throws<InvalidOperationException>(() => _policy.RegisterFeature(_short, 0x01, 0x06, HidppFeature.DeviceName));
    }

    [Fact]
    public void Collections_of_one_receiver_share_a_channel_key_by_parent_or_by_path()
    {
        Assert.Equal(LogitechCommandPolicy.ChannelKey(_short), LogitechCommandPolicy.ChannelKey(_long));
        Assert.NotEqual(LogitechCommandPolicy.ChannelKey(_short), LogitechCommandPolicy.ChannelKey(_otherReceiverShort));

        var backend = new FakeHidBackend();
        var col01 = backend.Add(0x046D, 0xC54D, 0xFF00, 0x0001, 7, 7, mi: 2,
            path: @"\\?\hid#vid_046d&pid_c54d&mi_02&col01#c&1823a09c&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}").Info with { ParentInstanceId = null };
        var col02 = backend.Add(0x046D, 0xC54D, 0xFF00, 0x0002, 20, 20, mi: 2,
            path: @"\\?\hid#vid_046d&pid_c54d&mi_02&col02#c&1823a09c&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}").Info with { ParentInstanceId = null };
        Assert.Equal(LogitechCommandPolicy.ChannelKey(col01), LogitechCommandPolicy.ChannelKey(col02));
    }
}
