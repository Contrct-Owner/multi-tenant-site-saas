using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using NpgsqlTypes;

namespace Premise.Platform.Spatial;

/// <summary>
/// The one MVT renderer every point data layer shares (ADR 50 §4): tile
/// envelope, scope predicates, clustering below the layer's point zoom,
/// <c>ST_AsMVTGeom</c>/<c>ST_AsMVT</c>. A layer supplies only its facts
/// query - the columns <c>id</c>, <c>name</c>, <c>status</c>, <c>path</c>
/// (ltree) and <c>location</c> (geography) - and this does the rest, so the
/// projection decision and the scope predicate live in one place.
/// </summary>
public static class DataLayerTiles
{
    public const int Extent = 4096;
    public const int Buffer = 64;
    public const int MaxZoom = 22;

    /// <summary>
    /// The site facts every layer starts from, as SQL: the org's sites that
    /// have a location. Named here, below every module, so a layer in any
    /// module joins its own facts to <c>s.id</c> without spelling Tenancy's
    /// schema - the same way the audit sink is one shared, known table.
    /// Yields <c>id, name, status, path, time_zone, location</c>; expects
    /// <c>@org</c>.
    /// </summary>
    public const string SitesSql = """
        SELECT s.id, s.name, s.status, s.path, s.time_zone, s.location
        FROM tenancy.sites s
        WHERE s.org_id = @org AND s.location IS NOT NULL
        """;

    public static bool InRange(int z, int x, int y) =>
        z is >= 0 and <= MaxZoom && x >= 0 && y >= 0 && x < (1L << z) && y < (1L << z);

    /// <summary>
    /// Renders one tile. <paramref name="factsSql"/> may use <c>@org</c> and
    /// any parameter in <paramref name="extra"/>; the names <c>z</c>,
    /// <c>x</c>, <c>y</c>, <c>paths</c> and <c>under</c> are the renderer's.
    /// </summary>
    public static async Task<byte[]> RenderAsync(
        DatabaseFacade database,
        string layerName,
        int minPointZoom,
        string factsSql,
        DataLayerTileRequest request,
        IEnumerable<NpgsqlParameter>? extra = null,
        CancellationToken ct = default
    )
    {
        var parameters = new List<NpgsqlParameter>
        {
            new("z", request.Z),
            new("x", request.X),
            new("y", request.Y),
            new("org", request.Org.Value),
        };
        if (extra is not null)
            parameters.AddRange(extra);

        var predicates = "";
        if (request.ScopePaths is { } paths)
        {
            // ltree "is descendant of or equal": the same predicate InScope() translates to
            predicates += " AND f.path <@ ANY(@paths::ltree[])";
            parameters.Add(
                new NpgsqlParameter("paths", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    Value = paths.ToArray(),
                }
            );
        }
        if (request.UnderPath is { } under)
        {
            predicates += " AND f.path <@ @under::ltree";
            parameters.Add(new NpgsqlParameter("under", under));
        }

        // features: single points, or clusters on a grid of one eighth of the tile
        var features =
            request.Z >= minPointZoom
                ? $"""
                    SELECT ST_AsMVTGeom(i.geom, b.geom, {Extent}, {Buffer}, true) AS geom,
                           i.id::text AS id, i.name, i.status, 1 AS count
                    FROM inside i, bounds b
                    """
                : $"""
                    SELECT ST_AsMVTGeom(ST_Centroid(ST_Collect(i.geom)), b.geom, {Extent}, {Buffer}, true) AS geom,
                           NULL::text AS id, NULL::text AS name, NULL::text AS status, count(*)::int AS count
                    FROM inside i, bounds b
                    GROUP BY b.geom, ST_SnapToGrid(i.geom, (ST_XMax(b.geom) - ST_XMin(b.geom)) / 8.0)
                    """;

        var sql = $"""
            WITH bounds AS (SELECT ST_TileEnvelope(@z, @x, @y) AS geom),
            facts AS ({factsSql}),
            inside AS (
                SELECT f.id, f.name, f.status::text AS status, ST_Transform(f.location::geometry, 3857) AS geom
                FROM facts f, bounds b
                WHERE ST_Intersects(f.location, ST_Transform(b.geom, 4326)::geography){predicates}
            ),
            features AS ({features})
            SELECT COALESCE(ST_AsMVT(f, '{layerName}', {Extent}, 'geom'), ''::bytea) AS "Value"
            FROM features f
            WHERE f.geom IS NOT NULL
            """;

        return await database
            .SqlQueryRaw<byte[]>(sql, parameters.Cast<object>().ToArray())
            .SingleAsync(ct);
    }
}
