using Premise.Platform.Messaging;

namespace Premise.Modules.Storage;

public sealed class PendingUploadService(IServiceProvider services)
    : PerOrgSweepService<ExpirePendingUploads>(services)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(1);
}
