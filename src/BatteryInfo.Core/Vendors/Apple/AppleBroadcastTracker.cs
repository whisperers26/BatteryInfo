using System.Globalization;
using BatteryInfo.Core.Bluetooth;
using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Model;

namespace BatteryInfo.Core.Vendors.Apple;

/// <summary>What the tracker currently shows for its device.</summary>
/// <param name="Components">The fresh levels; empty when none is fresh.</param>
/// <param name="LastHeard">When the last frame of the followed address was taken in; null since the last connect.</param>
/// <param name="Address">The random address followed now, or null.</param>
/// <param name="Rule">Why that address was chosen, for traces.</param>
/// <param name="CaseReadAt">When the case level shown was read, if it is a remembered one rather than a fresh one.</param>
public sealed record AppleBroadcastSnapshot(
    IReadOnlyList<BatteryComponent> Components,
    DateTimeOffset? LastHeard,
    ulong? Address,
    string? Rule,
    DateTimeOffset? CaseReadAt = null);

/// <summary>Picks, among the broadcasts of one model, the address that belongs to one paired device, and keeps its
/// levels (docs/protocols/apple.md §7-8). Without the identity key nothing proves ownership, so the choice rests on
/// whether an address's levels continue the ones shown, when it first appeared, and signal strength, in that order.
/// Not thread-safe: the provider serializes calls.</summary>
public sealed class AppleBroadcastTracker(AppleModel model, TimeProvider time)
{
    public const string ContinuityRule = "levels match";
    public const string BornAtConnectRule = "new at connect";
    public const string StrongestRule = "strongest";

    /// <summary>The RSSI Windows reports when it measured none.</summary>
    public const short NoSignal = -127;

    /// <summary>The weakest smoothed signal the strongest rule accepts. A followed address is kept whatever its signal
    /// (one chosen as the strongest still gives way to better evidence).</summary>
    internal const short StrongestFloor = -80;

    /// <summary>Frames an address needs within <see cref="AliveFor"/> before it can be chosen.</summary>
    internal const int MinHits = 2;

    private const int MatchTolerance = 10;
    private const double Smoothing = 0.3;
    private const string CaseName = "Case";

    /// <summary>An address unheard for longer is no longer followed.</summary>
    internal static readonly TimeSpan AliveFor = TimeSpan.FromSeconds(30);

    internal static readonly TimeSpan ForgetAfter = TimeSpan.FromMinutes(10);

    /// <summary>Addresses first heard from this long before a connect to <see cref="BornAfterConnect"/> after it count as
    /// new at the connect: pods take a new address when they leave the case.</summary>
    internal static readonly TimeSpan BornBeforeConnect = TimeSpan.FromSeconds(60);

    internal static readonly TimeSpan BornAfterConnect = TimeSpan.FromSeconds(30);

    /// <summary>Addresses first heard this soon after the scan started were already in range, so they never count as
    /// new at a connect: every address seems new when the scan has only just begun.</summary>
    internal static readonly TimeSpan AlreadyAroundFor = TimeSpan.FromSeconds(10);

    /// <summary>How long every address in range gets to be heard before the strongest rule picks one, when the connect
    /// moment is unknown.</summary>
    internal static readonly TimeSpan SettleUnknownConnect = TimeSpan.FromSeconds(5);

    /// <summary>How long every address in range gets to be heard before the strongest rule picks one, after the
    /// followed address went silent.</summary>
    internal static readonly TimeSpan SettleAfterLoss = TimeSpan.FromSeconds(10);

    internal static readonly TimeSpan PodFreshFor = TimeSpan.FromMinutes(3);

    internal static readonly TimeSpan CaseFreshFor = TimeSpan.FromMinutes(10);

    /// <summary>How long the case level read the last time a pod was in the case is still shown, across reconnects.
    /// Worn pods never report the case, and a closed case's level only changes while it charges.</summary>
    internal static readonly TimeSpan CaseMemoryFor = TimeSpan.FromHours(6);

    /// <summary>A one-battery model's charging bit drops out for seconds at a time while it charges.</summary>
    internal static readonly TimeSpan ChargingHold = TimeSpan.FromSeconds(30);

    private readonly Dictionary<ulong, Candidate> _candidates = [];
    private readonly Dictionary<string, Held> _held = [];
    private bool _connected;
    private DateTimeOffset? _connectedAt;
    private DateTimeOffset _strongestAllowedAt;
    private ulong? _chosen;
    private string? _rule;
    private IReadOnlyList<BatteryComponent>? _reference;
    private DateTimeOffset? _lastHeard;
    private Held? _caseMemory;
    private DateTimeOffset _listeningSince = time.GetUtcNow();

