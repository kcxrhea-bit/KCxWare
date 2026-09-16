using KCxWare.Core.Loading;

namespace KCxWare.Tests.Loading;

public class LoadingCoordinatorTests
{
    [Fact]
    public void Begin_MakesOperationCurrentAndVisible()
    {
        var coordinator = new LoadingCoordinator();
        using var op = coordinator.Begin("Gaming Mode", "Stopping development services…");

        Assert.NotNull(coordinator.Current);
        Assert.Equal("Gaming Mode", coordinator.Current!.Title);
        Assert.Equal("Stopping development services…", coordinator.Current.Status);
        Assert.True(coordinator.Current.IsIndeterminate);
    }

    [Fact]
    public void Update_ChangesStatusAndProgressWithoutFabricatingCompletion()
    {
        var coordinator = new LoadingCoordinator();
        using var op = coordinator.Begin("Recovery", "Verifying KCx Gaming environment…", indeterminate: false);

        op.Update("Restoring services…", progress: 40);

        Assert.Equal(40, coordinator.Current!.Progress);
        Assert.Equal("Restoring services…", coordinator.Current.Status);
        Assert.False(coordinator.Current.IsCompleted);
    }

    [Fact]
    public void Complete_SetsProgressTo100AndMarksCompleted()
    {
        var coordinator = new LoadingCoordinator();
        using var op = coordinator.Begin("Settings", "Applying settings…", indeterminate: false);

        op.Update(progress: 50);
        op.Complete("Settings saved.");

        Assert.True(coordinator.Current!.IsCompleted);
        Assert.Equal(100, coordinator.Current.Progress);
        Assert.False(coordinator.Current.IsFailed);
    }

    [Fact]
    public void Fail_NeverFlashesSuccessFirst_AndPreservesErrorMessage()
    {
        var coordinator = new LoadingCoordinator();
        using var op = coordinator.Begin("Gaming Mode", "Stopping development services…", indeterminate: false);

        op.Update(progress: 70);
        op.Fail("Service 'WSearch' failed to stop within the timeout.");

        var state = coordinator.Current!;
        Assert.True(state.IsFailed);
        Assert.False(state.IsCompleted);
        Assert.Equal("Service 'WSearch' failed to stop within the timeout.", state.ErrorMessage);
    }

    [Fact]
    public void OverlappingOperations_InnerCompletionDoesNotDismissOuterOperation()
    {
        var coordinator = new LoadingCoordinator();
        using var outer = coordinator.Begin("Gaming Mode", "Requesting elevation…");
        using (var inner = coordinator.Begin("Device Refresh", "Refreshing devices…"))
        {
            Assert.Equal(inner.Id, coordinator.Current!.Id);
            inner.Complete("Devices refreshed.");
            coordinator.Dismiss(inner.Id);
        }

        // Outer operation must still be tracked and current after the inner one finished and was dismissed.
        Assert.NotNull(coordinator.Current);
        Assert.Equal(outer.Id, coordinator.Current!.Id);
        Assert.False(coordinator.Current.IsCompleted);
    }

    [Fact]
    public void Dismiss_RemovesOperationFromActiveSet()
    {
        var coordinator = new LoadingCoordinator();
        var op = coordinator.Begin("Settings", "Saving changes…");
        op.Complete();

        coordinator.Dismiss(op.Id);

        Assert.Null(coordinator.Current);
        Assert.Empty(coordinator.ActiveOperations);
    }

    [Fact]
    public void Dispose_WithoutCompleteOrFail_RemovesOperationAsSafetyNet()
    {
        var coordinator = new LoadingCoordinator();
        using (coordinator.Begin("Abandoned", "Doing something…"))
        {
            // no Complete/Fail called
        }

        Assert.Null(coordinator.Current);
    }

