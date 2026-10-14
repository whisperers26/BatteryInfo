using BatteryInfo.Core.Tests.Fakes;
using BatteryInfo.Core.Updates;

namespace BatteryInfo.Core.Tests.Updates;

public sealed class UpdaterTests : IDisposable
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    private readonly ManualTimeProvider _time = new();
    private readonly FakeBackend _backend = new();
    private readonly List<Updater> _updaters = [];

    public void Dispose()
    {
        foreach (var updater in _updaters) updater.Dispose();
    }

    private sealed class FakeBackend : IUpdateBackend
    {
        public bool IsInstalled { get; set; } = true;

        public string? CurrentVersion { get; set; } = "1.0.0";

        public string? Available { get; set; }

        public Exception? CheckError { get; set; }

        public Exception? DownloadError { get; set; }

        public int Checks { get; private set; }

        public int Downloads { get; private set; }

        public int Applied { get; private set; }

        public Task<string?> CheckAsync(CancellationToken cancellationToken)
        {
            Checks++;
            return CheckError is null ? Task.FromResult(Available) : Task.FromException<string?>(CheckError);
        }

        public Task DownloadAsync(CancellationToken cancellationToken)
        {
            Downloads++;
            return DownloadError is null ? Task.CompletedTask : Task.FromException(DownloadError);
        }

        public void ApplyAndRestartAfterExit() => Applied++;
    }

    private Updater Updater(bool automatic = true)
    {
        var updater = new Updater(_backend, _time) { Automatic = automatic };
        _updaters.Add(updater);
        return updater;
    }

    private static async Task<UpdateStatus> NextStatus(Updater updater, UpdateState state)
    {
        var tcs = new TaskCompletionSource<UpdateStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        updater.StatusChanged += (_, status) =>
        {
            if (status.State == state) tcs.TrySetResult(status);
        };
        if (updater.Status.State == state) return updater.Status;
        return await tcs.Task.WaitAsync(WaitLimit);
    }

    [Fact]
    public void Not_installed_build_is_unavailable_and_never_checks()
    {
        _backend.IsInstalled = false;
        var updater = Updater();

        updater.Start();
        _time.Advance(TimeSpan.FromHours(7));

        Assert.Equal(UpdateState.Unavailable, updater.Status.State);
        Assert.Equal(0, _backend.Checks);
    }

    [Fact]
    public async Task Manual_check_without_an_update_is_up_to_date()
    {
        var updater = Updater(automatic: false);

        await updater.CheckNowAsync();

        Assert.Equal(UpdateState.UpToDate, updater.Status.State);
        Assert.Equal("1.0.0", updater.Status.CurrentVersion);
    }

    [Fact]
    public async Task Found_update_is_downloaded_but_not_installed_yet()
    {
        _backend.Available = "1.1.0";
        var updater = Updater(automatic: false);

        await updater.CheckNowAsync();

        Assert.Equal(UpdateState.ReadyToRestart, updater.Status.State);
        Assert.Equal("1.1.0", updater.Status.NewVersion);
        Assert.Equal(1, _backend.Downloads);
        Assert.Equal(0, _backend.Applied);
    }

    [Fact]
    public async Task Restart_hands_the_update_to_the_installer_and_asks_the_app_to_exit()
    {
        _backend.Available = "1.1.0";
        var updater = Updater(automatic: false);
        var exitRequested = false;
        updater.ExitRequested += (_, _) => exitRequested = true;
        await updater.CheckNowAsync();

        updater.RestartToUpdate();

        Assert.Equal(1, _backend.Applied);
        Assert.True(exitRequested);
    }

    [Fact]
    public void Restart_without_a_downloaded_update_does_nothing()
    {
        var updater = Updater(automatic: false);

        updater.RestartToUpdate();

        Assert.Equal(0, _backend.Applied);
    }

    [Fact]
    public async Task Automatic_mode_checks_a_minute_after_start_then_every_six_hours()
    {
        var updater = Updater();
        updater.Start();
        Assert.Equal(0, _backend.Checks);

        _time.Advance(TimeSpan.FromMinutes(1));
        await NextStatus(updater, UpdateState.UpToDate);
        Assert.Equal(1, _backend.Checks);

        _time.Advance(TimeSpan.FromHours(6));
        await WaitFor(() => _backend.Checks == 2);
    }

    [Fact]
    public void Manual_mode_never_checks_by_itself()
    {
        var updater = Updater(automatic: false);
        updater.Start();

        _time.Advance(TimeSpan.FromHours(13));

        Assert.Equal(0, _backend.Checks);
    }

    [Fact]
    public async Task Turning_automatic_on_schedules_the_next_check()
    {
        var updater = Updater(automatic: false);
        updater.Start();

        updater.Automatic = true;
        _time.Advance(TimeSpan.FromMinutes(1));

        await WaitFor(() => _backend.Checks == 1);
    }

    [Fact]
    public async Task Automatic_update_announces_each_new_version_once()
    {
        _backend.Available = "1.1.0";
        var updater = Updater();
        var announced = new List<string>();
        updater.UpdateReady += (_, version) => announced.Add(version);
        updater.Start();

        _time.Advance(TimeSpan.FromMinutes(1));
        await NextStatus(updater, UpdateState.ReadyToRestart);
        _time.Advance(TimeSpan.FromHours(6));
        await WaitFor(() => _backend.Checks == 2);

        Assert.Equal(["1.1.0"], announced);
    }

    [Fact]
    public async Task Manual_check_does_not_announce()
    {
        _backend.Available = "1.1.0";
        var updater = Updater(automatic: false);
        var announced = 0;
        updater.UpdateReady += (_, _) => announced++;

        await updater.CheckNowAsync();

        Assert.Equal(0, announced);
    }

    [Fact]
    public async Task Check_failure_is_reported_with_its_message()
    {
        _backend.CheckError = new HttpRequestException("No connection");
        var updater = Updater(automatic: false);

        await updater.CheckNowAsync();

        Assert.Equal(UpdateState.Failed, updater.Status.State);
        Assert.Contains("No connection", updater.Status.Message);
    }

    [Fact]
    public async Task Download_failure_is_reported_and_nothing_is_applied()
    {
        _backend.Available = "1.1.0";
        _backend.DownloadError = new IOException("Disk full");
        var updater = Updater(automatic: false);

        await updater.CheckNowAsync();

        Assert.Equal(UpdateState.Failed, updater.Status.State);
        Assert.Equal(0, _backend.Applied);
    }

    [Fact]
    public async Task Once_ready_a_later_check_does_not_download_again()
    {
        _backend.Available = "1.1.0";
        var updater = Updater(automatic: false);
        await updater.CheckNowAsync();

        await updater.CheckNowAsync();

        Assert.Equal(1, _backend.Downloads);
        Assert.Equal(UpdateState.ReadyToRestart, updater.Status.State);
    }

    [Fact]
    public async Task A_newer_version_found_while_ready_is_downloaded()
    {
        _backend.Available = "1.1.0";
        var updater = Updater(automatic: false);
        await updater.CheckNowAsync();

        _backend.Available = "1.2.0";
        await updater.CheckNowAsync();

        Assert.Equal(2, _backend.Downloads);
        Assert.Equal("1.2.0", updater.Status.NewVersion);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(waited.Elapsed < WaitLimit, "condition not met in time");
            await Task.Delay(5);
        }
    }
}
