using KCxWare.Core.Windows;

namespace KCxWare.Tests;

public sealed class WindowsSystemControllerLocaleTests
{
    [Fact]
    public async Task IsServiceRunning_UsesNumericStoppedState()
    {
        var controller = Controller(new FakeServiceStatusReader(0x00000001));

        Assert.False(await controller.IsServiceRunningAsync("WSearch"));
    }

    [Fact]
    public async Task IsServiceRunning_UsesNumericRunningState()
    {
        var controller = Controller(new FakeServiceStatusReader(0x00000004));

        Assert.True(await controller.IsServiceRunningAsync("WSearch"));
    }

    [Fact]
    public async Task IsServiceRunning_ReturnsNullForMissingService()
    {
        var controller = Controller(new FakeServiceStatusReader(null));

        Assert.Null(await controller.IsServiceRunningAsync("MissingService"));
    }

    [Fact]
    public async Task IsServiceRunning_PropagatesQueryFailures()
    {
        var expected = new InvalidOperationException("query failed");
        var controller = Controller(new FakeServiceStatusReader(null, expected));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.IsServiceRunningAsync("WSearch"));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task DeleteOneShotTask_WhenAbsent_DoesNotRunDelete()
    {
        var runner = new SequencedRunner(new CommandResult(1, string.Empty, "localized absence"));
        var controller = Controller(new FakeServiceStatusReader(null), runner);

        await controller.DeleteOneShotTaskAsync();

        Assert.DoesNotContain(runner.Calls, call => call.FileName.Equals("schtasks.exe", StringComparison.OrdinalIgnoreCase) &&
            call.Arguments.Contains("/Delete", StringComparer.OrdinalIgnoreCase));
        Assert.Contains(runner.Calls, call => call.Arguments.Contains("/Query", StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeleteOneShotTask_WhenPresent_DeletesSuccessfully()
    {
        var runner = new SequencedRunner(
            new CommandResult(0, "task", string.Empty),
            new CommandResult(0, string.Empty, string.Empty));
        var controller = Controller(new FakeServiceStatusReader(null), runner);

        await controller.DeleteOneShotTaskAsync();

        Assert.Equal(2, runner.Calls.Count);
        Assert.Contains("/Query", runner.Calls[0].Arguments);
        Assert.Contains("/Delete", runner.Calls[1].Arguments);
    }

    [Fact]
    public async Task DeleteOneShotTask_WhenDeleteFails_ThrowsWithoutParsingLocalizedError()
    {
        var runner = new SequencedRunner(
            new CommandResult(0, "task", string.Empty),
            new CommandResult(1, string.Empty, "localized failure"));
        var controller = Controller(new FakeServiceStatusReader(null), runner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.DeleteOneShotTaskAsync());
    }

    private static WindowsSystemController Controller(IServiceStatusReader statusReader,
        ICommandRunner? runner = null) =>
        new(runner ?? new RecordingRunner(), serviceStatusReader: statusReader);

    private sealed class FakeServiceStatusReader(uint? state, Exception? failure = null) : IServiceStatusReader
    {
        public Task<uint?> QueryCurrentStateAsync(string name, CancellationToken cancellationToken = default) =>
            failure is null ? Task.FromResult(state) : Task.FromException<uint?>(failure);

    }
}

internal sealed class SequencedServiceStatusReader(params uint[] states) : IServiceStatusReader
{
    private readonly Queue<uint> _states = new(states);
    public int Calls { get; private set; }

    public Task<uint?> QueryCurrentStateAsync(string name, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult<uint?>(_states.Dequeue());
    }
}
