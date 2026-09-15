namespace KCxWare.Core.Loading;

/// <summary>
/// The neon accent a milestone label uses once it activates. Values map directly onto
/// KCxWare's existing Cyan/Magenta theme brushes - see App.xaml.
/// </summary>
public enum MilestoneAccent
{
    Cyan,
    Magenta,
    Combined
}

public readonly record struct Milestone(int Threshold, MilestoneAccent Accent);

/// <summary>
/// Pure, framework-free progress math for the KCx loading overlay's 20/40/60/80/100 milestone row.
/// Alternates cyan/magenta per the KCxWare visual language, with a combined cyan+magenta treatment at 100%.
/// </summary>
public static class MilestoneCalculator
{
    public static readonly IReadOnlyList<Milestone> All = new[]
    {
        new Milestone(20, MilestoneAccent.Cyan),
        new Milestone(40, MilestoneAccent.Magenta),
        new Milestone(60, MilestoneAccent.Cyan),
        new Milestone(80, MilestoneAccent.Magenta),
        new Milestone(100, MilestoneAccent.Combined)
    };

    /// <summary>
    /// A milestone is only illuminated by real, measured progress. Indeterminate operations
    /// never light up milestones - doing so would fabricate progress that was never measured.
    /// </summary>
    public static bool IsActive(int threshold, int progress, bool indeterminate) =>
        !indeterminate && progress >= threshold;
}
