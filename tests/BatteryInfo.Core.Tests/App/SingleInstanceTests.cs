using System.Security.AccessControl;
using BatteryInfo.App;

namespace BatteryInfo.Core.Tests.App;

public sealed class SingleInstanceTests
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(5);

    // A fresh name per test, so tests never meet each other's objects or a running BatteryInfo.
    private readonly string _name = "BatteryInfo.Tests." + Guid.NewGuid().ToString("N");

    [Fact]
    public void A_second_TryAcquire_returns_null_while_the_first_copy_runs()
    {
        using var running = SingleInstance.TryAcquire(_name);

        Assert.NotNull(running);
        Assert.Null(SingleInstance.TryAcquire(_name));
    }

    [Fact]
    public void RequestShow_signals_the_running_copy()
    {
        using var running = SingleInstance.TryAcquire(_name);
        using var shown = new ManualResetEventSlim();
        running!.ListenForShowRequests(shown.Set);

        SingleInstance.RequestShow(_name);

        Assert.True(shown.Wait(SignalTimeout));
    }

    [Fact]
    public void RequestShow_returns_quietly_when_the_running_copy_is_elevated()
    {
        // An elevated copy's objects get the elevated token's default DACL, in which this user's logon SID may
        // only read and wait (GR|GX). Opening the event to set it is then denied. Everyone stands in for the SID.
        using var showRequest = CreateEvent($@"Local\{_name}.ShowFlyout", "D:(A;;GRGX;;;WD)");

        SingleInstance.RequestShow(_name);

        Assert.False(showRequest.WaitOne(0));
    }

    private static EventWaitHandle CreateEvent(string name, string sddl)
    {
        var security = new EventWaitHandleSecurity();
        security.SetSecurityDescriptorSddlForm(sddl);
        return EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, name, out _, security);
    }
}
