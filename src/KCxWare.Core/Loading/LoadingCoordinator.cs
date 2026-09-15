namespace KCxWare.Core.Loading;

/// <summary>
/// Central, framework-agnostic hub for KCxWare's loading overlay. Any operation - Gaming Mode
/// activation, recovery, settings persistence, state refresh - begins an operation here and
/// updates/completes/fails it as real work progresses. The UI layer (LoadingViewModel) only
/// ever renders whatever this coordinator reports; it never invents progress of its own.
///
/// Concurrent operations are tracked on a stack keyed by operation id so that one operation
/// finishing can never dismiss a different operation that is still active: the overlay always
/// reflects the most recently started operation that is still open, and falls back to the next
/// one down the stack when the top operation is dismissed.
/// </summary>
public sealed class LoadingCoordinator
{
    private readonly object _sync = new();
    private readonly List<LoadingOperationState> _stack = new();

    public event Action? Changed;

    public LoadingOperationState? Current
    {
        get
        {
            lock (_sync)
            {
                return _stack.Count > 0 ? _stack[^1] : null;
            }
        }
    }

    public IReadOnlyList<LoadingOperationState> ActiveOperations
    {
        get
        {
            lock (_sync)
            {
                return _stack.ToArray();
            }
        }
    }

    /// <summary>
    /// Starts a new tracked operation and returns a handle the caller uses to report progress.
    /// </summary>
    /// <param name="title">Short operation name, e.g. "Gaming Mode".</param>
    /// <param name="status">Initial human-readable status message describing real work.</param>
    /// <param name="indeterminate">True when exact progress cannot be known for this operation.</param>
    /// <param name="cancellable">True only when the underlying operation can actually be cancelled.</param>
    /// <param name="isGamingRelated">True only when reaching completion should show the Gaming Mode completion state.</param>
    public LoadingHandle Begin(string title, string status, bool indeterminate = true, bool cancellable = false, bool isGamingRelated = false)
    {
        var state = new LoadingOperationState
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            Status = status,
            IsIndeterminate = indeterminate,
            Progress = 0,
            IsCancellable = cancellable,
            IsGamingRelated = isGamingRelated
        };

        lock (_sync)
        {
            _stack.Add(state);
        }

        Raise();
        return new LoadingHandle(this, state.Id);
    }

    internal void Update(string id, string? status, int? progress, bool? indeterminate)
    {
        lock (_sync)
        {
            var op = _stack.Find(o => o.Id == id);
            if (op is null) return;
            if (status is not null) op.Status = status;
            if (progress is not null) op.Progress = Math.Clamp(progress.Value, 0, 100);
            if (indeterminate is not null) op.IsIndeterminate = indeterminate.Value;
        }

        Raise();
    }

    internal void Complete(string id, string? completionStatus)
    {
        lock (_sync)
        {
            var op = _stack.Find(o => o.Id == id);
            if (op is null) return;
            op.IsCompleted = true;
            op.IsIndeterminate = false;
            op.Progress = 100;
            if (completionStatus is not null) op.Status = completionStatus;
        }

        Raise();
    }

    internal void Fail(string id, string errorMessage)
    {
        lock (_sync)
        {
            var op = _stack.Find(o => o.Id == id);
            if (op is null) return;
            op.IsFailed = true;
            op.IsIndeterminate = false;
            op.ErrorMessage = errorMessage;
        }

        Raise();
    }

    /// <summary>Removes an operation from the stack, e.g. after the overlay has shown its completion/failure state.</summary>
    public void Dismiss(string id)
    {
        lock (_sync)
        {
            _stack.RemoveAll(o => o.Id == id);
        }

        Raise();
    }

    private void Raise() => Changed?.Invoke();
}

/// <summary>
/// Caller-facing token for one loading operation. Disposing a handle that was never completed or
/// failed removes it as a safety net (e.g. an unexpected early return) without leaving a stale
/// overlay behind; a handle that already reached Complete/Fail is left for the UI to dismiss.
/// </summary>
public sealed class LoadingHandle : IDisposable
{
    private readonly LoadingCoordinator _coordinator;
    private readonly string _id;
    private bool _finished;

    internal LoadingHandle(LoadingCoordinator coordinator, string id)
    {
        _coordinator = coordinator;
        _id = id;
    }

    public string Id => _id;

    public bool IsFailed => FindState()?.IsFailed ?? false;
    public string ErrorMessage => FindState()?.ErrorMessage ?? string.Empty;

    private LoadingOperationState? FindState()
    {
        foreach (var state in _coordinator.ActiveOperations)
        {
            if (state.Id == _id) return state;
        }

        return null;
    }

    public void Update(string? status = null, int? progress = null, bool? indeterminate = null) =>
        _coordinator.Update(_id, status, progress, indeterminate);

    public void Complete(string? completionStatus = null)
    {
        _finished = true;
        _coordinator.Complete(_id, completionStatus);
    }

    public void Fail(string errorMessage)
    {
        _finished = true;
        _coordinator.Fail(_id, errorMessage);
    }

    public void Dispose()
    {
        if (!_finished) _coordinator.Dismiss(_id);
    }
}
