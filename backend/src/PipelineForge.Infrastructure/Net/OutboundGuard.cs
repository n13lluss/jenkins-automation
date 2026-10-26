using System.Net;
using System.Net.Sockets;

namespace PipelineForge.Infrastructure.Net;

public sealed class OutboundOptions
{
    /// <summary>Allow private/loopback addresses (e.g. a self-hosted Bitbucket on the LAN).</summary>
    public bool AllowPrivateNetworks { get; set; }
    /// <summary>Allow plain http (local development only).</summary>
    public bool AllowHttp { get; set; }
    /// <summary>When set, only these hosts may be called. Listed hosts may resolve to private addresses.</summary>
    public List<string> AllowedHosts { get; set; } = [];
    /// <summary>Hosts that may resolve to private addresses without restricting other hosts (e.g. an internal Bitbucket).</summary>
    public List<string> PrivateHosts { get; set; } = [];
    /// <summary>Use the system proxy (HTTPS_PROXY). Address checks then apply to the target host name only.</summary>
    public bool UseProxy { get; set; }
}

public sealed class OutboundBlockedException(string message) : Exception(message);

/// <summary>
/// SSRF protection for every call to a user-supplied URL (SCM, Jenkins, AI). The address is
/// checked at connect time, after DNS resolution, so DNS rebinding cannot bypass it.
/// Redirects are never followed.
/// </summary>
public static class OutboundGuard
{
    private static readonly IPAddress[] MetadataAddresses =
    [
        IPAddress.Parse("169.254.169.254"),
        IPAddress.Parse("169.254.170.2"),
        IPAddress.Parse("100.100.100.200"),
        IPAddress.Parse("fd00:ec2::254"),
    ];

    public static void ValidateUrl(Uri uri, OutboundOptions options)
    {
        if (!uri.IsAbsoluteUri) throw new OutboundBlockedException("URL must be absolute.");
        if (uri.Scheme != Uri.UriSchemeHttps && !(options.AllowHttp && uri.Scheme == Uri.UriSchemeHttp))
            throw new OutboundBlockedException("Only https URLs are allowed.");
        if (!string.IsNullOrEmpty(uri.UserInfo)) throw new OutboundBlockedException("Credentials in URLs are not allowed.");
        if (options.AllowedHosts.Count > 0 && !options.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            throw new OutboundBlockedException($"Host {uri.Host} is not in the outbound allow-list.");
        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal) && IsBlocked(literal, options, listed: IsListed(uri.Host, options)))
            throw new OutboundBlockedException("That address is not allowed.");
    }

    private static bool IsListed(string host, OutboundOptions options) =>
        options.AllowedHosts.Concat(options.PrivateHosts).Any(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase));

    public static bool IsBlocked(IPAddress ip, OutboundOptions options, bool listed)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (MetadataAddresses.Any(m => m.Equals(ip))) return true;
        if (IsUnspecifiedOrMulticast(ip)) return true;
        if (listed || options.AllowPrivateNetworks) return false;
        return IsPrivate(ip);
    }

    private static bool IsUnspecifiedOrMulticast(IPAddress ip)
    {
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6) return ip.IsIPv6Multicast;
        var b = ip.GetAddressBytes();
        return b[0] == 0 || b[0] >= 224;
    }

    public static bool IsPrivate(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b6 = ip.GetAddressBytes();
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (b6[0] & 0xFE) == 0xFC;
        }
        var b = ip.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
            || (b[0] == 192 && b[1] == 0 && b[2] == 0)
            || (b[0] == 198 && (b[1] == 18 || b[1] == 19));
    }

    public static SocketsHttpHandler CreateHandler(OutboundOptions options) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = options.UseProxy,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = async (context, ct) =>
        {
            var host = context.DnsEndPoint.Host;
            var listed = IsListed(host, options);
            var addresses = IPAddress.TryParse(host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host, ct);
            // With a proxy the connection goes to the proxy, which is trusted configuration.
            var allowed = options.UseProxy ? addresses : addresses.Where(a => !IsBlocked(a, options, listed)).ToArray();
            if (allowed.Length == 0) throw new OutboundBlockedException($"Host {host} resolves only to blocked addresses.");
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };
}
