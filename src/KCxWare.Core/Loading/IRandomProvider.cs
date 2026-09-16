namespace KCxWare.Core.Loading;

/// <summary>Testable seam around random selection so tests can control/verify it without real randomness.</summary>
public interface IRandomProvider
{
    /// <summary>Returns a value in [minInclusive, maxExclusive), matching <see cref="Random.Next(int,int)"/>.</summary>
    int Next(int minInclusive, int maxExclusive);
}

/// <summary>Production implementation backed by the standard, non-cryptographic <see cref="Random.Shared"/>.</summary>
public sealed class SystemRandomProvider : IRandomProvider
{
    public int Next(int minInclusive, int maxExclusive) => Random.Shared.Next(minInclusive, maxExclusive);
}
