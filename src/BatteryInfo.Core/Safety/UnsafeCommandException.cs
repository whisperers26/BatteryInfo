namespace BatteryInfo.Core.Safety;

/// <summary>A frame was refused by its vendor's allow-list and was not sent.</summary>
public sealed class UnsafeCommandException(string message) : InvalidOperationException(message);
