using System.Linq.Expressions;
using NetTopologySuite.Geometries;

namespace Premise.Platform.Spatial;

/// <summary>
/// Builds the "inside any of these envelopes" predicate a bounding box turns
/// into (ADR 49). EF cannot translate <c>envelopes.Any(e =&gt; location.Intersects(e))</c>
/// over a client-side list of geometries, so the OR chain is assembled as an
/// expression tree; each term is an <c>ST_Intersects</c> the GiST index serves.
/// </summary>
public static class SpatialPredicates
{
    public static Expression<Func<T, bool>> IntersectsAny<T>(
        Expression<Func<T, Point?>> location,
        IReadOnlyList<Geometry> envelopes
    )
    {
        if (envelopes.Count == 0)
            throw new ArgumentException("at least one envelope", nameof(envelopes));
        var parameter = location.Parameters[0];
        var point = location.Body;
        var intersects = typeof(Geometry).GetMethod(
            nameof(Geometry.Intersects),
            [typeof(Geometry)]
        )!;
        Expression? any = null;
        foreach (var envelope in envelopes)
        {
            // member access on a captured object is what EF's parameter extraction
            // treats as a closure, so each envelope becomes a query parameter
            // (plan cache friendly) rather than an inlined geometry literal
            var captured = Expression.Property(
                Expression.Constant(new Captured(envelope)),
                nameof(Captured.Value)
            );
            var term = Expression.Call(point, intersects, captured);
            any = any is null ? term : Expression.OrElse(any, term);
        }
        var notNull = Expression.NotEqual(point, Expression.Constant(null, typeof(Point)));
        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(notNull, any!), parameter);
    }

    private sealed record Captured(Geometry Value);
}
