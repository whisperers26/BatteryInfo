using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Vendors.Apple;
using BatteryInfo.Core.Vendors.Betop;
using BatteryInfo.Core.Vendors.Huawei;
using BatteryInfo.Core.Vendors.Logitech;
using BatteryInfo.Core.Vendors.Razer;
using BatteryInfo.Core.Vendors.Rog;
using BatteryInfo.Core.Vendors.SteelSeries;

namespace BatteryInfo.Core;

/// <summary>Creates the vendor providers the app and the CLI run, backed by the real Windows APIs.</summary>
public static class ProviderFactory
{
    private static Func<IBatteryProvider>[] Factories(bool oneShot) =>
    [
        () => new LogitechBatteryProvider(),
        () => new RogBatteryProvider(),
        () => new HuaweiBatteryProvider(),
        () => new BetopBatteryProvider(),
        () => new RazerBatteryProvider(),
        () => new SteelSeriesBatteryProvider(),
        () => new AppleBatteryProvider(new WindowsAppleBluetooth(),
            firstFrameWait: oneShot ? AppleBatteryProvider.OneShotFirstFrameWait : null),
    ];

    /// <param name="oneShot">The caller polls once and exits (the CLI): a provider whose values arrive on their own
    /// waits a little for the first one.</param>
    public static IReadOnlyList<IBatteryProvider> CreateAll(bool oneShot = false) => Create(Factories(oneShot));

    /// <summary>Runs each factory; one that throws is logged and skipped so the others still start.</summary>
    internal static IReadOnlyList<IBatteryProvider> Create(IEnumerable<Func<IBatteryProvider>> factories)
    {
        var providers = new List<IBatteryProvider>();
        foreach (var create in factories)
        {
            try
            {
                providers.Add(create());
            }
            catch (Exception e)
            {
                Log.Error("A battery provider failed to start and was skipped", e);
            }
        }
        return providers;
    }
}
