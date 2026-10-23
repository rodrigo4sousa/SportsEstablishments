using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Auth;

/// <summary>
/// US03: opening another profile's area is a 403 from the API. Proved on the test-only area endpoints of
/// <see cref="ProfileAreasTestController"/> until the real areas have endpoints.
/// </summary>
public sealed class ProfileAuthorizationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> EveryProfileOnEveryArea()
    {
        var data = new TheoryData<string, string>();
        foreach (var caller in Profiles.All)
        {
            foreach (var area in Profiles.All)
            {
                data.Add(caller, area);
            }
        }

        return data;
    }

    private async Task<HttpResponseMessage> GetAreaAsync(string area, string? accessToken)
    {
        using var client = factory.CreateSiteClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ProfileAreasTestController.Route}/{area.ToLowerInvariant()}");
        if (accessToken is not null)
        {
            request.WithCookies((SessionCookies.AccessToken, accessToken));
        }

        return await client.SendAsync(request, Ct);
    }

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(EveryProfileOnEveryArea))]
    public async Task Only_the_areas_own_profile_gets_in_and_every_other_profile_gets_403(string caller, string area)
    {
        var (_, tokens) = await factory.CreateSignedInAsync(caller);

        using var response = await GetAreaAsync(area, tokens.AccessToken);

        if (caller == area)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        else
        {
            await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        }
    }

    [Theory]
    [InlineData(Profiles.Player)]
    [InlineData(Profiles.Owner)]
    [InlineData(Profiles.Admin)]
    public async Task Without_a_session_every_area_is_401(string area)
    {
        using var response = await GetAreaAsync(area, accessToken: null);

        await ShouldBeProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
    }
}