    public AppleModel Model => model;

    /// <summary>The scan (re)started now: the addresses in range are about to be heard again. A new tracker assumes the
    /// scan started when it was created.</summary>
    public void ListeningStarted() => _listeningSince = time.GetUtcNow();

    /// <summary>The device connected: start over. <paramref name="at"/> is when, or null when that is not known
    /// (it was already connected when first seen).</summary>
    public void Connected(DateTimeOffset? at)
    {
        Forget();
        _connected = true;
        _connectedAt = at;
        _strongestAllowedAt = at is { } moment ? moment + BornAfterConnect : time.GetUtcNow() + SettleUnknownConnect;
    }

    public void Disconnected()
    {
        Forget();
        _connected = false;
        _connectedAt = null;
    }

    /// <summary>Takes in one status frame. Frames are taken in while disconnected too, so the addresses already around
    /// before a connect are known. Returns true when what <see cref="Snapshot"/> shows changed.</summary>
    public bool Offer(ulong address, short rssi, ProximityStatus status)
    {
        if (status.ModelId != model.Id) return false;
        var now = time.GetUtcNow();
        Prune(now);
        Track(address, rssi, status, now);
        if (!_connected) return false;

        var before = Snapshot().Components;
        var changed = Choose(now);
        if (_chosen is { } chosen && (changed || chosen == address))
        {
            var candidate = _candidates[chosen];
            Apply(candidate.LastStatus, candidate.LastSeen);
        }
        return !before.SequenceEqual(Snapshot().Components);
    }

    public AppleBroadcastSnapshot Snapshot()
    {
        var now = time.GetUtcNow();
        var components = model.ComponentNames.Select(name => Shown(name, now)).ToList();
        DateTimeOffset? caseReadAt = null;
        var caseIndex = components.FindIndex(c => c.Name == CaseName);
        if (caseIndex >= 0 && components[caseIndex].Percent is null && components.Any(c => c.Percent is not null) &&
            _caseMemory is { } memory && now - memory.At <= CaseMemoryFor)
        {
            components[caseIndex] = new BatteryComponent(CaseName, memory.Percent, ChargeState.Unknown);
            caseReadAt = memory.At;
        }
        if (components.All(c => c.Percent is null)) components = [];
        return new AppleBroadcastSnapshot(components, _lastHeard, _chosen, _rule, caseReadAt);
    }

