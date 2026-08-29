using Microsoft.Extensions.Options;
using Premise.Platform.Auth;
using WorkOS;

namespace Premise.Integrations.WorkOS;

public sealed class WorkOSOptions
{
    public required string ApiKey { get; set; }
    public required string ClientId { get; set; }

    /// <summary>
    /// Override to point at the WorkOS emulator (@workos/emulate) for local
    /// dev and adapter smoke tests - e.g. http://localhost:4100 with the
    /// emulator's sk_test_default key. Null = the real https://api.workos.com.
    /// </summary>
    public string? ApiBaseUrl { get; set; }
}

/// <summary>
/// The built-in WorkOS implementation of the auth seam (ADR 14): AuthKit
/// hosted UI for authentication, org directory capability for tenant
/// lifecycle. Grants, entitlements, and audit stay internal (ADRs 6/10/12) -
/// WorkOS is identity and org directory only.
/// </summary>
public sealed class WorkOSAuthProvider : IAuthProvider, IOrganizationDirectory, IUserProvisioning
{
    private readonly WorkOSClient _client;
    private readonly string _clientId;
    private readonly string _baseUrl;

    public WorkOSAuthProvider(IOptions<WorkOSOptions> options)
    {
        _clientId = options.Value.ClientId;
        _baseUrl = options.Value.ApiBaseUrl ?? "https://api.workos.com";
        var clientOptions = new global::WorkOS.WorkOSOptions
        {
            ApiKey = options.Value.ApiKey,
            ClientId = options.Value.ClientId,
        };
        if (options.Value.ApiBaseUrl is { } baseUrl)
            clientOptions.ApiBaseURL = baseUrl;
        _client = new WorkOSClient(clientOptions);
    }

    public string Name => "workos";

    public string BuildAuthorizationUrl(
        string redirectUri,
        string state,
        string? loginHint = null,
        string? orgHint = null,
        string? screenHint = null
    ) =>
        AuthorizationUrlBuilder.BuildAuthKitAuthorizationUrl(
            _baseUrl,
            _clientId,
            new AuthKitAuthorizationUrlOptions
            {
                RedirectUri = redirectUri,
                State = state,
                LoginHint = loginHint,
                OrganizationId = orgHint,
                ScreenHint = screenHint, // "sign-up" sends AuthKit to registration
            }
        );

    public async Task<ExternalIdentity> ExchangeCodeAsync(
        string code,
        string redirectUri,
        CancellationToken ct = default
    )
    {
        var response = await _client.UserManagement.AuthenticateWithCodeAsync(
            new AuthenticateWithCodeOptions { Code = code },
            cancellationToken: ct
        );
        var user = response.User;
        var name = string.Join(
            ' ',
            new[] { user.FirstName, user.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))
        );
        return new ExternalIdentity(
            Provider: Name,
            Subject: user.Id,
            Email: user.Email,
            Name: string.IsNullOrWhiteSpace(name) ? null : name,
            ExternalOrgId: response.OrganizationId
        );
    }

    public async Task EnsureUserAsync(string email, CancellationToken ct = default)
    {
        try
        {
            await _client.UserManagement.CreateAsync(
                new UserManagementCreateOptions { Email = email, EmailVerified = true },
                cancellationToken: ct
            );
        }
        catch (ApiException) // already exists: sign-up is idempotent
        { }
    }

    public async Task UpdateOrganizationNameAsync(
        string externalOrgId,
        string name,
        CancellationToken ct = default
    ) =>
        await _client.Organizations.UpdateAsync(
            externalOrgId,
            new OrganizationsUpdateOptions { Name = name },
            cancellationToken: ct
        );

    public async Task AddMemberAsync(
        string externalOrgId,
        string externalUserId,
        CancellationToken ct = default
    ) =>
        await _client.OrganizationMembership.CreateAsync(
            new OrganizationMembershipCreateOptions
            {
                OrganizationId = externalOrgId,
                UserId = externalUserId,
            },
            cancellationToken: ct
        );

    public async Task<string> SendInvitationAsync(
        string externalOrgId,
        string email,
        CancellationToken ct = default
    )
    {
        var invitation = await _client.UserManagement.SendInvitationAsync(
            new UserManagementSendInvitationOptions
            {
                OrganizationId = externalOrgId,
                Email = email,
                ExpiresInDays = 7,
            },
            cancellationToken: ct
        );
        return invitation.Id;
    }

    public async Task<IReadOnlyList<PendingInvitation>> ListInvitationsAsync(
        string externalOrgId,
        CancellationToken ct = default
    )
    {
        var invitations = await _client.UserManagement.ListInvitationsAsync(
            new UserManagementListInvitationsOptions { OrganizationId = externalOrgId },
            cancellationToken: ct
        );
        return
        [
            .. (invitations.Data ?? []).Select(i => new PendingInvitation(
                i.Id,
                i.Email,
                i.State.ToString(),
                i.ExpiresAt
            )),
        ];
    }

    public async Task RevokeInvitationAsync(string invitationId, CancellationToken ct = default) =>
        await _client.UserManagement.RevokeInvitationAsync(invitationId, cancellationToken: ct);

    public async Task<string> CreateOrganizationAsync(string name, CancellationToken ct = default)
    {
        var org = await _client.Organizations.CreateAsync(
            new OrganizationsCreateOptions { Name = name },
            cancellationToken: ct
        );
        return org.Id;
    }
}
