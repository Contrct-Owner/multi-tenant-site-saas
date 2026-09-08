using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Premise.Modules.Reporting;
using Premise.Modules.Reporting.Data;
using Premise.Platform.Infra;
using Premise.Platform.Kernel;

namespace Premise.Api;

/// <summary>Explicit replay policies; other endpoints retain duplicate suppression without storing secrets.</summary>
internal static class IdempotencyReplay
{
    public static bool Supports(HttpRequest request) =>
        request.Method == "PUT"
            && request.Path.Value?.Split('/') is ["", "api", "settings", { Length: > 0 }]
        || request.Method == "POST" && request.Path == "/api/reports";

    public static async Task<bool> CanReadAsync(
        HttpContext http,
        IPrincipalAccessor accessor,
        IdempotencyRecord record
    )
    {
        if (
            !Supports(http.Request)
            || record.Body is null
            || accessor.Current is not Principal.User user
        )
            return false;
        var scopes = http.RequestServices.GetRequiredService<IScopeResolver>();
        if (http.Request.Path.StartsWithSegments("/api/settings"))
            return await scopes.ScopeForAsync(user, Capabilities.OrgManage, http.RequestAborted)
                is NodeScope.EntireOrg;
        if (
            !await scopes.CanAsync(user, Capabilities.ReportsGenerate, http.RequestAborted)
            || !await scopes.CanAsync(user, Capabilities.FilesRead, http.RequestAborted)
        )
            return false;
        using var response = JsonDocument.Parse(record.Body);
        if (!response.RootElement.TryGetProperty("id", out var id) || !id.TryGetGuid(out var jobId))
            return false;
        var db = http.RequestServices.GetRequiredService<ReportingDbContext>();
        var job = await db
            .Jobs.AsNoTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(
                x => x.Id == jobId && x.OrgId == record.OrgId && x.RequestedBy == user.UserId,
                http.RequestAborted
            );
        return job is not null
            && await http
                .RequestServices.GetRequiredService<ReportAccess>()
                .CanReadJobAsync(
                    job,
                    user.UserId,
                    http.RequestServices.GetRequiredService<ReportRegistry>(),
                    http.RequestAborted
                );
    }
}
