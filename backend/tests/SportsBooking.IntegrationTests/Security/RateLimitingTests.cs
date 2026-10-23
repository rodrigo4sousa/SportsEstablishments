using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SportsBooking.Application.Common;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Security;

/// <summary>
/// US03: login, registration and refresh are rate limited per client IP. The client IP comes from
/// <c>X-Forwarded-For</c> only when the connection is from a configured proxy, so a spoofed header changes nothing.
/// </summary>
public sealed class RateLimitingTests(ApiFactory factory)
{
    private const int Limit = 3;
    private const string TrustedProxy = "10.0.0.2";
    private const string TrustedNetworkMember = "10.1.2.3";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> StartApp()
    {
        var app = factory.CreateIsolatedApp($"us03_rl_{Guid.NewGuid():N}", new Dictionary<string, string?>
        {
            ["RateLimiting:Auth:LoginPermitLimit"] = $"{Limit}",
            ["RateLimiting:Auth:RegisterPermitLimit"] = $"{Limit}",
            ["RateLimiting:Auth:RefreshPermitLimit"] = $"{Limit}",
            ["ForwardedHeaders:KnownProxies:0"] = TrustedProxy,
            ["ForwardedHeaders:KnownNetworks:0"] = "10.1.0.0/16",
        });
        _ = app.Server;
        return app;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        WebApplicationFactory<Program> app,
        string path,
        string connectionAddress,
        string? forwardedFor = null)
    {
        using var client = app.CreateBrowserClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path).From(connectionAddress);
        if (path != "/api/auth/refresh")
        {
            request.Content = JsonContent.Create(new { email = "ninguem@exemplo.pt", password = "palavra-passe" });
        }

        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return await client.SendAsync(request, Ct);
    }

    /// <summary>Uses up the limit of one client; none of these requests is rate limited.</summary>
    private static async Task ExhaustAsync(WebApplicationFactory<Program> app, string path, string connectionAddress, Func<int, string?>? forwardedFor = null)
    {
        for (var request = 0; request < Limit; request++)
        {
            using var response = await SendAsync(app, path, connectionAddress, forwardedFor?.Invoke(request));
            response.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }
    }

    private static async Task ShouldBeTooManyRequestsAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        response.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldNotBeNull().ShouldBeGreaterThan(TimeSpan.Zero);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("status").GetInt32().ShouldBe(429);
        problem.GetProperty("code").GetString().ShouldBe(ErrorCodes.TooManyRequests);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/register")]
    [InlineData("/api/auth/refresh")]
    public async Task Each_auth_endpoint_answers_429_problem_details_after_the_limit_from_one_address(string path)
    {
        await using var app = StartApp();
        await ExhaustAsync(app, path, "203.0.113.10");

        using var limited = await SendAsync(app, path, "203.0.113.10");
        using var otherClient = await SendAsync(app, path, "203.0.113.11");

        await ShouldBeTooManyRequestsAsync(limited);
        otherClient.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Each_endpoint_has_its_own_limit()
    {
        await using var app = StartApp();
        await ExhaustAsync(app, "/api/auth/login", "203.0.113.12");

        using var register = await SendAsync(app, "/api/auth/register", "203.0.113.12");

        register.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Spoofed_forwarded_for_from_an_untrusted_connection_does_not_change_the_partition()
    {
        await using var app = StartApp();
        await ExhaustAsync(app, "/api/auth/login", "203.0.113.20", request => $"198.51.100.{request + 1}");

        using var limited = await SendAsync(app, "/api/auth/login", "203.0.113.20", "198.51.100.99");

        await ShouldBeTooManyRequestsAsync(limited);
    }

    [Fact]
    public async Task Spoofed_forwarded_for_cannot_use_up_another_clients_limit()
    {
        await using var app = StartApp();
        await ExhaustAsync(app, "/api/auth/login", "203.0.113.21", _ => "203.0.113.22");

        using var victim = await SendAsync(app, "/api/auth/login", "203.0.113.22");

        victim.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData(TrustedProxy)]
    [InlineData(TrustedNetworkMember)]
    public async Task A_trusted_proxy_forwards_the_client_address_and_each_client_has_its_own_limit(string proxy)
    {
        await using var app = StartApp();
        await ExhaustAsync(app, "/api/auth/login", proxy, _ => "198.51.100.10");

        using var limited = await SendAsync(app, "/api/auth/login", proxy, "198.51.100.10");
        using var otherClient = await SendAsync(app, "/api/auth/login", proxy, "198.51.100.11");

        await ShouldBeTooManyRequestsAsync(limited);
        otherClient.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Ipv6_clients_share_a_limit_per_64_bit_network()
    {
        await using var app = StartApp();
        await ExhaustAsync(app, "/api/auth/login", "2001:db8:1:1::1");

        using var sameNetwork = await SendAsync(app, "/api/auth/login", "2001:db8:1:1::abcd");
        using var otherNetwork = await SendAsync(app, "/api/auth/login", "2001:db8:1:2::1");

        await ShouldBeTooManyRequestsAsync(sameNetwork);
        otherNetwork.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }
}
