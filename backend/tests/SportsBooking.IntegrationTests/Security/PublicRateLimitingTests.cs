using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SportsBooking.Application.Common;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Security;

/// <summary>
/// T06: the public read endpoints (club search, localities, a club's page and its slots) are rate limited per client IP, with one limit
/// shared by the four. Normal browsing never reaches it; a burst gets 429 ProblemDetails with Retry-After and a traceId. The client IP
/// is decided like for the auth limits: <c>X-Forwarded-For</c> counts only from a configured proxy.
/// </summary>
public sealed class PublicRateLimitingTests(ApiFactory factory)
{
    private const int Limit = 4;
    private const string TrustedProxy = "10.0.0.2";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> StartApp(bool smallLimit)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = TrustedProxy,
        };
        if (smallLimit)
        {
            settings["RateLimiting:Public:ReadPermitLimit"] = $"{Limit}";
        }
        else
        {
            // The shipped default: the test host raises it for every other test, so this one asks for the real value.
            settings["RateLimiting:Public:ReadPermitLimit"] = "120";
        }

        var app = factory.CreateIsolatedApp($"t06_rl_{Guid.NewGuid():N}", settings);
        _ = app.Server;
        return app;
    }

    private static async Task<HttpResponseMessage> GetAsync(WebApplicationFactory<Program> app, string path, string connectionAddress, string? forwardedFor = null)
    {
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path).From(connectionAddress);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return await client.SendAsync(request, Ct);
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

    private static async Task ExhaustAsync(WebApplicationFactory<Program> app, string path, string connectionAddress, Func<int, string?>? forwardedFor = null)
    {
        for (var request = 0; request < Limit; request++)
        {
            using var response = await GetAsync(app, path, connectionAddress, forwardedFor?.Invoke(request));
            response.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }
    }

    [Fact]
    public async Task Normal_browsing_is_never_limited()
    {
        await using var app = StartApp(smallLimit: false);
        var club = await app.CreateClubAsync();
        var pitch = await app.CreatePitchAsync(club.Id);
        var date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // A thorough visit in one minute: ten searches, ten club pages, twenty slot lists (new date or pitch each time) and the localities.
        var paths = new List<string> { "/api/localities" };
        paths.AddRange(Enumerable.Range(0, 10).Select(index => $"/api/clubs?sport=padel&locality=Porto&page={index + 1}"));
        paths.AddRange(Enumerable.Range(0, 10).Select(_ => $"/api/clubs/{club.Slug}"));
        paths.AddRange(Enumerable.Range(0, 20).Select(_ => $"/api/clubs/{club.Slug}/pitches/{pitch.Id}/slots?date={date}"));

        foreach (var path in paths)
        {
            using var response = await GetAsync(app, path, "203.0.113.50");
            response.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests, path);
        }
    }

    [Theory]
    [InlineData("/api/clubs")]
    [InlineData("/api/localities")]
    [InlineData("/api/clubs/um-clube")]
    [InlineData("/api/clubs/um-clube/pitches/0199f7a0-0000-7000-8000-0000000000d1/slots?date=2026-05-04")]
    public async Task A_burst_gets_429_problem_details_with_retry_after_and_another_client_is_unaffected(string path)
    {
        await using var app = StartApp(smallLimit: true);
        await ExhaustAsync(app, path, "203.0.113.10");

        using var limited = await GetAsync(app, path, "203.0.113.10");
        using var otherClient = await GetAsync(app, path, "203.0.113.11");

        await ShouldBeTooManyRequestsAsync(limited);
        otherClient.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task The_four_endpoints_share_one_limit_per_client()
    {
        await using var app = StartApp(smallLimit: true);
        var paths = new[] { "/api/localities", "/api/clubs", "/api/clubs/um-clube", "/api/clubs/um-clube/pitches/0199f7a0-0000-7000-8000-0000000000d1/slots" };
        foreach (var path in paths)
        {
            using var response = await GetAsync(app, path, "203.0.113.12");
            response.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }

        using var limited = await GetAsync(app, "/api/clubs", "203.0.113.12");

        await ShouldBeTooManyRequestsAsync(limited);
    }

    [Fact]
    public async Task Other_endpoints_are_not_counted_in_the_public_limit()
    {
        await using var app = StartApp(smallLimit: true);
        await ExhaustAsync(app, "/api/localities", "203.0.113.13");

        using var health = await GetAsync(app, "/health", "203.0.113.13");
        using var login = await GetAsync(app, "/api/me/bookings?scope=upcoming", "203.0.113.13");

        health.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        login.StatusCode.ShouldBe(HttpStatusCode.Unauthorized); // not limited by the public policy either
    }

    [Fact]
    public async Task Spoofed_forwarded_for_from_an_untrusted_connection_does_not_change_the_partition()
    {
        await using var app = StartApp(smallLimit: true);
        await ExhaustAsync(app, "/api/localities", "203.0.113.20", request => $"198.51.100.{request + 1}");

        using var limited = await GetAsync(app, "/api/localities", "203.0.113.20", "198.51.100.99");

        await ShouldBeTooManyRequestsAsync(limited);
    }

    [Fact]
    public async Task Spoofed_forwarded_for_cannot_use_up_another_clients_limit()
    {
        await using var app = StartApp(smallLimit: true);
        await ExhaustAsync(app, "/api/localities", "203.0.113.21", _ => "203.0.113.22");

        using var victim = await GetAsync(app, "/api/localities", "203.0.113.22");

        victim.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_trusted_proxy_forwards_the_client_address_and_each_client_has_its_own_limit()
    {
        await using var app = StartApp(smallLimit: true);
        await ExhaustAsync(app, "/api/localities", TrustedProxy, _ => "198.51.100.10");

        using var limited = await GetAsync(app, "/api/localities", TrustedProxy, "198.51.100.10");
        using var otherClient = await GetAsync(app, "/api/localities", TrustedProxy, "198.51.100.11");

        await ShouldBeTooManyRequestsAsync(limited);
        otherClient.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Ipv6_clients_share_a_limit_per_64_bit_network()
    {
        await using var app = StartApp(smallLimit: true);
        await ExhaustAsync(app, "/api/localities", "2001:db8:1:1::1");

        using var sameNetwork = await GetAsync(app, "/api/localities", "2001:db8:1:1::abcd");
        using var otherNetwork = await GetAsync(app, "/api/localities", "2001:db8:1:2::1");

        await ShouldBeTooManyRequestsAsync(sameNetwork);
        otherNetwork.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_nonsense_setting_stops_the_api_from_starting()
    {
        var app = factory.CreateIsolatedApp($"t06_rl_{Guid.NewGuid():N}", new Dictionary<string, string?> { ["RateLimiting:Public:ReadPermitLimit"] = "0" });

        var failure = Should.Throw<Exception>(() => _ = app.Server);

        failure.ToString().ShouldContain("RateLimiting:Public:ReadPermitLimit");
        await app.DisposeAsync();
    }
}
