using System.Net;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Security;

/// <summary>T06: every API answer carries <c>X-Content-Type-Options: nosniff</c>, none leaks the server software, and the API's own docs stay out of production.</summary>
public sealed class ResponseHeadersTests(ApiFactory factory)
{
    [Fact]
    public async Task A_successful_answer_has_nosniff_and_no_server_headers()
    {
        using var client = factory.CreateSiteClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.Contains("Server").ShouldBeFalse();
        response.Headers.Contains("X-Powered-By").ShouldBeFalse();
    }

    [Fact]
    public async Task A_missing_session_answer_has_nosniff()
    {
        using var client = factory.CreateSiteClient();

        using var response = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
    }

    [Fact]
    public async Task A_rejection_from_the_origin_check_has_nosniff()
    {
        using var client = factory.CreateClient(new() { BaseAddress = new Uri(ApiFactory.SiteOrigin) });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
    }

    [Theory]
    [InlineData("/swagger/index.html")]
    [InlineData("/openapi/v1.json")]
    public async Task Swagger_and_the_openapi_document_are_not_served_outside_Development(string path)
    {
        using var client = factory.CreateSiteClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldNotBe(HttpStatusCode.OK);
    }
}
