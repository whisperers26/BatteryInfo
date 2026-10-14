using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;

namespace BatteryInfo.Core.Tests;

public class ProviderFactoryTests
{
    private sealed class StubProvider(string vendor) : IBatteryProvider
    {
        public string Vendor => vendor;

        public event EventHandler? RefreshRequested
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<DeviceReading>> PollAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DeviceReading>>([]);

        public void Dispose()
        {
        }
    }

    [Fact]
    public void Create_skips_a_provider_whose_constructor_throws()
    {
        var providers = ProviderFactory.Create(
        [
            () => new StubProvider("Logitech"),
            () => throw new InvalidOperationException("Bluetooth stack missing"),
            () => new StubProvider("ROG"),
        ]);

        Assert.Equal(["Logitech", "ROG"], providers.Select(p => p.Vendor));
    }
}
