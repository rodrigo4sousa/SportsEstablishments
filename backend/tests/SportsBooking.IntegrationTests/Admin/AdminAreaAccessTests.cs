using System.Net;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SportsBooking.Domain.Accounts;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Admin;

/// <summary>
/// Every endpoint of the admin area, found from the API's own routes so one added later is covered without touching this file:
/// only the Admin profile gets in (a player or an owner gets 403, no session 401), and no response carries anything about a
/// player. Admin-only data is counts; the only personal data on the platform that the admin reads is the owners' name and email.
/// </summary>
public sealed class AdminAreaAccessTests(ApiFactory factory)
{
    private const string Prefix = "api/admin";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private List<(string Method, string Path)> Endpoints() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/').StartsWith(Prefix, StringComparison.Ordinal) == true)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => (method, "/" + System.Text.RegularExpressions.Regex.Replace(
                    endpoint.RoutePattern.RawText!.TrimStart('/'), @"\{(\w+)(:[^}]*)?\}", _ => Guid.CreateVersion7().ToString()))))
            .ToList();

    private async Task<HttpStatusCode> SendAsync(string method, string path, string? accessToken)
    {
        using var client = factory.CreateSiteClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (accessToken is not null)
        {
            request.WithCookies((SessionCookies.AccessToken, accessToken));
        }

        using var response = await client.SendAsync(request, Ct);
        return response.StatusCode;
    }

    [Fact]
    public void The_admin_area_has_the_known_endpoints()
    {
        // The ids in the paths are new Guids: compare the shape.
        var shapes = Endpoints()
            .Select(endpoint => $"{endpoint.Method} {System.Text.RegularExpressions.Regex.Replace(endpoint.Path, "[0-9a-f]{8}-[0-9a-f-]{27}", "{id}")}")
            .Order()
            .ToList();
        shapes.ShouldBe(
        [
            "GET /api/admin/bookings-over-time",
            "GET /api/admin/clubs/top-occupation",
            "GET /api/admin/owners",
            "GET /api/admin/owners/{id}",
            "GET /api/admin/owners/{id}/history",
            "GET /api/admin/registrations",
            "GET /api/admin/stats",
            "POST /api/admin/owners/{id}/deactivate",
            "POST /api/admin/owners/{id}/reactivate",
        ]);
    }

    [Fact]
    public async Task Without_a_session_every_endpoint_is_401()
    {
        foreach (var (method, path) in Endpoints())
        {
            (await SendAsync(method, path, accessToken: null)).ShouldBe(HttpStatusCode.Unauthorized, $"{method} {path}");
        }
    }

    [Theory]
    [InlineData(Profiles.Player)]
    [InlineData(Profiles.Owner)]
    public async Task Another_profile_gets_403_on_every_endpoint(string profile)
    {
        var (_, tokens) = await factory.CreateSignedInAsync(profile);

        foreach (var (method, path) in Endpoints())
        {
            (await SendAsync(method, path, tokens.AccessToken)).ShouldBe(HttpStatusCode.Forbidden, $"{method} {path} as {profile}");
        }
    }

    [Fact]
    public async Task The_admin_gets_in_everywhere()
    {
        var (_, tokens) = await factory.CreateSignedInAsync(Profiles.Admin);

        // An id that is not an owner's is a 404 and a POST without a body a 400: what matters is that the policy lets the admin through.
        foreach (var (method, path) in Endpoints())
        {
            var status = await SendAsync(method, path, tokens.AccessToken);
            status.ShouldNotBe(HttpStatusCode.Unauthorized, $"{method} {path}");
            status.ShouldNotBe(HttpStatusCode.Forbidden, $"{method} {path}");
            if (method == "GET" && !path.Contains("/owners/", StringComparison.Ordinal))
            {
                status.ShouldBe(HttpStatusCode.OK, $"{method} {path}");
            }
        }
    }

    [Fact]
    public async Task No_response_holds_a_players_name_email_or_id()
    {
        await using var admin = await AdminTestApp.CreateAsync(factory);
        var owner = await admin.AddOwnerAsync("Dono Visível");
        var club = await admin.App.CreateClubAsync(owner);
        var pitch = await admin.App.CreatePitchAsync(club.Id);
        var playerEmail = $"jogadora-secreta-{Guid.NewGuid():N}@exemplo.test";
        var playerId = await admin.AddPlayerAsync("Jogadora Secretíssima", playerEmail);
        await admin.AddBookingAsync(pitch.Id, "2026-10-20T10:00:00Z", playerId);
        await admin.AddBookingAsync(pitch.Id, "2026-10-21T10:00:00Z", playerId, "CancelledByPlayer");

        var paths = new[]
        {
            "/api/admin/stats",
            "/api/admin/bookings-over-time?from=2026-10-01&to=2026-10-28&includeCancelled=true",
            "/api/admin/owners",
            "/api/admin/owners?search=Secretíssima",
            "/api/admin/owners?search=jogadora-secreta",
        };
        foreach (var path in paths)
        {
            using var response = await admin.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync(Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
            body.ShouldNotContain("Secretíssima", Case.Insensitive, path);
            body.ShouldNotContain("Secret\\u00EDssima", Case.Insensitive, path);
            body.ShouldNotContain(playerEmail, Case.Insensitive, path);
            body.ShouldNotContain(playerId.ToString(), Case.Insensitive, path);
        }

        // The owner is there, with their name: it is the owner list.
        (await admin.GetJsonAsync("/api/admin/owners")).GetProperty("items")[0].GetProperty("name").GetString().ShouldBe("Dono Visível");
    }
}
