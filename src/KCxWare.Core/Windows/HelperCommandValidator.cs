namespace KCxWare.Core.Windows;

/// <summary>
/// Narrow allowlist validation for KCxWare.Helper's power commands. Deliberately accepts exactly
/// two shapes - a single "restart" argument or a single "shutdown" argument - and nothing else:
/// no extra flags, no shell parameters, no arbitrary command text. Kept as a small pure function
/// so it is testable without administrator rights (unlike the rest of KCxWare.Helper, which
/// requires elevation to even start).
/// </summary>
public static class HelperCommandValidator
{
    public static bool IsValidPowerCommand(IReadOnlyList<string> args) =>
        args.Count == 1 &&
        (string.Equals(args[0], "restart", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(args[0], "shutdown", StringComparison.OrdinalIgnoreCase));

    public static bool IsRestartCommand(IReadOnlyList<string> args) =>
        IsValidPowerCommand(args) && string.Equals(args[0], "restart", StringComparison.OrdinalIgnoreCase);
}
