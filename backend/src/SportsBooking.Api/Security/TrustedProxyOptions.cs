using System.Net;
using Microsoft.Extensions.Options;

namespace SportsBooking.Api.Security;

/// <summary>
/// Which reverse proxies decide the client address and scheme (<c>X-Forwarded-For</c>, <c>X-Forwarded-Proto</c>; US03, T06). By default
/// none: the address is the connection's and the headers are ignored. Either name the proxies (<see cref="KnownProxies"/>,
/// <see cref="KnownNetworks"/>) or, for a host whose proxies have no addresses you can list (Render), set <see cref="TrustAnyProxy"/>.
/// <para>
/// With <see cref="TrustAnyProxy"/> the API is reachable directly, so a caller can send its own <c>X-Forwarded-For</c>: an IP-based rate
/// limit is then best-effort only, and the real protection against guessing passwords is the Identity account lockout (<c>LoginLockout</c>).
/// Provisional, see "Pontos a rever no T06" in docs/backlog.md. <see cref="ForwardLimit"/> has to match the real number of proxies in front
/// of the API, which is not documented by the hosts: docs/deployment/environment-variables.md says how to find it.
/// </para>
/// </summary>
internal sealed class TrustedProxyOptions
{
    public const string SectionName = "ForwardedHeaders";

    public const int MaximumForwardLimit = 10;

    /// <summary>Single addresses, e.g. <c>172.18.0.5</c>.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Networks in CIDR notation, e.g. <c>172.18.0.0/16</c>.</summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>Believe the headers whoever connects (env <c>ForwardedHeaders__TrustAnyProxy</c>). See the class comment for what this gives up.</summary>
    public bool TrustAnyProxy { get; set; }

    /// <summary>How many entries of the headers are read, from the right (env <c>ForwardedHeaders__ForwardLimit</c>). The rightmost are the hardest to forge.</summary>
    public int ForwardLimit { get; set; } = 1;

    public bool TrustsAProxy => TrustAnyProxy || KnownProxies.Length > 0 || KnownNetworks.Length > 0;
}

internal sealed class TrustedProxyOptionsValidator : IValidateOptions<TrustedProxyOptions>
{
    public ValidateOptionsResult Validate(string? name, TrustedProxyOptions options)
    {
        var failures = new List<string>();

        var badProxies = options.KnownProxies.Where(proxy => !IPAddress.TryParse(proxy, out _)).ToList();
        if (badProxies.Count > 0)
        {
            failures.Add($"ForwardedHeaders:KnownProxies entries must be IP addresses like 172.18.0.5: {string.Join(", ", badProxies)}");
        }

        var badNetworks = options.KnownNetworks.Where(network => !IPNetwork.TryParse(network, out _)).ToList();
        if (badNetworks.Count > 0)
        {
            failures.Add($"ForwardedHeaders:KnownNetworks entries must be CIDR networks like 172.18.0.0/16: {string.Join(", ", badNetworks)}");
        }

        if (options.TrustAnyProxy && (options.KnownProxies.Length > 0 || options.KnownNetworks.Length > 0))
        {
            failures.Add("ForwardedHeaders:TrustAnyProxy cannot be combined with ForwardedHeaders:KnownProxies or KnownNetworks.");
        }

        if (options.ForwardLimit is < 1 or > TrustedProxyOptions.MaximumForwardLimit)
        {
            failures.Add($"ForwardedHeaders:ForwardLimit must be between 1 and {TrustedProxyOptions.MaximumForwardLimit}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
