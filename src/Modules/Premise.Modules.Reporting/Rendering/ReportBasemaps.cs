using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

namespace Premise.Modules.Reporting.Rendering;

/// <summary>Deployment-owned XYZ raster providers. Tenant/browser URLs never enter this client.</summary>
public sealed class ReportBasemaps : IDisposable
{
    private readonly Dictionary<string, Provider> providers;
    private readonly HttpClient client;

    public ReportBasemaps(IConfiguration configuration, IHttpClientFactory clients)
    {
        providers = configuration
            .GetSection("Reports:Basemaps")
            .GetChildren()
            .Select(section =>
            {
                var provider = new Provider(
                    section.Key,
                    section["Url"] ?? "",
                    section["Attribution"] ?? ""
                );
                if (
                    provider.Id.Length > 80
                    || provider.Attribution.Length is 0 or > 500
                    || !provider.Url.Contains("{z}")
                    || !provider.Url.Contains("{x}")
                    || !provider.Url.Contains("{y}")
                )
                    throw new InvalidOperationException(
                        "Report basemap requires an ID, XYZ URL and attribution."
                    );
                var uri = provider.Tile(0, 0, 0);
                if (
                    uri.Scheme != "https"
                    || !uri.IsDefaultPort
                    || uri.UserInfo.Length != 0
                    || uri.Fragment.Length != 0
                    || Uri.CheckHostName(uri.Host) != UriHostNameType.Dns
                    || provider.Tile(1, 2, 3).Authority != uri.Authority
                )
                    throw new InvalidOperationException(
                        "Report basemap must use a public HTTPS DNS host on port 443."
                    );
                return provider;
            })
            .ToDictionary(p => p.Id, StringComparer.Ordinal);
        client = clients.CreateClient(ClientName);
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Premise-Reports/1.0");
    }

    public const string ClientName = "report-basemaps";

    public static SocketsHttpHandler CreateHandler() =>
        new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = async (context, ct) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
                if (addresses.Length == 0 || addresses.Any(a => !IsPublic(a)))
                    throw new HttpRequestException(
                        "Report basemap resolved to a prohibited address."
                    );
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    // Connect to the validated addresses, avoiding a second DNS resolution/rebinding gap.
                    await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };

    public IReadOnlyCollection<Provider> All => providers.Values;

    public Provider? Find(string? id) => id is null ? null : providers.GetValueOrDefault(id);

    public async Task<byte[]> ReadTileAsync(
        Provider provider,
        int zoom,
        int x,
        int y,
        CancellationToken ct
    )
    {
        if (!providers.TryGetValue(provider.Id, out var configured) || configured != provider)
            throw new InvalidOperationException("Unknown report basemap.");
        using var response = await client.GetAsync(
            provider.Tile(zoom, x, y),
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > ReportImages.MaxEncodedBytes)
            throw new InvalidDataException("Map tile exceeds byte limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await ReportImages.ReadAsync(stream, ct, preserveLossless: true);
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (b.Length == 16)
            return (b[0] & 0xe0) == 0x20 // global unicast only; excludes local/mapped/multicast/NAT64
                && !(b[0] == 0x20 && b[1] == 0x01 && b[2] < 2) // protocol assignments, including Teredo
                && !(b[0] == 0x20 && b[1] == 0x02) // 6to4 can encapsulate a private IPv4 destination
                && !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8);
        return b[0] is not (0 or 10 or 127)
            && b[0] < 224
            && !(b[0] == 100 && b[1] is >= 64 and <= 127)
            && !(b[0] == 169 && b[1] == 254)
            && !(b[0] == 172 && b[1] is >= 16 and <= 31)
            && !(b[0] == 192 && (b[1] == 168 || b[1] == 0 || b[1] == 2))
            && !(b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100))
            && !(b[0] == 203 && b[1] == 0 && b[2] == 113);
    }

    public void Dispose() => client.Dispose();

    public sealed record Provider(string Id, string Url, string Attribution)
    {
        public Uri Tile(int z, int x, int y) =>
            new(
                Url.Replace("{z}", z.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Replace("{x}", x.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Replace("{y}", y.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                UriKind.Absolute
            );
    }
}
