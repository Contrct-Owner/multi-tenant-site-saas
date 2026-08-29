using System.Text;
using Premise.Platform.Auth;

namespace Premise.Modules.Identity.Auth;

/// <summary>
/// Dev/test-only IAuthProvider (ADR 14's base contract, nothing more): the
/// "authorization server" is the redirect itself - the code IS the login hint.
/// Lets the template run and its test suites authenticate with no external
/// account. Program blocks it in Production.
/// </summary>
public sealed class LocalAuthProvider : IAuthProvider
{
    public string Name => "local";

    public string BuildAuthorizationUrl(
        string redirectUri,
        string state,
        string? loginHint = null,
        string? orgHint = null
    )
    {
        var email = loginHint ?? "dev@premise.local";
        var code = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}|{orgHint}"));
        return $"{redirectUri}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}";
    }

    public Task<ExternalIdentity> ExchangeCodeAsync(
        string code,
        string redirectUri,
        CancellationToken ct = default
    )
    {
        var parts = Encoding.UTF8.GetString(Convert.FromBase64String(code)).Split('|', 2);
        var email = parts[0];
        var orgHint = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
        return Task.FromResult(
            new ExternalIdentity(
                Name,
                Subject: email,
                Email: email,
                Name: email.Split('@')[0],
                ExternalOrgId: orgHint
            )
        );
    }
}
