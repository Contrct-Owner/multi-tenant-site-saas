using Microsoft.EntityFrameworkCore;
using Premise.Contracts;
using Premise.Modules.Storage.Data;
using Premise.Platform.Kernel;

namespace Premise.Modules.Storage;

public sealed class ReportPublishedFiles(StorageDbContext db) : IReportPublishedFiles
{
    public async Task<bool> AreCleanAsync(OrgId org, Guid[] ids, CancellationToken ct) =>
        ids.Length > 0
        && await db.Files.CountAsync(
            x =>
                x.OrgId == org
                && ids.Contains(x.Id)
                && x.Origin == "report"
                && x.Status == FileStatus.Clean,
            ct
        ) == ids.Length;
}