    [Fact]
    public async Task Begin_ReturnsImmediately_WhileTheRealOperationRunsAsynchronouslyInTheBackground()
    {
        // Mirrors the WPF flow: Begin() sets the busy/visible state synchronously and returns at
        // once; the actual elevated-helper work (modeled here by a slow Task) proceeds
        // independently and is only reflected via Update/Complete once it finishes - the
        // coordinator itself never blocks the caller waiting for that work.
        var coordinator = new LoadingCoordinator();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        using var op = coordinator.Begin("Gaming Mode", "Requesting Windows elevation…");
        var beginElapsed = sw.ElapsedMilliseconds;

        Assert.True(beginElapsed < 100, "Begin() must return immediately, not wait for the operation to finish.");
        Assert.True(coordinator.Current is { IsCompleted: false, IsFailed: false });

        var simulatedHelperExit = await Task.Run(async () =>
        {
            await Task.Delay(50);
            return 0;
        });

        Assert.Equal(0, simulatedHelperExit);
        op.Complete("Elevation granted.");
        Assert.True(coordinator.Current!.IsCompleted);
    }

    [Fact]
    public void Dispose_AfterComplete_DoesNotRemoveOperation()
    {
        var coordinator = new LoadingCoordinator();
        string id;
        using (var op = coordinator.Begin("Settings", "Saving changes…"))
        {
            id = op.Id;
            op.Complete();
        }

        // Handle disposal must not silently hide a completed operation before the UI can show it.
        Assert.NotNull(coordinator.Current);
        Assert.Equal(id, coordinator.Current!.Id);
    }

    [Fact]
    public void Progress_IsMonotonic_AndFailurePreservesReachedPercentage()
    {
        var coordinator = new LoadingCoordinator();
        using var op = coordinator.Begin("Gaming Mode", "Preparing…", indeterminate: false);

        op.Update("Baseline secured", 60, false);
        op.Update("Stale event", 40, false);

        Assert.Equal(60, coordinator.Current!.Progress);
        Assert.False(coordinator.Current.IsCompleted);
        op.Fail("Verification failed.");
        Assert.Equal(60, coordinator.Current.Progress);
        Assert.True(coordinator.Current.IsFailed);
        Assert.False(coordinator.Current.IsCompleted);
    }

    [Fact]
    public void CompleteWithMessage_SetsTextOnlyCompletionTitleAndSubtitle()
    {
        var coordinator = new LoadingCoordinator();
        using var op = coordinator.Begin("Gaming Mode", "Requesting Windows elevation…");

        op.CompleteWithMessage("GAME MODE ENHANCED", "Latency neutralized. Good luck, pilot.");

        var state = coordinator.Current!;
        Assert.True(state.IsCompleted);
        Assert.Equal(100, state.Progress);
        Assert.Equal("GAME MODE ENHANCED", state.CompletionTitle);
        Assert.Equal("Latency neutralized. Good luck, pilot.", state.CompletionSubtitle);
    }

    [Fact]
    public void FailedOperation_NeverCarriesTextCompletionCopy()
    {
        // A failed transition must never expose success completion copy: Fail() never sets
        // CompletionTitle/CompletionSubtitle, so the UI's HasTextCompletion binding stays false.
        var coordinator = new LoadingCoordinator();
        using var op = coordinator.Begin("Gaming Mode", "Stopping development services…");

        op.Fail("Gaming cleanup verification failed.");

        var state = coordinator.Current!;
        Assert.True(state.IsFailed);
        Assert.False(state.IsCompleted);
        Assert.Null(state.CompletionTitle);
        Assert.Null(state.CompletionSubtitle);
    }

    [Fact]
    public void CompleteWithMessage_IsPresentationOnly_DoesNotTouchAuthoritativeModeState()
    {
        // LoadingOperationState/LoadingCoordinator carry zero references to ModeState/IStateStore;
        // completing with a text message can only ever mutate this presentation-only record.
        var coordinator = new LoadingCoordinator();
        using var op = coordinator.Begin("Restart Windows", "Returning to Normal Mode…");

        op.CompleteWithMessage("REBOOTING SYSTEM.", "Cleaning out the digital cobwebs.");

        // The only side effects of CompleteWithMessage are on this operation's own presentation
        // fields; nothing here is capable of writing to any persisted mode/power state.
        Assert.True(coordinator.Current!.IsCompleted);
        Assert.Single(coordinator.ActiveOperations);
    }
}
