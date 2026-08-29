using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Premise.Modules.Storage.Data;
using Premise.Platform.Kernel;
using Premise.Platform.Storage;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace Premise.Modules.Storage;

public sealed record CreateFileRequest(string Name, string ContentType, long SizeBytes);

public sealed record SetHoldRequest(bool Hold);

public static class FileEndpoints
{
    private const long MaxUploadBytes = 100 * 1024 * 1024;

    [Transactional(typeof(StorageDbContext))]
    [WolverinePost("/api/files")]
    public static async Task<IResult> Create(
        CreateFileRequest request,
        StorageDbContext db,
        IObjectStore store,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (
            accessor.Current
                is not Principal.User { ActiveOrg: { } org, UserId: var userId } principal
            || !await scopes.CanAsync(principal, Capabilities.FilesManage, ct)
        )
            return Results.Unauthorized();
        if (request.SizeBytes is <= 0 or > MaxUploadBytes)
            return Results.BadRequest(new { error = $"size must be 1..{MaxUploadBytes} bytes" });

        var id = Guid.CreateVersion7();
        // tenant- and region-scoped key layout (ADR 19/35)
        var key = $"{RegionId.Default.Value}/{org.Value}/files/{id}";
        var file = new FileObject
        {
            Id = id,
            OrgId = org,
            Key = key,
            Name = request.Name,
            ContentType = request.ContentType,
            MaxBytes = request.SizeBytes,
            CreatedBy = userId,
        };
        db.Files.Add(file);
        await db.SaveChangesAsync(ct);

        var ticket = await store.CreateUploadTicketAsync(
            key,
            request.ContentType,
            request.SizeBytes,
            ct
        );
        return Results.Ok(new { fileId = id, ticket });
    }

    /// <summary>Client signals the direct upload finished; scanning starts (quarantine until verdict).</summary>
    [Transactional(typeof(StorageDbContext))]
    [WolverinePost("/api/files/{id}/complete")]
    public static async Task<IResult> Complete(
        Guid id,
        StorageDbContext db,
        IObjectStore store,
        IMessageBus bus,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.FilesManage, ct))
            return Results.Unauthorized();
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null)
            return Results.NotFound();
        if (file.Status != FileStatus.PendingUpload)
            return Results.Conflict(new { error = $"file is {file.Status}" });
        if (!await store.ExistsAsync(file.Key, ct))
            return Results.BadRequest(new { error = "no bytes were uploaded for this ticket" });

        file.Status = FileStatus.Uploaded;
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new ScanUploadedFile(file.Id),
            new DeliveryOptions { TenantId = file.OrgId.Value.ToString() }
        );
        return Results.Accepted();
    }

    [Transactional(typeof(StorageDbContext))]
    [WolverineGet("/api/files")]
    public static async Task<IResult> List(
        StorageDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.FilesRead, ct))
            return Results.Unauthorized();
        var files = await db
            .Files.Where(f => f.Status != FileStatus.Erased)
            .OrderByDescending(f => f.CreatedAt)
            .Take(200)
            .Select(f => new
            {
                f.Id,
                f.Name,
                f.ContentType,
                status = f.Status.ToString(),
                f.LegalHold,
                hasPreview = f.PreviewKey != null,
                f.CreatedAt,
            })
            .ToListAsync(ct);
        return Results.Ok(files);
    }

    /// <summary>Authorization happens HERE, before signing - the URL itself is unguarded (ADR 19).</summary>
    [Transactional(typeof(StorageDbContext))]
    [WolverineGet("/api/files/{id}/download")]
    public static async Task<IResult> Download(
        Guid id,
        StorageDbContext db,
        IObjectStore store,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.FilesRead, ct))
            return Results.Unauthorized();
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id, ct);
        // quarantined/pending/erased are all 404: never confirm undownloadable bytes
        if (file is null || file.Status != FileStatus.Clean)
            return Results.NotFound();
        var url = await store.GetDownloadUrlAsync(file.Key, TimeSpan.FromMinutes(5), ct);
        return Results.Ok(new { url = url.ToString(), expiresInSeconds = 300 });
    }

    [Transactional(typeof(StorageDbContext))]
    [WolverinePost("/api/files/{id}/hold")]
    public static async Task<IResult> SetHold(
        Guid id,
        SetHoldRequest request,
        StorageDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.FilesManage, ct))
            return Results.Unauthorized();
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null)
            return Results.NotFound();
        file.LegalHold = request.Hold;
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new Premise.Contracts.RecordDomainAudit(
                request.Hold ? "file.hold_placed" : "file.hold_released",
                System.Text.Json.JsonSerializer.Serialize(new { file.Id, file.Name })
            ),
            new DeliveryOptions { TenantId = file.OrgId.Value.ToString() }
        );
        return Results.NoContent();
    }

    /// <summary>
    /// Auditable erasure (ADR 19): bytes and derivatives go, the row stays as
    /// a tombstone, and the act is a domain event. Legal hold blocks it.
    /// </summary>
    [Transactional(typeof(StorageDbContext))]
    [WolverineDelete("/api/files/{id}")]
    public static async Task<IResult> Erase(
        Guid id,
        StorageDbContext db,
        IObjectStore store,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.FilesManage, ct))
            return Results.Unauthorized();
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null || file.Status == FileStatus.Erased)
            return Results.NotFound();
        if (file.LegalHold)
            return Results.Conflict(new { error = "file is under legal hold" });

        await store.DeleteAsync(file.Key, ct);
        if (file.PreviewKey is { } previewKey)
            await store.DeleteAsync(previewKey, ct);
        file.Status = FileStatus.Erased;
        file.PreviewKey = null;
        await db.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new Premise.Contracts.RecordDomainAudit(
                "file.erased",
                System.Text.Json.JsonSerializer.Serialize(new { file.Id, file.Name })
            ),
            new DeliveryOptions { TenantId = file.OrgId.Value.ToString() }
        );
        return Results.NoContent();
    }
}
