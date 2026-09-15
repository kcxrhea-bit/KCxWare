namespace KCxWare.Core.Loading;

/// <summary>
/// Mutable snapshot of one in-flight loading operation tracked by <see cref="LoadingCoordinator"/>.
/// </summary>
public sealed class LoadingOperationState
{
    public required string Id { get; init; }
    public required string Title { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsIndeterminate { get; set; } = true;
    public int Progress { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsFailed { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsCancellable { get; set; }
    public bool IsGamingRelated { get; set; }
}
