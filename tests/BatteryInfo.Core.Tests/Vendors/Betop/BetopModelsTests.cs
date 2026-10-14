using BatteryInfo.Core.Vendors.Betop;

namespace BatteryInfo.Core.Tests.Vendors.Betop;

public class BetopModelsTests
{
    [Theory]
    [InlineData(0x518D)]
    [InlineData(0x518C)]
    public void KP70C2_is_found_by_its_dongle_and_wired_pids(ushort pid)
    {
        var model = BetopModels.FindByProductId(pid);

        Assert.NotNull(model);
        Assert.Equal("KP70C2", model.Name);
        Assert.Equal("betop:KP70C2", model.DeviceId);
        Assert.Equal("Betop KP70C2", model.DisplayName);
        Assert.Equal(0x518C, model.WiredProductId);
        Assert.Equal(0x518D, model.DongleProductId);
    }

    [Theory]
    [InlineData(0x5158, "KP20D")]
    [InlineData(0x5170, "KP20DL")]
    [InlineData(0x5190, "KP20EB")]
    [InlineData(0x515C, "KP40D")]
    [InlineData(0x5165, "KP40MCF")]
    [InlineData(0x5164, "KP40MCK")]
    [InlineData(0x5149, "KP50C")]
    [InlineData(0x5151, "KP50D")]
    [InlineData(0x5152, "KP50E")]
    [InlineData(0x5134, "KP50B")]
    [InlineData(0x512F, "KP70A")]
    [InlineData(0x516E, "KP70A1")]
    [InlineData(0x515F, "KP70B")]
    [InlineData(0x5174, "KP70C")]
    [InlineData(0x518A, "KP70C1")]
    [InlineData(0x5162, "PG01A")]
    public void Plain_byte_siblings_are_in_the_table(ushort pid, string name) =>
        Assert.Equal(name, BetopModels.FindByProductId(pid)?.Name);

    // SetHidWriteEnc 1 models substitute every outgoing byte through a table, so a plain 02 15 would reach
    // them as some other command. They must not be in the table at all.
    [Theory]
    [InlineData(0x5126)] // KP20A
    [InlineData(0x5128)]
    [InlineData(0x5125)] // KP40A
    [InlineData(0x5127)]
    [InlineData(0x5145)] // KP40B
    [InlineData(0x5146)]
    [InlineData(0x5154)] // KP40D-b1
    [InlineData(0x5155)]
    [InlineData(0x515D)] // KP40F
    [InlineData(0x515E)]
    [InlineData(0x5169)]
    [InlineData(0x516A)]
    [InlineData(0x5350)] // 0517:5350, the KP50 firmware-update device
    [InlineData(0x0000)]
    public void Byte_substituting_models_and_unknown_pids_are_not_in_the_table(ushort pid) =>
        Assert.Null(BetopModels.FindByProductId(pid));

    [Fact]
    public void Every_pid_belongs_to_one_model()
    {
        var pids = BetopModels.All.SelectMany(m => new[] { m.WiredProductId, m.DongleProductId }).ToList();

        Assert.Equal(17, BetopModels.All.Count);
        Assert.Equal(pids.Count, pids.Distinct().Count());
    }

    [Fact]
    public void Only_the_KP70C2_dongle_means_asleep_when_it_shows_up_as_20DD()
    {
        Assert.Equal("KP70C2", BetopModels.FindAsleepDongle(0x20DD, 0x518D)?.Name);

        // KP50C and others register 20DD:wiredPID as a normal device, so the rule is KP70C2-only.
        Assert.Null(BetopModels.FindAsleepDongle(0x20DD, 0x5149));
        Assert.Null(BetopModels.FindAsleepDongle(0x20DD, 0x518C));
        Assert.Null(BetopModels.FindAsleepDongle(0x20BC, 0x518D));
        Assert.Null(BetopModels.FindAsleepDongle(0x0517, 0x518D));
    }
}
