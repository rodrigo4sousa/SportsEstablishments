using System.Net;
using Microsoft.Extensions.Options;

namespace SportsBooking.Api.Security;

/// <summary>
/// Reverse proxies whose <c>X-Forwarded-For</c> is believed (US03). None by default: the client address is then the
/// connection's, and a forwarded header sent by anyone else is ignored, so it cannot change the rate-limit partition.
/// No proxy is trusted today. Production is planned (T06, ADR 0009, proposed) to trust only the Next.js service, and only after it
/// rewrites <c>X-Forwarded-For</c> (<c>ForwardedHeaders__KnownProxies__0</c> or <c>ForwardedHeaders__KnownNetworks__0</c>); until then leave both empty.
/// </summary>
internal sealed class TrustedProxyOptions
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>Single addresses, e.g. <c>172.18.0.5</c>.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Networks in CIDR notation, e.g. <c>172.18.0.0/16</c>.</summary>
    public string[] KnownNetworks { get; set; } = [];
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

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
