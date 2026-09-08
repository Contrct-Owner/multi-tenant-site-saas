using Premise.Platform.Kernel;

namespace Premise.Contracts;

/// <summary>Rehydrate a current member for background authorization; never restore impersonation.</summary>
public interface IReportRequester
{
    Task<Principal.User?> GetActiveAsync(OrgId org, Guid userId, CancellationToken ct = default);
}
