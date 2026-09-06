using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.IO;
using Premise.Contracts;
using Premise.Modules.Spatial.Data;
using Premise.Platform.Kernel;
using Wolverine.Attributes;

namespace Premise.Modules.Spatial;

/// <summary>
/// Spatial's slice of the offboarding export (ADR 25): every layer, trash
/// included, with its features as WKT so the org leaves with shapes it can
/// load anywhere. A module without an exporter drops out of the export
/// silently; an architecture test enforces its presence.
/// </summary>
public sealed class SpatialExporter(SpatialDbContext db) : IOrgDataExporter
{
    public string Section => "spatial";

    public async Task<string> ExportJsonAsync(OrgId org, CancellationToken ct = default)
    {
        var layers = await db
            .Layers.IgnoreQueryFilters()
            .Where(l => l.OrgId == org)
            .Select(l => new
            {
                l.Id,
                l.Name,
                l.Kind,
                l.Style,
                l.NodeId,
                l.HierarchyId,
                Path = l.Path.ToString(),
                l.Version,
                l.CreatedAt,
                l.UpdatedAt,
                l.DeletedAt,
            })
            .ToListAsync(ct);
        var writer = new WKTWriter();
        var features = (
            await db
                .Features.IgnoreQueryFilters()
                .Where(f => f.OrgId == org)
                .Select(f => new
                {
                    f.Id,
                    f.LayerId,
                    f.Geom,
                    f.Properties,
                    f.CreatedAt,
                })
                .ToListAsync(ct)
        ).Select(f => new
        {
            f.Id,
            f.LayerId,
            Wkt = writer.Write(f.Geom),
            f.Properties,
            f.CreatedAt,
        });
        return JsonSerializer.Serialize(
            new { layers, features },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }
        );
    }
}

/// <summary>Envelope-tenanted purge of the org's overlays.</summary>
public static class PurgeOrgSpatialHandler
{
    [Transactional]
    public static async Task Handle(
        PurgeOrgSpatial _,
        SpatialDbContext db,
        ITenantContext tenant,
        CancellationToken ct
    )
    {
        var org =
            tenant.OrgId
            ?? throw new InvalidOperationException("purge arrived with no tenant on the envelope");
        await db.Features.IgnoreQueryFilters().Where(f => f.OrgId == org).ExecuteDeleteAsync(ct);
        await db.Layers.IgnoreQueryFilters().Where(l => l.OrgId == org).ExecuteDeleteAsync(ct);
    }
}
