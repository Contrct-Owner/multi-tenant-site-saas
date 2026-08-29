namespace Premise.Platform.Kernel;

/// <summary>
/// The set of hierarchy nodes a principal's grant applies over - the third gate
/// (scope filters, never errors). Repositories and query helpers REQUIRE a
/// NodeScope argument so "forgot to filter by location" is not expressible.
/// Subtree membership resolves via materialized ltree path prefixes (ADR 1/3),
/// never id lists.
/// </summary>
public abstract record NodeScope
{
    /// <summary>No access. Queries must return empty, not throw.</summary>
    public sealed record None : NodeScope;

    /// <summary>Every node in the org's authoritative hierarchy.</summary>
    public sealed record EntireOrg(OrgId Org) : NodeScope;

    /// <summary>Access to the subtrees rooted at the given ltree paths.</summary>
    public sealed record Subtrees(OrgId Org, IReadOnlyList<string> Paths) : NodeScope;

    public static readonly NodeScope Nothing = new None();
}
