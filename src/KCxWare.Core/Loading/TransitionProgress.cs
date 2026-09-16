namespace KCxWare.Core.Loading;

public sealed record TransitionProgress(string OperationId, int Percent, string Status, bool FinalVerification = false);

public interface ITransitionProgressReporter
{
    void Report(TransitionProgress progress);
}