    private void Forget()
    {
        _chosen = null;
        _rule = null;
        _held.Clear();
        _reference = null;
        _lastHeard = null;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (address, candidate) in _candidates.ToList())
        {
            if (now - candidate.LastSeen > ForgetAfter) _candidates.Remove(address);
        }
    }

    private void Track(ulong address, short rssi, ProximityStatus status, DateTimeOffset now)
    {
        if (!_candidates.TryGetValue(address, out var candidate)) _candidates[address] = candidate = new Candidate(now, status);
        candidate.LastSeen = now;
        candidate.LastStatus = status;
        candidate.Hits.Enqueue(now);
        while (candidate.Hits.Peek() < now - AliveFor) candidate.Hits.Dequeue();
        if (rssi != NoSignal) candidate.Rssi = candidate.Rssi is { } old ? old * (1 - Smoothing) + rssi * Smoothing : rssi;
    }

    /// <summary>Keeps, replaces or picks the followed address. Returns true when it changed to another one.</summary>
    private bool Choose(DateTimeOffset now)
    {
        if (_chosen is { } current && _candidates.TryGetValue(current, out var followed) && now - followed.LastSeen <= AliveFor)
        {
            // Only a choice made on signal strength alone gives way, to an address with better evidence.
            if (_rule != StrongestRule || Best(now, allowStrongest: false) is not { } better) return false;
            if (better.Address == current)
            {
                _rule = better.Rule;
                return false;
            }
            Follow(better, keepLevels: false);
            return true;
        }

        if (_chosen is { } lost)
        {
            // The levels it showed are what a new address must continue. A choice made on signal alone may have been a
            // stranger: its levels are dropped, and the reference stays the one from before it.
            if (_rule != StrongestRule)
            {
                _reference = HeldLevels();
            }
            else
            {
                _held.Clear();
                _lastHeard = null;
            }
            Log.Info($"{model.Name}: {Format(lost)} went silent; looking for its next address");
            _chosen = null;
            _rule = null;
            _strongestAllowedAt = now + SettleAfterLoss;
        }

        if (Best(now, allowStrongest: now >= _strongestAllowedAt) is not { } pick) return false;
        Follow(pick, keepLevels: pick.Rule == ContinuityRule);
        return true;
    }

    private Pick? Best(DateTimeOffset now, bool allowStrongest)
    {
        Pick? best = null;
        foreach (var (address, candidate) in _candidates)
        {
            if (now - candidate.LastSeen > AliveFor || candidate.Hits.Count(t => t >= now - AliveFor) < MinHits) continue;
            var rule = Continues(candidate.LastStatus) ? ContinuityRule
                : IsBornAtConnect(candidate) ? BornAtConnectRule
                : allowStrongest && candidate.Rssi >= StrongestFloor ? StrongestRule
                : null;
            if (rule is null) continue;

            var pick = new Pick(address, rule, Rank(rule), candidate.Rssi ?? double.MinValue);
            if (best is null || pick.Rank < best.Rank || (pick.Rank == best.Rank && pick.Rssi > best.Rssi)) best = pick;
        }
        return best;
    }

    private void Follow(Pick pick, bool keepLevels)
    {
        _chosen = pick.Address;
        _rule = pick.Rule;
        if (!keepLevels)
        {
            _held.Clear();
            _lastHeard = null;
        }
        if (pick.Rule != StrongestRule) _reference = null;
        var rssi = pick.Rssi == double.MinValue ? "no signal value" : $"{pick.Rssi.ToString("0", CultureInfo.InvariantCulture)} dBm";
        Log.Info($"{model.Name}: following {Format(pick.Address)} ({pick.Rule}, {rssi})");
    }

    // The levels a new address must continue: within one 10 % step on every level both report, at least one of them.
    private bool Continues(ProximityStatus status)
    {
        if (_reference is null) return false;
        var compared = 0;
        foreach (var component in model.Components(status))
        {
            if (component.Percent is not { } percent) continue;
            if (_reference.FirstOrDefault(r => r.Name == component.Name)?.Percent is not { } shown) continue;
            if (Math.Abs(percent - shown) > MatchTolerance) return false;
            compared++;
        }
        return compared > 0;
    }

    private bool IsBornAtConnect(Candidate candidate) =>
        _connectedAt is { } at && candidate.FirstSeen >= at - BornBeforeConnect && candidate.FirstSeen <= at + BornAfterConnect &&
        candidate.FirstSeen >= _listeningSince + AlreadyAroundFor;

    private static int Rank(string rule) => rule switch
    {
        ContinuityRule => 0,
        BornAtConnectRule => 1,
        _ => 2,
    };

    // A level not reported in this frame keeps the last reported one, until it is no longer fresh.
    private void Apply(ProximityStatus status, DateTimeOffset at)
    {
        foreach (var component in model.Components(status))
        {
            if (component.Percent is not { } percent) continue;
            var charging = component.Charge == ChargeState.Charging;
            var chargingAt = charging ? at : _held.GetValueOrDefault(component.Name)?.ChargingAt;
            _held[component.Name] = new Held(percent, charging, at, chargingAt);

            // Remembered across reconnects, unless it may be a stranger's (a pick on signal alone).
            if (component.Name == CaseName && _rule != StrongestRule) _caseMemory = new Held(percent, false, at, null);
        }
        _lastHeard = at;
    }

    private BatteryComponent Shown(string name, DateTimeOffset now)
    {
        if (!_held.TryGetValue(name, out var held) || now - held.At > (name == CaseName ? CaseFreshFor : PodFreshFor))
        {
            return new BatteryComponent(name, null, ChargeState.Unknown);
        }
        var charging = held.Charging ||
                       (model.Layout == AppleLayout.OneBattery && held.ChargingAt is { } since && now - since <= ChargingHold);
        return new BatteryComponent(name, held.Percent, charging ? ChargeState.Charging : ChargeState.Unknown);
    }

    // However old: a level is still the best guess of what the next address must continue.
    private List<BatteryComponent> HeldLevels() =>
        _held.Select(h => new BatteryComponent(h.Key, h.Value.Percent, ChargeState.Unknown)).ToList();

    private static string Format(ulong address) => BluetoothBatteryProperty.FormatAddress(address);

    private sealed class Candidate(DateTimeOffset firstSeen, ProximityStatus status)
    {
        public DateTimeOffset FirstSeen { get; } = firstSeen;

        public DateTimeOffset LastSeen { get; set; } = firstSeen;

        public ProximityStatus LastStatus { get; set; } = status;

        public Queue<DateTimeOffset> Hits { get; } = new();

        /// <summary>Smoothed signal strength; null until one frame carried a measured value.</summary>
        public double? Rssi { get; set; }
    }

    private sealed record Held(int Percent, bool Charging, DateTimeOffset At, DateTimeOffset? ChargingAt);

    private sealed record Pick(ulong Address, string Rule, int Rank, double Rssi);
}
