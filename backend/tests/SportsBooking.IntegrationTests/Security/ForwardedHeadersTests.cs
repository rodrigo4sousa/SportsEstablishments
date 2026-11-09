using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Security;

/// <summary>T06: the client address and scheme behind the host's proxies, with an explicit limit on how many forwarded entries are read.</summary>
public sealed class ForwardedHeadersTests(ApiFactory factory)
{
    private const string Connection = "10.9.9.9";
    private const string Claimed = "198.51.100.1";
    private const string LastHop = "203.0.113.50";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> StartApp(params (string Key, string Value)[] settings)
    {
        var app = factory.CreateIsolatedApp($"t06_fwd_{Guid.NewGuid():N}", settings.ToDictionary(setting => setting.Key, setting => (string?)setting.Value));
        _ = app.Server;
        return app;
    }

    private static async Task<ConnectionInfoTestController.ConnectionInfoResponse> ReadAsync(
        WebApplicationFactory<Program> app,
        string forwardedFor,
        string connection = Connection)
    {
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ConnectionInfoTestController.Route).From(connection);
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await client.SendAsync(request, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ConnectionInfoTestController.ConnectionInfoResponse>(Ct)).ShouldNotBeNull();
    }

    [Fact]
    public async Task With_no_proxy_configured_the_address_and_scheme_are_the_connections()
    {
        await using var app = StartApp();

        var seen = await ReadAsync(app, Claimed);

        seen.Address.ShouldBe(Connection);
        seen.Scheme.ShouldBe("http");
    }

    [Fact]
    public async Task Trusting_any_proxy_with_a_limit_of_one_reads_only_the_rightmost_entry_and_the_scheme()
    {
        await using var app = StartApp(("ForwardedHeaders:TrustAnyProxy", "true"), ("ForwardedHeaders:ForwardLimit", "1"));

        var seen = await ReadAsync(app, $"{Claimed}, {LastHop}");

        seen.Address.ShouldBe(LastHop);
        seen.Scheme.ShouldBe("https");
    }

    [Fact]
    public async Task The_limit_is_one_when_it_is_not_set()
    {
        await using var app = StartApp(("ForwardedHeaders:TrustAnyProxy", "true"));

        (await ReadAsync(app, $"{Claimed}, {LastHop}")).Address.ShouldBe(LastHop);
    }

    [Theory]
    [InlineData("2", $"{Claimed}, {LastHop}", Claimed)]
    [InlineData("2", "192.0.2.7, 198.51.100.1, 203.0.113.50", Claimed)]
    [InlineData("2", Claimed, Claimed)]
    public async Task A_larger_limit_reads_further_left_and_no_further(string limit, string forwardedFor, string expected)
    {
        await using var app = StartApp(("ForwardedHeaders:TrustAnyProxy", "true"), ("ForwardedHeaders:ForwardLimit", limit));

        (await ReadAsync(app, forwardedFor)).Address.ShouldBe(expected);
    }

    [Fact]
    public async Task A_known_proxy_is_believed_and_any_other_connection_is_not()
    {
        await using var app = StartApp(("ForwardedHeaders:KnownProxies:0", Connection));

        var fromProxy = await ReadAsync(app, Claimed);
        var fromAnyoneElse = await ReadAsync(app, Claimed, connection: "10.9.9.10");

        fromProxy.Address.ShouldBe(Claimed);
        fromProxy.Scheme.ShouldBe("https");
        fromAnyoneElse.Address.ShouldBe("10.9.9.10");
        fromAnyoneElse.Scheme.ShouldBe("http");
    }

    [Fact]
    public async Task The_rate_limiter_counts_and_logs_the_client_address_and_not_the_proxy()
    {
        await using var app = StartApp(
            ("ForwardedHeaders:TrustAnyProxy", "true"),
            ("RateLimiting:Auth:LoginPermitLimit", "1"));

        async Task<HttpStatusCode> LoginAsync(string client)
        {
            using var browser = app.CreateBrowserClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { email = "a@b.pt", password = "palavra-passe" }) };
            request.From(Connection).Headers.Add("X-Forwarded-For", client);
            using var response = await browser.SendAsync(request, Ct);
            return response.StatusCode;
        }

        (await LoginAsync("198.51.100.20")).ShouldNotBe(HttpStatusCode.TooManyRequests);
        (await LoginAsync("198.51.100.21")).ShouldNotBe(HttpStatusCode.TooManyRequests);
        (await LoginAsync("198.51.100.20")).ShouldBe(HttpStatusCode.TooManyRequests);

        var logged = app.Services.GetFakeLogCollector().GetSnapshot().Single(record => record.Message.StartsWith("Too many requests", StringComparison.Ordinal));
        logged.Message.ShouldContain("198.51.100.20");
        logged.Message.ShouldNotContain(Connection);
    }

    [Theory]
    [InlineData("ForwardedHeaders:ForwardLimit", "0", "ForwardedHeaders:ForwardLimit")]
    [InlineData("ForwardedHeaders:ForwardLimit", "11", "ForwardedHeaders:ForwardLimit")]
    [InlineData("ForwardedHeaders:KnownProxies:0", "10.0.0.2", "ForwardedHeaders:TrustAnyProxy")]
    public void Contradictory_or_out_of_range_settings_stop_the_api(string key, string value, string named)
    {
        using var app = factory.CreateIsolatedApp(
            $"t06_fwd_{Guid.NewGuid():N}",
            new Dictionary<string, string?> { ["ForwardedHeaders:TrustAnyProxy"] = "true", [key] = value });

        Should.Throw<OptionsValidationException>(() => _ = app.Server).Message.ShouldContain(named);
    }
}
