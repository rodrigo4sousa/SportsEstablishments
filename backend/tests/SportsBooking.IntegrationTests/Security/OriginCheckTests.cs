using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SportsBooking.Application.Common;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Security;

/// <summary>Mutating requests must come from the site's own origin (CSRF defence for cookie sessions).</summary>
public sealed class OriginCheckTests(ApiFactory factory)
{
    // Any path works: the check runs before authorization, so a request that gets through is then a 401 (no session).
    private const string Path = "/api/origin-check-probe";

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string? origin)
    {
        using var client = factory.CreateClient(new() { BaseAddress = new Uri(ApiFactory.SiteOrigin) });
        using var request = new HttpRequestMessage(method, Path) { Content = JsonContent.Create(new { }) };
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static TheoryData<string> MutatingMethods => ["POST", "PUT", "PATCH", "DELETE"];

    [Theory]
    [MemberData(nameof(MutatingMethods))]
    public async Task Mutating_requests_from_another_site_are_rejected_with_problem_details(string method)
    {
        using var response = await SendAsync(new HttpMethod(method), "https://evil.example");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("code").GetString().ShouldBe(ErrorCodes.OriginNotAllowed);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("https://localhost.evil.example")]
    [InlineData("http://localhost")]
    [InlineData("https://localhost:8443")]
    public async Task Post_without_the_exact_site_origin_is_rejected(string? origin)
    {
        using var response = await SendAsync(HttpMethod.Post, origin);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(ApiFactory.SiteOrigin)]
    [InlineData("HTTPS://LOCALHOST")]
    public async Task Post_from_the_site_origin_goes_through(string origin)
    {
        using var response = await SendAsync(HttpMethod.Post, origin);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Safe_methods_do_not_need_an_origin()
    {
        using var response = await SendAsync(HttpMethod.Get, origin: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void Api_refuses_to_start_with_an_allowed_origin_that_is_not_a_bare_origin()
    {
        using var app = factory.CreateIsolatedApp(
            $"origins_{Guid.NewGuid():N}",
            new Dictionary<string, string?> { ["Security:AllowedOrigins:0"] = "https://reservas.pt/path" });

        Should.Throw<OptionsValidationException>(() => _ = app.Server).Message.ShouldContain("Security:AllowedOrigins");
    }
}
