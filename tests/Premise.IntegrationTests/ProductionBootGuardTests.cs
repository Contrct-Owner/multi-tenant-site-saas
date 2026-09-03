using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Premise.IntegrationTests;

/// <summary>
/// docs/production.md promises the image refuses to start in Production
/// with any dev-only adapter selected. The maturity review found two seams
/// (object storage, malware scanning) registered unconditionally behind that
/// promise, and a third (secrets) with no production registration path at
/// all. This proves the promise seam by seam: each case takes a
/// production-valid configuration and flips ONE seam back to its dev
/// adapter, and the host must refuse before it starts. The refusal happens
/// while the composition root runs, so no database or fixture is needed.
/// </summary>
public class ProductionBootGuardTests
{
    private static readonly string KeyPath = Path.Combine(
        Path.GetTempPath(),
        "premise-boot-guard-keys"
    );

    /// <summary>Every seam on its production adapter (registration is lazy; nothing connects).</summary>
    private static Dictionary<string, string?> ProductionValid() =>
        new()
        {
            ["ROLE"] = "api",
            ["ConnectionStrings:premise"] = "Host=localhost;Database=unused",
            ["DataProtection:KeyPath"] = KeyPath,
            ["Auth:Provider"] = "workos",
            ["Storage:Provider"] = "s3",
            ["Storage:S3:BucketName"] = "unused",
            ["Scanner:Provider"] = "clamav",
            ["Scanner:ClamAv:Host"] = "unused",
            ["Secrets:Provider"] = "kms",
            ["Secrets:Kms:KeyId"] = "unused",
            ["Billing:Provider"] = "stripe",
            ["Notifications:Transport"] = "smtp",
            ["Notifications:Sms"] = "off",
        };

    [Theory]
    [InlineData(
        "Storage:Provider",
        "local",
        "Storage:Provider 'local' is not valid for Production"
    )]
    [InlineData(
        "Scanner:Provider",
        "eicar",
        "Scanner:Provider 'eicar' is not valid for Production"
    )]
    [InlineData(
        "Secrets:Provider",
        "local",
        "Secrets:Provider 'local' is not valid for Production"
    )]
    [InlineData("Auth:Provider", "local", "Auth:Provider 'local' is not valid for Production")]
    [InlineData("Billing:Provider", "local", "Billing:Provider 'local' is dev/test only")]
    [InlineData(
        "Notifications:Transport",
        "local",
        "Notifications:Transport 'local' is dev/test only"
    )]
    [InlineData("Notifications:Sms", "local", "Notifications:Sms 'local' is dev/test only")]
    [InlineData("DataProtection:KeyPath", null, "DataProtection:KeyPath is required in Production")]
    public void Production_refuses_to_boot_with_a_dev_adapter(
        string key,
        string? devValue,
        string refusal
    )
    {
        var settings = ProductionValid();
        settings[key] = devValue;

        var refused = BootRefusal(settings);

        Assert.NotNull(refused);
        Assert.Contains(refusal, refused.Message);
    }

    [Fact]
    public void An_unknown_role_is_refused_before_anything_else()
    {
        // a typo in a manifest used to start a host that mapped nothing
        var settings = ProductionValid();
        settings["ROLE"] = "apu";

        Assert.Contains("ROLE 'apu' is not a role this image runs", BootRefusal(settings)!.Message);
    }

    [Fact]
    public void An_unknown_provider_is_refused_in_every_environment()
    {
        // a typo must not fall through to a default adapter
        var settings = ProductionValid();
        settings["Storage:Provider"] = "s4";

        Assert.Contains("Storage:Provider 's4'", BootRefusal(settings)!.Message);
    }

    /// <summary>The composition root's refusal, or null when the host built.</summary>
    private static Exception? BootRefusal(Dictionary<string, string?> settings)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            foreach (var (key, value) in settings)
                b.UseSetting(key, value);
        });
        var thrown = Record.Exception(() => factory.Server);
        for (var e = thrown; e is not null; e = e.InnerException)
            if (e is InvalidOperationException)
                return e;
        return thrown;
    }
}
