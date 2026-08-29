using Microsoft.EntityFrameworkCore;
using Premise.Modules.Identity.Data;
using Premise.Platform.Kernel;

namespace Premise.Api;

/// <summary>
/// Suspension enforcement (org lifecycle back half): a Suspended org's
/// members can still sign in, switch orgs, and see /me - but every /api/*
/// call answers 403 org_suspended. Reads the org_directory read model (one
/// indexed PK lookup), which learns of transitions via OrganizationUpserted.
/// </summary>
public sealed class SuspensionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IPrincipalAccessor accessor,
        IdentityDbContext db
    )
    {
        if (
            context.Request.Path.StartsWithSegments("/api")
            && accessor.Current is Principal.User { ActiveOrg: { } org }
        )
        {
            var status = await db
                .OrgDirectory.Where(d => d.OrgId == org)
                .Select(d => d.Status)
                .FirstOrDefaultAsync(context.RequestAborted);
            if (status == "Suspended")
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    new { error = "organization suspended", code = "org_suspended" }
                );
                return;
            }
        }
        await next(context);
    }
}
