using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Premise.Platform.Notifications;

namespace Premise.IntegrationTests;

public class ContactTierTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Issue_deliver_redeem_yields_contact_principal()
    {
        // Issue: a member of Org A invites a contact
        var member = await fixture.LoginAsync(ApiFixture.UserA);
        var issue = await member.PostAsJsonAsync(
            "/contact-links",
            new { email = "visitor@example.com" }
        );
        issue.EnsureSuccessStatusCode();

        // Deliver: through the outbox -> Wolverine handler -> transport
        var catcher = (LocalMailCatcher)
            fixture.Factory.Services.GetRequiredService<INotificationTransport>();
        EmailMessage? mail = null;
        for (var i = 0; i < 50 && mail is null; i++) // outbox delivery is async
        {
            await Task.Delay(100);
            mail = catcher.Sent.FirstOrDefault(m => m.To == "visitor@example.com");
        }
        Assert.NotNull(mail);
        var url = mail!.TextBody.Split('\n')[0].Replace("Follow this link to continue: ", "");

        // Redeem: fresh browser, no account
        var visitor = fixture.GuestClient();
        var redeem = await visitor.GetAsync(new Uri(url).PathAndQuery);
        redeem.EnsureSuccessStatusCode();

        var me = await visitor.GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal("contact", me.GetProperty("tier").GetString());
        Assert.Equal(fixture.OrgA.Value, me.GetProperty("org").GetGuid());
    }

    [Fact]
    public async Task Guest_cannot_issue_contact_links()
    {
        var response = await fixture
            .GuestClient()
            .PostAsJsonAsync("/contact-links", new { email = "x@example.com" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var visitor = fixture.GuestClient();
        var response = await visitor.GetAsync("/contact/redeem?token=CfDJ8-tampered");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Guest_receives_session_cookie()
    {
        var response = await fixture.GuestClient().GetAsync("/me");
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith("premise_guest=")
        );
    }
}
