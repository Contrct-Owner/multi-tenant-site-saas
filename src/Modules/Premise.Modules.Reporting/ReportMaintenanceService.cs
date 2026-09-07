using Premise.Platform.Messaging;

namespace Premise.Modules.Reporting;

public sealed class ReportMaintenanceService(IServiceProvider services)
    : PerOrgSweepService<MaintainReports>(services)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(1);
}
