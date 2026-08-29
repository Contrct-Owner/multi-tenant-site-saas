using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Premise.Platform.Data;

/// <summary>
/// Sets app.org_id on the Postgres session when a connection opens, so RLS
/// policies (USING org_id = current_setting('app.org_id')::uuid) apply.
///
/// Stateless singleton on purpose: the tenant is read from the CURRENT
/// ModuleDbContext instance via the event data. Capturing an ITenantContext at
/// options-build time is a bug - DbContext options can be cached from the
/// first (startup/codegen) scope, freezing an empty tenant forever.
///
/// When no tenant is set the variable stays unset; current_setting(..., true)
/// returns NULL and policies match nothing: fail closed. Npgsql's pool reset
/// (DISCARD ALL) clears the variable when the connection is returned.
/// </summary>
public sealed class TenantSessionInterceptor : DbConnectionInterceptor
{
    public static readonly TenantSessionInterceptor Instance = new();

    private TenantSessionInterceptor() { }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (OrgIdOf(eventData) is { } orgId)
        {
            using var cmd = MakeCommand(connection, orgId);
            cmd.ExecuteNonQuery();
        }
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        if (OrgIdOf(eventData) is { } orgId)
        {
            await using var cmd = MakeCommand(connection, orgId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static Guid? OrgIdOf(ConnectionEndEventData eventData) =>
        eventData.Context is ModuleDbContext { Tenant.OrgId: { } orgId } ? orgId.Value : null;

    private static DbCommand MakeCommand(DbConnection connection, Guid orgId)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.org_id', $1, false)";
        var p = cmd.CreateParameter();
        p.Value = orgId.ToString();
        cmd.Parameters.Add(p);
        return cmd;
    }
}
