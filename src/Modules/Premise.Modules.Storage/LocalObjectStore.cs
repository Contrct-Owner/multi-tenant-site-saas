using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Premise.Platform.Storage;

namespace Premise.Modules.Storage;

/// <summary>
/// Filesystem adapter for dev/tests: honors the SAME ticket contract as the
/// cloud adapters - clients PUT to a tokenized URL served by this API (see
/// LocalStoreEndpoints), so the whole ticket flow is exercised in-process.
/// </summary>
public sealed class LocalObjectStore(IConfiguration configuration) : IObjectStore
{
    private readonly string _root =
        configuration["Storage:LocalRoot"] ?? Path.Combine(Path.GetTempPath(), "premise-objects");

    // tickets are SIGNED, not remembered (ADR 52): a fleet of api replicas
    // shares the master key and the store directory, so the replica that
    // receives the PUT need not be the one that issued the ticket - the same
    // property a cloud adapter's presigned URL has
    private readonly byte[] _secret = Convert.FromBase64String(
        configuration["Secrets:LocalMasterKey"]
            ?? throw new InvalidOperationException(
                "Secrets:LocalMasterKey is required to sign local upload tickets"
            )
    );

    public ValueTask<UploadTicket> CreateUploadTicketAsync(
        string key,
        string contentType,
        long maxBytes,
        CancellationToken ct = default
    )
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(15);
        return ValueTask.FromResult(
            new UploadTicket(
                $"/objects/upload/{Sign(key, maxBytes, expires)}",
                "PUT",
                new Dictionary<string, string> { ["Content-Type"] = contentType },
                expires
            )
        );
    }

    public ValueTask<Uri> GetDownloadUrlAsync(
        string key,
        TimeSpan ttl,
        CancellationToken ct = default
    ) =>
        ValueTask.FromResult(
            new Uri(
                $"/objects/download/{Sign(key, 0, DateTimeOffset.UtcNow.Add(ttl))}",
                UriKind.Relative
            )
        );

    public (string key, long maxBytes)? Redeem(string token)
    {
        var parts = token.Split('.', 2);
        if (parts.Length != 2)
            return null;
        byte[] payload;
        byte[] signature;
        try
        {
            payload = Convert.FromBase64String(FromUrl(parts[0]));
            signature = Convert.FromBase64String(FromUrl(parts[1]));
        }
        catch (FormatException)
        {
            return null;
        }
        if (
            !CryptographicOperations.FixedTimeEquals(
                HMACSHA256.HashData(_secret, payload),
                signature
            )
        )
            return null;
        var fields = System.Text.Encoding.UTF8.GetString(payload).Split('\n');
        if (
            fields.Length != 3
            || !long.TryParse(fields[1], out var maxBytes)
            || !long.TryParse(fields[2], out var expiresUnix)
            || DateTimeOffset.FromUnixTimeSeconds(expiresUnix) < DateTimeOffset.UtcNow
        )
            return null;
        return (fields[0], maxBytes);
    }

    private string Sign(string key, long maxBytes, DateTimeOffset expires)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes(
            $"{key}\n{maxBytes}\n{expires.ToUnixTimeSeconds()}"
        );
        return $"{ToUrl(payload)}.{ToUrl(HMACSHA256.HashData(_secret, payload))}";
    }

    private static string ToUrl(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string FromUrl(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        return padded + new string('=', (4 - padded.Length % 4) % 4);
    }

    public ValueTask<long?> GetLengthAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return ValueTask.FromResult<long?>(new FileInfo(PathFor(key)).Length);
        }
        catch (FileNotFoundException)
        {
            return ValueTask.FromResult<long?>(null);
        }
        catch (DirectoryNotFoundException)
        {
            return ValueTask.FromResult<long?>(null);
        }
    }

    public ValueTask<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
        ValueTask.FromResult<Stream>(File.OpenRead(PathFor(key)));

    public async ValueTask WriteAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken ct = default
    )
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
    }

    public ValueTask DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            File.Delete(PathFor(key));
        }
        catch (DirectoryNotFoundException) { } // Already absent, including an intent whose upload never started.
        return ValueTask.CompletedTask;
    }

    public string PathFor(string key)
    {
        // keys are server-generated, but never trust a path join
        var path = Path.GetFullPath(
            Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar))
        );
        return path.StartsWith(Path.GetFullPath(_root))
            ? path
            : throw new InvalidOperationException("key escapes the storage root");
    }
}

/// <summary>EICAR-detecting scanner for dev/tests (ADR 19); forks plug real scanning here.</summary>
public sealed class EicarScanner : IVirusScanner
{
    private const string Eicar =
        @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    public async ValueTask<ScanVerdict> ScanAsync(Stream content, CancellationToken ct = default)
    {
        using var reader = new StreamReader(content, leaveOpen: false);
        var buffer = new char[128 * 1024];
        var read = await reader.ReadBlockAsync(buffer, ct);
        return new string(buffer, 0, read).Contains(Eicar)
            ? ScanVerdict.Infected
            : ScanVerdict.Clean;
    }
}
