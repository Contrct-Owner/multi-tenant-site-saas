using System.Security.Cryptography;

namespace Premise.Api;

/// <summary>
/// Guests are principals (ADR 7), so they get a session too: an opaque random
/// cookie that is the rate-limit subject (ADR 30) and, later, the CSRF anchor.
/// Not an auth cookie - it identifies a browser, not a person.
/// </summary>
public sealed class GuestSessionMiddleware(RequestDelegate next)
{
    public const string CookieName = "premise_guest";

    public async Task InvokeAsync(HttpContext context)
    {
        if (
            context.User.Identity?.IsAuthenticated != true
            && !context.Request.Cookies.ContainsKey(CookieName)
        )
        {
            context.Response.Cookies.Append(
                CookieName,
                Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    MaxAge = TimeSpan.FromDays(30),
                    IsEssential = true,
                }
            );
        }
        await next(context);
    }
}
