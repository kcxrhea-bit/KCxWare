namespace KCxWare.Core.Models;

/// <summary>
/// Ephemeral, read-only preflight facts used to explain a transition. This is intentionally not
/// persisted in ModeState: KCxWare is a mode switcher, not a machine-inventory database.
/// </summary>
public sealed record MachineCapabilities(
    IReadOnlyList<string> GraphicsVendors,
    IReadOnlyList<string> DevelopmentTools)
{
    public static MachineCapabilities Empty { get; } = new([], []);
}
