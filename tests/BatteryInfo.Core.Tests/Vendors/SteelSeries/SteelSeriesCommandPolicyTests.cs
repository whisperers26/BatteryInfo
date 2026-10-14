using BatteryInfo.Core.Vendors.SteelSeries;

namespace BatteryInfo.Core.Tests.Vendors.SteelSeries;

public class SteelSeriesCommandPolicyTests
{
    private readonly SteelSeriesCommandPolicy _policy = new();

    private static byte[] Frame(params byte[] head) => SteelSeriesProtocolTests.Pad65(head);

    [Theory]
    [InlineData((ushort)0x2253)]
    [InlineData((ushort)0x2232)]
    public void Status_query_is_allowed_on_a_known_base_station(ushort pid) =>
        Assert.True(_policy.IsAllowed(SteelSeriesProtocolTests.Device(pid), Frame(0x00, 0xB0)));

    [Fact]
    public void Built_request_passes_the_policy() =>
        Assert.True(_policy.IsAllowed(SteelSeriesProtocolTests.Device(), SteelSeriesProtocol.BuildStatusRequest()));

    // docs/protocols/steelseries.md section 4: setters and saves on the same collection.
    [Theory]
    [InlineData(new byte[] { 0x00, 0x09 })]
    [InlineData(new byte[] { 0x00, 0x35, 0x01 })]
    [InlineData(new byte[] { 0x00, 0x39, 0x05 })]
    [InlineData(new byte[] { 0x00, 0x37, 0x05 })]
    [InlineData(new byte[] { 0x00, 0xAE, 0x01 })]
    [InlineData(new byte[] { 0x00, 0xA3, 0x0A })]
    [InlineData(new byte[] { 0x00, 0x27, 0x01 })]
    [InlineData(new byte[] { 0x00, 0x33, 0x20 })]
    public void Setters_and_saves_are_refused(byte[] head) =>
        Assert.False(_policy.IsAllowed(SteelSeriesProtocolTests.Device(), Frame(head)));

    [Fact]
    public void Status_query_with_a_payload_is_refused() =>
        Assert.False(_policy.IsAllowed(SteelSeriesProtocolTests.Device(), Frame(0x00, 0xB0, 0x01)));

    [Fact]
    public void Another_report_id_is_refused() =>
        Assert.False(_policy.IsAllowed(SteelSeriesProtocolTests.Device(), Frame(0x01, 0xB0)));

    [Fact]
    public void Short_frame_is_refused() =>
        Assert.False(_policy.IsAllowed(SteelSeriesProtocolTests.Device(), [0x00, 0xB0]));

    [Fact]
    public void Other_collections_of_the_base_station_are_refused()
    {
        Assert.False(_policy.IsAllowed(SteelSeriesProtocolTests.Device(usagePage: 0xFF00), Frame(0x00, 0xB0)));
        Assert.False(_policy.IsAllowed(SteelSeriesProtocolTests.Device(usagePage: 0x000C), Frame(0x00, 0xB0)));
    }

    [Fact]
    public void Unknown_products_and_vendors_are_refused()
    {
        Assert.False(_policy.IsAllowed(SteelSeriesProtocolTests.Device(pid: 0x12AD), Frame(0x00, 0xB0)));
        Assert.False(_policy.IsAllowed(SteelSeriesProtocolTests.Device(vid: 0x046D), Frame(0x00, 0xB0)));
    }
}
