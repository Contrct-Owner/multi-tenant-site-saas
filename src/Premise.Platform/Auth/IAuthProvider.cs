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
/// Optional capability (ADR 14): providers that manage an org directory and
/// its invitations. WorkOS implements all of it (it delivers and tracks the
/// invitation emails); the local dev provider carries an in-memory version so
/// the day-zero flows run offline. Role INTENT never travels here - grants
/// are internal (ADR 6); the provider only knows who belongs where.
/// </summary>
public interface IOrganizationDirectory
{
    Task<string> CreateOrganizationAsync(string name, CancellationToken ct = default);

    /// <summary>Record provider-side membership so future SSO/AuthKit logins carry the org.</summary>
    Task AddMemberAsync(
        string externalOrgId,
        string externalUserId,
        CancellationToken ct = default
    );

    /// <summary>Provider creates, emails, and tracks the invitation. Returns its id.</summary>
    Task<string> SendInvitationAsync(
        string externalOrgId,
        string email,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<PendingInvitation>> ListInvitationsAsync(
        string externalOrgId,
        CancellationToken ct = default
    );

    Task RevokeInvitationAsync(string invitationId, CancellationToken ct = default);
}

public sealed record PendingInvitation(
    string Id,
    string Email,
    string State,
    DateTimeOffset ExpiresAt
);
