using BatteryInfo.Core.Monitoring;

namespace BatteryInfo.Core;

/// <summary>Creates the vendor providers the app and the CLI run, backed by the real Windows APIs.</summary>
public static class ProviderFactory
{
    public static IReadOnlyList<IBatteryProvider> CreateAll() => [];
}
