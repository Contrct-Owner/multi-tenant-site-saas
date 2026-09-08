using Premise.Platform.Kernel;

namespace Premise.Contracts;

/// <summary>Lifecycle check before serving a bundle containing published files.</summary>
public interface IReportPublishedFiles
{
    Task<bool> AreCleanAsync(OrgId org, Guid[] ids, CancellationToken ct);
}
