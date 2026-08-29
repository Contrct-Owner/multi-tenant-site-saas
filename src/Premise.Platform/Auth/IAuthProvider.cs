namespace Premise.Platform.Auth;

/// <summary>
/// The OIDC-generic authentication seam (ADR 14). Any provider satisfies this
/// base contract; richer behavior lives in optional capability interfaces the
/// host feature-detects ("provider is IOrganizationDirectory"). WorkOS
/// implements everything; the local dev provider implements only the base.
/// </summary>
public interface IAuthProvider
{
    /// <summary>Stable name used in config and audit ("workos", "local").</summary>
    string Name { get; }

    /// <summary>
    /// Where to send the browser to authenticate. loginHint is the OIDC
    /// login_hint (pre-filled email); orgHint pre-selects an org connection.
    /// </summary>
    string BuildAuthorizationUrl(
        string redirectUri,
        string state,
        string? loginHint = null,
        string? orgHint = null
    );

    /// <summary>Exchange the callback code for the authenticated identity.</summary>
    Task<ExternalIdentity> ExchangeCodeAsync(
        string code,
        string redirectUri,
        CancellationToken ct = default
    );
}

/// <summary>
/// What a provider asserts about who authenticated. ExternalOrgId is the
/// provider-side organization (e.g. a WorkOS org id) when the login happened
/// through an org connection - used to map into a Premise org.
/// </summary>
public sealed record ExternalIdentity(
    string Provider,
    string Subject,
    string Email,
    string? Name,
    string? ExternalOrgId
);

/// <summary>
/// Optional capability (ADR 14): providers that manage an org directory.
/// Used by tenant lifecycle to link a Premise org to a provider org.
/// </summary>
public interface IOrganizationDirectory
{
    Task<string> CreateOrganizationAsync(string name, CancellationToken ct = default);
}
