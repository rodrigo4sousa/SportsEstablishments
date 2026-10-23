using System.Net;
using SportsBooking.Api.Security;

namespace SportsBooking.UnitTests.Security;

/// <summary>US03: rate-limit settings, trusted proxies and the client partition.</summary>
public sealed class RateLimitingOptionsTests
{
    [Fact]
    public void Default_rate_limits_are_valid()
    {
        new AuthRateLimitOptionsValidator().Validate(null, new AuthRateLimitOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Rate_limits_need_a_positive_window_and_at_least_one_request()
    {
        var result = new AuthRateLimitOptionsValidator().Validate(null, new AuthRateLimitOptions
        {
            Window = TimeSpan.Zero,
            LoginPermitLimit = 0,
            RegisterPermitLimit = -1,
            RefreshPermitLimit = 0,
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldNotBeNull().Count().ShouldBe(4);
    }

    [Fact]
    public void No_trusted_proxy_by_default_is_valid()
    {
        new TrustedProxyOptionsValidator().Validate(null, new TrustedProxyOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Trusted_proxies_are_addresses_and_networks_in_cidr_notation()
    {
        var valid = new TrustedProxyOptions { KnownProxies = ["172.18.0.5", "fd00::5"], KnownNetworks = ["172.18.0.0/16", "fd00::/64"] };

        new TrustedProxyOptionsValidator().Validate(null, valid).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("caddy", null)]
    [InlineData("172.18.0.0/16", null)]
    [InlineData(null, "172.18.0.5")]
    [InlineData(null, "172.18.0.0/40")]
    public void Anything_else_is_invalid_and_named_in_the_error(string? proxy, string? network)
    {
        var options = new TrustedProxyOptions
        {
            KnownProxies = proxy is null ? [] : [proxy],
            KnownNetworks = network is null ? [] : [network],
        };

        var result = new TrustedProxyOptionsValidator().Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(proxy ?? network!);
    }

    [Theory]
    [InlineData("203.0.113.10", "203.0.113.10")]
    [InlineData("::ffff:203.0.113.10", "203.0.113.10")]
    [InlineData("2001:db8:1:1::1", "2001:db8:1:1::/64")]
    [InlineData("2001:db8:1:1:ffff:ffff:ffff:ffff", "2001:db8:1:1::/64")]
    public void Clients_are_partitioned_by_ipv4_address_or_ipv6_64_bit_network(string address, string partition)
    {
        ClientPartition.For(IPAddress.Parse(address)).ShouldBe(partition);
    }

    [Fact]
    public void A_request_without_an_address_falls_into_one_shared_partition()
    {
        ClientPartition.For(null).ShouldBe("unknown");
    }
}
