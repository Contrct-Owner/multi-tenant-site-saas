using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Premise.Modules.Tenancy.Data;
using Premise.Platform.Kernel;
using Wolverine.Http;

namespace Premise.Modules.Tenancy.Hierarchy;

public sealed record CreateHierarchyRequest(string Name, string[] Levels);

public sealed record CreateNodeRequest(Guid ParentId, string Name);

public sealed record MoveNodeRequest(Guid NewParentId);

public sealed record NodeResponse(Guid Id, Guid? ParentId, string Name, int Depth, string Path);

public static class HierarchyEndpoints
{
    [WolverinePost("/api/hierarchy")]
    public static async Task<IResult> Create(
        CreateHierarchyRequest request,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { ActiveOrg: { } org }
            || !await scopes.CanAsync(accessor.Current, "hierarchy:manage", ct)
        )
            return Results.Unauthorized();
        if (request.Levels.Length == 0)
            return Results.BadRequest(new { error = "at least one level is required" });
        if (await db.Hierarchies.AnyAsync(h => h.IsAuthoritative, ct))
            return Results.Conflict(new { error = "hierarchy already exists" });

        var hierarchy = OrgHierarchy.Create(org, request.Name, request.Levels);
        var root = HierarchyNode.CreateRoot(org, hierarchy.Id, request.Name);
        db.Hierarchies.Add(hierarchy);
        db.HierarchyNodes.Add(root);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { hierarchy.Id, rootNodeId = root.Id });
    }

    [WolverineGet("/api/hierarchy")]
    public static async Task<IResult> Get(TenancyDbContext db, CancellationToken ct)
    {
        var hierarchy = await db.Hierarchies.FirstOrDefaultAsync(h => h.IsAuthoritative, ct);
        if (hierarchy is null)
            return Results.NotFound();
        var nodes = await db
            .HierarchyNodes.Where(n => n.HierarchyId == hierarchy.Id)
            .OrderBy(n => n.Path)
            .Select(n => new NodeResponse(n.Id, n.ParentId, n.Name, n.Depth, n.Path.ToString()))
            .ToListAsync(ct);
        return Results.Ok(
            new
            {
                hierarchy.Id,
                hierarchy.Name,
                hierarchy.Levels,
                nodes,
            }
        );
    }

    [WolverinePost("/api/hierarchy/nodes")]
    public static async Task<IResult> CreateNode(
        CreateNodeRequest request,
        TenancyDbContext db,
        CancellationToken ct
    )
    {
        var parent = await db.HierarchyNodes.FirstOrDefaultAsync(n => n.Id == request.ParentId, ct);
        if (parent is null)
            return Results.NotFound();
        var hierarchy = await db.Hierarchies.FirstAsync(h => h.Id == parent.HierarchyId, ct);
        if (parent.Depth + 1 > hierarchy.Levels.Length)
            return Results.BadRequest(
                new
                {
                    error = $"hierarchy is limited to {hierarchy.Levels.Length} level(s) below the root",
                }
            );

        var node = HierarchyNode.CreateChild(parent, request.Name);
        db.HierarchyNodes.Add(node);
        await db.SaveChangesAsync(ct);
        return Results.Ok(
            new NodeResponse(node.Id, node.ParentId, node.Name, node.Depth, node.Path.ToString())
        );
    }

    /// <summary>
    /// Re-parenting (ADR 2): rewrites the subtree's materialized paths - nodes
    /// and the denormalized site paths - in two set-based statements. History
    /// stays honest because FACT tables stamp paths at write time; projections
    /// join live rows and need no rewrite.
    /// </summary>
    [WolverinePost("/api/hierarchy/nodes/{id}/move")]
    public static async Task<IResult> MoveNode(
        Guid id,
        MoveNodeRequest request,
        TenancyDbContext db,
        CancellationToken ct
    )
    {
        var node = await db.HierarchyNodes.FirstOrDefaultAsync(n => n.Id == id, ct);
        var newParent = await db.HierarchyNodes.FirstOrDefaultAsync(
            n => n.Id == request.NewParentId,
            ct
        );
        if (node is null || newParent is null)
            return Results.NotFound();
        if (node.ParentId is null)
            return Results.BadRequest(new { error = "the root cannot move" });
        // in-memory prefix check (LTree.IsDescendantOf only translates in queries)
        var nodePrefix = node.Path.ToString();
        var parentPath = newParent.Path.ToString();
        if (parentPath == nodePrefix || parentPath.StartsWith(nodePrefix + '.'))
            return Results.BadRequest(new { error = "cannot move a node under its own subtree" });

        var oldPrefix = node.Path.ToString();
        var newPrefix = $"{newParent.Path}.{HierarchyNode.Label(node.Id)}";
        var depthDelta = newParent.Depth + 1 - node.Depth;

        var hierarchy = await db.Hierarchies.FirstAsync(h => h.Id == node.HierarchyId, ct);
        var subtreeDepth = await db
            .HierarchyNodes.Where(n => n.Path.IsDescendantOf(node.Path))
            .MaxAsync(n => n.Depth, ct);
        if (subtreeDepth + depthDelta > hierarchy.Levels.Length)
            return Results.BadRequest(new { error = "move would exceed the hierarchy's depth" });

        node.ParentId = newParent.Id;
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE tenancy.hierarchy_nodes
            SET path = CASE WHEN path = {oldPrefix}::ltree THEN {newPrefix}::ltree
                       ELSE ({newPrefix}::ltree || subpath(path, nlevel({oldPrefix}::ltree))) END,
                depth = depth + {depthDelta}
            WHERE path <@ {oldPrefix}::ltree
            """,
            ct
        );
        await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE tenancy.sites
            SET path = ({newPrefix}::ltree || subpath(path, nlevel({oldPrefix}::ltree)))
            WHERE path <@ {oldPrefix}::ltree
            """,
            ct
        );
        return Results.NoContent();
    }
}
