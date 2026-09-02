using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Premise.Modules.Tenancy.Data;
using Premise.Platform.Messaging;
using Wolverine;

namespace Premise.Modules.Tenancy.Sites;

/// <summary>
/// The horizon-roll enumerator (ADR 24/28): daily, cross-org platform work
/// that itself touches no tenant data - it reads the platform-global org list
/// and enqueues one tenant-scoped message per org. Runs in the worker role.
/// </summary>
public sealed class HorizonRollService(IServiceProvider services)
    : PerOrgSweepService<RollOccurrenceHorizons>(services)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(24);
}
