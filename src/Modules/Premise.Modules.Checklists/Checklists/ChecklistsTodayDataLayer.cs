using Premise.Modules.Checklists.Data;
using Premise.Platform.Kernel;
using Premise.Platform.Spatial;

namespace Premise.Modules.Checklists.Checklists;

/// <summary>
/// The <c>checklists-today</c> layer (ADR 50 §3): how far each site is
/// through today's lists, on the SITE's business date (ADR 26 kind 3 - the
/// date is taken in the site's own zone, exactly as the today endpoint does).
/// A module's data layer joins its own facts to the shared site facts; it
/// never touches Tenancy's tables by any other route.
/// </summary>
public sealed class ChecklistsTodayDataLayer(ChecklistsDbContext db) : IDataLayer
{
    public string Name => "checklists-today";
    public string Title => "Checklists today";
    public string Description =>
        "Progress through today's checklists at each site, on its own clock.";
    public string Capability => Capabilities.ChecklistsComplete;
    public int MinPointZoom => 9;

    public IReadOnlyList<DataLayerStatus> Statuses { get; } =
    [
        new("complete", "Complete", "#22c55e"),
        new("partial", "In progress", "#f59e0b"),
        new("pending", "Not started", "#ef4444"),
        new("none", "No lists", "#71717a"),
    ];

    private const string Facts = $"""
        WITH sites AS ({DataLayerTiles.SitesSql}),
        applying AS (
            SELECT s.id AS site_id, t.id AS template_id, cardinality(t.items) AS items,
                   (now() AT TIME ZONE s.time_zone)::date AS business_date
            FROM sites s
            JOIN checklists.templates t
              ON t.org_id = @org AND (t.scope_path IS NULL OR s.path <@ t.scope_path::ltree)
        ),
        progress AS (
            SELECT a.site_id, a.items,
                   (SELECT count(*) FROM checklists.item_checks c
                     WHERE c.template_id = a.template_id AND c.site_id = a.site_id
                       AND c.business_date = a.business_date) AS done
            FROM applying a
        ),
        per_site AS (SELECT site_id, sum(items) AS items, sum(done) AS done FROM progress GROUP BY site_id)
        SELECT s.id, s.name,
               CASE
                   WHEN p.site_id IS NULL OR p.items = 0 THEN 'none'
                   WHEN p.done = 0 THEN 'pending'
                   WHEN p.done >= p.items THEN 'complete'
                   ELSE 'partial'
               END AS status,
               s.path, s.location
        FROM sites s
        LEFT JOIN per_site p ON p.site_id = s.id
        """;

    public Task<byte[]> RenderAsync(DataLayerTileRequest request, CancellationToken ct = default) =>
        DataLayerTiles.RenderAsync(db.Database, Name, MinPointZoom, Facts, request, ct: ct);
}
