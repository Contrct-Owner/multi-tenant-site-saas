using Microsoft.EntityFrameworkCore;
using Premise.Contracts;
using Premise.Modules.Storage.Data;
using Premise.Platform.Data;
using Premise.Platform.Kernel;
using Premise.Platform.Messaging;
using Premise.Platform.Storage;
using Wolverine;
using Wolverine.Attributes;

namespace Premise.Modules.Storage;

public static class ExpirePendingUploadsHandler
{
    [Transactional(typeof(StorageDbContext))]
    public static async Task Handle(
        ExpirePendingUploads message,
        ITenantContext tenant,
        StorageDbContext db,
        IObjectStore store,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var org =
            tenant.OrgId
            ?? throw new InvalidOperationException("upload cleanup requires an organization");
        var cutoff = DateTimeOffset.UtcNow.AddHours(-1);
        var files = await db
            .Files.Where(f =>
                !f.LegalHold
                && f.CreatedAt < cutoff
                && (
                    f.Status == FileStatus.PendingUpload
                    || f.Status == FileStatus.Quarantined
                    || f.Status == FileStatus.Uploaded
                )
            )
            .OrderBy(f => f.CreatedAt)
            .Take(100)
            .ToListAsync(ct);
        foreach (var file in files)
        {
            await db.TakeAsync(file.Id, ct);
            await db.Entry(file).ReloadAsync(ct);
            if (
                file.LegalHold
                || file.CreatedAt >= cutoff
                || file.Status
                    is not (
                        FileStatus.PendingUpload
                        or FileStatus.Quarantined
                        or FileStatus.Uploaded
                    )
            )
                continue;
            await store.DeleteAsync(file.Key, ct);
            if (file.PreviewKey is { } preview)
                await store.DeleteAsync(preview, ct);
            file.Status = FileStatus.Erased;
            file.PreviewKey = null;
            await bus.AuditAsync(
                org,
                AuditActor.System,
                "file.erased",
                new { file.Id, source = "expired-upload" }
            );
        }
        await db.SaveChangesAsync(ct);
    }
}
