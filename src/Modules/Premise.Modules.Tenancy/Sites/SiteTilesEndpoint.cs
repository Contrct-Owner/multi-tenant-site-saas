using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Premise.Contracts;
using Premise.Modules.Tenancy.Data;
using Premise.Platform.Kernel;
using Wolverine.Attributes;
using Wolverine.Http;

namespace Premise.Modules.Tenancy.Sites;

/// <summary>
/// The <c>sites</c> data layer as vector tiles (ADR 50 §4): one gate call,
/// scope applied inside the SQL, then <c>ST_AsMVTGeom</c>/<c>ST_AsMVT</c>.
/// The tile is the scope-filtered answer, so what a user can see on the map
/// is decided where everything else they can see is. Below
/// <see cref="MinPointZoom"/> the tile carries clusters (a <c>count</c>
/// property, no id); at or above it, individual sites with their id, name and
/// status. Tiles are private: scope differs per principal, so no shared cache
/// ever holds one; a strong ETag lets a browser skip the bytes it already has.
/// </summary>
public static class SiteTilesEndpoint
{
    public const string Layer = "sites";

    /// <summary>Below this zoom, clusters; from here up, individual sites.</summary>
    public const int MinPointZoom = 9;

    private const int Extent = 4096;
    private const int Buffer = 64;

    [Transactional(typeof(TenancyDbContext))]
    [WolverineGet("/api/tiles/sites/{z}/{x}/{y}")]
    [ProducesResponseType(
        typeof(byte[]),
        StatusCodes.Status200OK,
        "application/vnd.mapbox-vector-tile"
    )]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public static async Task<IResult> Get(
        int z,
        int x,
        int y,
        Guid? under,
        HttpContext http,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (z is < 0 or > 22 || x < 0 || y < 0 || x >= (1L << z) || y >= (1L << z))
            return Results.BadRequest(new { error = "tile out of range" });

        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.SitesRead, ct);
        if (gate is not GateOutcome.Allowed { Scope: var scope })
            return gate.ToResult();

        // scope never fails - a principal with no scope simply sees an empty map
        OrgId? org = scope switch
        {
            NodeScope.EntireOrg entire => entire.Org,
            NodeScope.Subtrees subtrees => subtrees.Org,
            _ => null,
        };
        string[]? paths = scope is NodeScope.Subtrees limited ? limited.Paths.ToArray() : null;
        if (org is not { } owner)
            return Results.NoContent();

        // the console's chosen scope node (ADR 49's `under`), inside the grant
        string? underPath = null;
        if (under is { } nodeId)
        {
            var node = await db.HierarchyNodes.FirstOrDefaultAsync(n => n.Id == nodeId, ct);
            if (node is null || !scope.Covers(node.Path.ToString()))
                return Results.NoContent();
            underPath = node.Path.ToString();
        }

        var bytes = await RenderAsync(db, z, x, y, owner, paths, underPath, ct);
        if (bytes.Length == 0)
            return Results.NoContent();

        var etag = $"\"{Convert.ToHexStringLower(SHA256.HashData(bytes))[..32]}\"";
        http.Response.Headers.CacheControl = "private, max-age=60";
        http.Response.Headers.ETag = etag;
        if (http.Request.Headers.IfNoneMatch.Any(v => v == etag))
            return Results.StatusCode(StatusCodes.Status304NotModified);
        return Results.Bytes(bytes, "application/vnd.mapbox-vector-tile");
    }

    private static async Task<byte[]> RenderAsync(
        TenancyDbContext db,
        int z,
        int x,
        int y,
        OrgId org,
        string[]? paths,
        string? underPath,
        CancellationToken ct
    )
    {
        var parameters = new List<NpgsqlParameter>
        {
            new("z", z),
            new("x", x),
            new("y", y),
            new("org", org.Value),
        };
        var predicates = "";
        if (paths is not null)
        {
            // ltree "is descendant of or equal": the same predicate InScope() translates to
            predicates += " AND s.path <@ ANY(@paths::ltree[])";
            parameters.Add(
                new NpgsqlParameter("paths", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    Value = paths,
                }
            );
        }
        if (underPath is not null)
        {
            predicates += " AND s.path <@ @under::ltree";
            parameters.Add(new NpgsqlParameter("under", underPath));
        }

        // features: single sites, or clusters on a grid of one eighth of the tile
        var features =
            z >= MinPointZoom
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
            inside AS (
                SELECT s.id, s.name, s.status, ST_Transform(s.location::geometry, 3857) AS geom
                FROM tenancy.sites s, bounds b
                WHERE s.org_id = @org
                  AND s.location IS NOT NULL
                  AND ST_Intersects(s.location, ST_Transform(b.geom, 4326)::geography){predicates}
            ),
            features AS ({features})
            SELECT COALESCE(ST_AsMVT(f, '{Layer}', {Extent}, 'geom'), ''::bytea) AS "Value"
            FROM features f
            WHERE f.geom IS NOT NULL
            """;

        return await db
            .Database.SqlQueryRaw<byte[]>(sql, parameters.Cast<object>().ToArray())
            .SingleAsync(ct);
    }
}
