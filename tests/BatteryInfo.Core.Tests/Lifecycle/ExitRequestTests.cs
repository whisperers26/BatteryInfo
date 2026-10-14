using BatteryInfo.Core.Lifecycle;

namespace BatteryInfo.Core.Tests.Lifecycle;

public sealed class ExitRequestTests
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Event_name_is_the_one_publish_ps1_builds_from_the_process_id() =>
        Assert.Equal(@"Local\BatteryInfo.Exit.4242", ExitRequest.EventName(4242));

    [Fact]
    public void Setting_the_event_by_name_runs_the_listener()
    {
        using var request = ExitRequest.ForCurrentProcess();
        using var exitRequested = new ManualResetEventSlim();
        request.Listen(exitRequested.Set);

        SetByName(Environment.ProcessId);

        Assert.True(exitRequested.Wait(SignalTimeout));
    }

    [Fact]
    public void A_request_sent_before_the_listener_is_registered_is_not_lost()
    {
        using var request = ExitRequest.ForCurrentProcess();
        using var exitRequested = new ManualResetEventSlim();

        SetByName(Environment.ProcessId);
        request.Listen(exitRequested.Set);

        Assert.True(exitRequested.Wait(SignalTimeout));
    }

    // What publish.ps1 does: open the event by the process id it found and set it.
    private static void SetByName(int processId)
    {
        using var exitEvent = EventWaitHandle.OpenExisting(ExitRequest.EventName(processId));
        exitEvent.Set();
    }
}
