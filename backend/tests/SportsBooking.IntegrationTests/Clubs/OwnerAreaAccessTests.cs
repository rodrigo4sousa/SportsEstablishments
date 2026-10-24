using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Clubs;

/// <summary>
/// T02 for every owner-area endpoint, found from the API's own routes, so an endpoint added later is covered without
/// touching this file: only owners get in, and a club-scoped route (<c>{clubId}</c>) never reaches another owner's
/// club. A new endpoint that fails here is missing the Owner policy or is not going through <c>OwnedClubs</c>.
/// </summary>
public sealed class OwnerAreaAccessTests(ApiFactory factory)
{
    private const string OwnerAreaPrefix = "api/manage";
    private const string ClubParameter = "clubId";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A valid club body, so that a request reaching validation would succeed rather than fail with 400.</summary>
    private static readonly object ValidBody = new
    {
        name = "Clube de Teste",
        address = "Rua 1",
        locality = "Lisboa",
        phone = "912345678",
        contactEmail = "geral@clube.pt",
        cancellationWindowHours = 24,
    };

    private sealed record OwnerEndpoint(string Method, string Template)
    {
        public bool IsClubScoped => Template.Contains($"{{{ClubParameter}", StringComparison.Ordinal);

        public string Path(Guid clubId) =>
            "/" + System.Text.RegularExpressions.Regex.Replace(Template, @"\{(\w+)(:[^}]*)?\}", match =>
                match.Groups[1].Value == ClubParameter ? clubId.ToString() : Guid.CreateVersion7().ToString());

        public override string ToString() => $"{Method} /{Template}";
    }

    private List<OwnerEndpoint> OwnerAreaEndpoints() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/').StartsWith(OwnerAreaPrefix, StringComparison.Ordinal) == true)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => new OwnerEndpoint(method, endpoint.RoutePattern.RawText!.TrimStart('/'))))
            .ToList();

    private async Task<HttpResponseMessage> SendAsync(OwnerEndpoint endpoint, Guid clubId, string? accessToken)
    {
        using var client = factory.CreateSiteClient();
        using var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), endpoint.Path(clubId));
        if (endpoint.Method is "POST" or "PUT" or "PATCH")
        {
            request.Content = JsonContent.Create(ValidBody);
        }

        if (accessToken is not null)
        {
            request.WithCookies((SessionCookies.AccessToken, accessToken));
        }

        return await client.SendAsync(request, Ct);
    }

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code, OwnerEndpoint endpoint)
    {
        response.StatusCode.ShouldBe(status, endpoint.ToString());
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe(code, endpoint.ToString());
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void The_owner_area_endpoints_are_found()
    {
        var endpoints = OwnerAreaEndpoints().Select(endpoint => endpoint.ToString()).ToList();

        // The discovery itself must work, or the tests below would pass on nothing.
        endpoints.ShouldContain("POST /api/manage/clubs");
        endpoints.ShouldContain("GET /api/manage/clubs");
        endpoints.ShouldContain("GET /api/manage/clubs/{clubId:guid}");
        endpoints.ShouldContain("PUT /api/manage/clubs/{clubId:guid}");
        endpoints.ShouldContain("POST /api/manage/clubs/{clubId:guid}/pitches");
        endpoints.ShouldContain("GET /api/manage/clubs/{clubId:guid}/pitches");
        endpoints.ShouldContain("GET /api/manage/clubs/{clubId:guid}/pitches/{pitchId:guid}");
        endpoints.ShouldContain("PUT /api/manage/clubs/{clubId:guid}/pitches/{pitchId:guid}");
        endpoints.ShouldContain("POST /api/manage/clubs/{clubId:guid}/pitches/{pitchId:guid}/deactivate");
        endpoints.ShouldContain("POST /api/manage/clubs/{clubId:guid}/pitches/{pitchId:guid}/reactivate");
        endpoints.ShouldContain("GET /api/manage/clubs/{clubId:guid}/pitches/{pitchId:guid}/schedule");
        endpoints.ShouldContain("PUT /api/manage/clubs/{clubId:guid}/pitches/{pitchId:guid}/schedule");
        endpoints.ShouldContain("GET /api/manage/clubs/{clubId:guid}/pitches/{pitchId:guid}/slots");
    }

    [Fact]
    public async Task Without_a_session_every_owner_endpoint_is_401()
    {
        foreach (var endpoint in OwnerAreaEndpoints())
        {
            using var response = await SendAsync(endpoint, Guid.CreateVersion7(), accessToken: null);
            await ShouldBeProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized, endpoint);
        }
    }

    [Theory]
    [InlineData(Profiles.Player)]
    [InlineData(Profiles.Admin)]
    public async Task Every_other_profile_gets_403_on_every_owner_endpoint(string profile)
    {
        var (_, tokens) = await factory.CreateSignedInAsync(profile);

        foreach (var endpoint in OwnerAreaEndpoints())
        {
            using var response = await SendAsync(endpoint, Guid.CreateVersion7(), tokens.AccessToken);
            await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, ErrorCodes.Forbidden, endpoint);
        }
    }

    [Fact]
    public async Task No_club_scoped_endpoint_reaches_another_owners_club()
    {
        var (_, ownerA) = await factory.CreateSignedInAsync(Profiles.Owner);
        var (_, ownerB) = await factory.CreateSignedInAsync(Profiles.Owner);
        using var client = factory.CreateSiteClient();
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/manage/clubs") { Content = JsonContent.Create(ValidBody) };
        using var created = await client.SendAsync(create.WithCookies((SessionCookies.AccessToken, ownerB.AccessToken)), Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var clubOfB = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        var clubScoped = OwnerAreaEndpoints().Where(endpoint => endpoint.IsClubScoped).ToList();
        clubScoped.ShouldNotBeEmpty();
        foreach (var endpoint in clubScoped)
        {
            using var response = await SendAsync(endpoint, clubOfB, ownerA.AccessToken);
            await ShouldBeProblemAsync(response, HttpStatusCode.NotFound, ErrorCodes.ClubNotFound, endpoint);
        }
    }

    [Fact]
    public async Task An_owner_does_not_reach_the_admin_area_endpoints()
    {
        var (_, owner) = await factory.CreateSignedInAsync(Profiles.Owner);
        using var client = factory.CreateSiteClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ProfileAreasTestController.Route}/admin")
            .WithCookies((SessionCookies.AccessToken, owner.AccessToken));

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
