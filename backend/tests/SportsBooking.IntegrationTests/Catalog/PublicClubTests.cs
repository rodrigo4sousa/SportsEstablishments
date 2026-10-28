using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Pitches;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Catalog;

/// <summary>US16: the public page of a club, by slug.</summary>
public sealed class PublicClubTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string slug)
    {
        using var client = factory.CreateClient(); // no session
        using var response = await client.GetAsync($"/api/clubs/{slug}", Ct);
        return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>(Ct));
    }

    private static List<string> PitchNames(JsonElement club) =>
        club.GetProperty("pitches").EnumerateArray().Select(pitch => pitch.GetProperty("name").GetString()!).ToList();

    [Fact]
    public async Task Anyone_can_read_a_club_page_with_its_contacts_and_active_pitches()
    {
        var club = await factory.CreateClubAsync(name: $"Clube da Foz {Guid.NewGuid():N}", locality: "Porto");
        var padel = await factory.CreatePitchAsync(club.Id, "Padel 1", Sport.Padel, 24.5m);
        var tennis = await factory.CreatePitchAsync(club.Id, "Ténis 1", Sport.Tennis, 12m);

        var (status, body) = await GetAsync(club.Slug);

        status.ShouldBe(HttpStatusCode.OK);
        body.EnumerateObject().Select(property => property.Name).ShouldBe(["slug", "name", "address", "locality", "phone", "contactEmail", "pitches"]);
        body.GetProperty("slug").GetString().ShouldBe(club.Slug);
        body.GetProperty("name").GetString().ShouldBe(club.Name);
        body.GetProperty("address").GetString().ShouldBe("Rua Principal 1");
        body.GetProperty("locality").GetString().ShouldBe("Porto");
        body.GetProperty("phone").GetString().ShouldBe("912345678");
        body.GetProperty("contactEmail").GetString().ShouldBe("geral@clube.test");

        var pitches = body.GetProperty("pitches").EnumerateArray().ToList();
        pitches.Count.ShouldBe(2);
        pitches[0].EnumerateObject().Select(property => property.Name).ShouldBe(["id", "name", "sport", "pricePerSlot", "hasPriceBands"]);
        pitches[0].GetProperty("id").GetGuid().ShouldBe(padel.Id);
        pitches[0].GetProperty("name").GetString().ShouldBe("Padel 1");
        pitches[0].GetProperty("sport").GetString().ShouldBe("padel");
        pitches[0].GetProperty("pricePerSlot").GetDecimal().ShouldBe(24.5m);
        pitches[0].GetProperty("hasPriceBands").GetBoolean().ShouldBeFalse();
        pitches[1].GetProperty("id").GetGuid().ShouldBe(tennis.Id);
        pitches[1].GetProperty("sport").GetString().ShouldBe("tennis");
    }

    [Fact]
    public async Task Only_active_pitches_are_listed_ordered_by_name_ignoring_case()
    {
        var club = await factory.CreateClubAsync();
        await factory.CreatePitchAsync(club.Id, "zeta");
        await factory.CreatePitchAsync(club.Id, "Alfa");
        await factory.CreatePitchAsync(club.Id, "beta");
        await factory.DeactivatePitchAsync((await factory.CreatePitchAsync(club.Id, "Antigo")).Id);

        var (_, body) = await GetAsync(club.Slug);

        PitchNames(body).ShouldBe(["Alfa", "beta", "zeta"]);
    }

    [Fact]
    public async Task The_price_is_the_current_price_of_the_pitch()
    {
        var club = await factory.CreateClubAsync();
        var pitch = await factory.CreatePitchAsync(club.Id, price: 20m);
        await factory.ChangePriceAsync(pitch.Id, 35m);

        var (_, body) = await GetAsync(club.Slug);

        body.GetProperty("pitches")[0].GetProperty("pricePerSlot").GetDecimal().ShouldBe(35m);
    }

    [Fact]
    public async Task A_club_is_not_found_when_it_is_unknown_has_no_active_pitch_or_its_owner_is_deactivated()
    {
        var noPitches = await factory.CreateClubAsync();
        var onlyInactive = await factory.CreateClubAsync();
        await factory.DeactivatePitchAsync((await factory.CreatePitchAsync(onlyInactive.Id)).Id);
        var ownerGone = await factory.CreateClubAsync();
        await factory.CreatePitchAsync(ownerGone.Id);
        await factory.DeactivateAccountAsync(ownerGone.OwnerId);

        var answers = new List<(string Case, HttpStatusCode Status, JsonElement Body)>();
        foreach (var (name, slug) in new[]
        {
            ("unknown", "clube-que-nao-existe"),
            ("no pitches", noPitches.Slug),
            ("only inactive pitches", onlyInactive.Slug),
            ("owner deactivated", ownerGone.Slug),
        })
        {
            var (status, body) = await GetAsync(slug);
            answers.Add((name, status, body));
        }

        foreach (var (name, status, body) in answers)
        {
            status.ShouldBe(HttpStatusCode.NotFound, name);
            body.GetProperty("code").GetString().ShouldBe("club_not_found", name);
            body.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        }

        // Nothing tells the cases apart: the same title and detail whatever the reason.
        answers.Select(answer => answer.Body.GetProperty("title").GetString()).Distinct().Count().ShouldBe(1);
        answers.Select(answer => answer.Body.GetProperty("detail").GetString()).Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task A_hidden_club_is_back_when_it_can_take_bookings_again()
    {
        var club = await factory.CreateClubAsync();
        var pitch = await factory.CreatePitchAsync(club.Id);
        await factory.DeactivatePitchAsync(pitch.Id);
        (await GetAsync(club.Slug)).Status.ShouldBe(HttpStatusCode.NotFound);

        await factory.CreatePitchAsync(club.Id, "Campo novo");

        (await GetAsync(club.Slug)).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_slug_must_match_exactly()
    {
        var club = await factory.CreateClubAsync();
        await factory.CreatePitchAsync(club.Id);

        (await GetAsync(club.Slug.ToUpperInvariant())).Status.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(club.Slug[..^1])).Status.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(new string('x', 500))).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_page_says_nothing_about_the_owner_or_the_players()
    {
        var owner = await factory.CreateAccountAsync(Profiles.Owner, password: null, email: $"dono-{Guid.NewGuid():N}@exemplo.test", name: "Dona Privada Silva");
        var club = await factory.CreateClubAsync(owner.Id);
        var pitch = await factory.CreatePitchAsync(club.Id);
        var player = await factory.CreateAccountAsync(Profiles.Player, password: null, email: $"jogador-{Guid.NewGuid():N}@exemplo.test", name: "Jogador Privado Costa");
        var booking = await factory.AddBookingAsync(pitch.Id, TestVenues.Utc("2030-05-06T08:00:00Z"), TestVenues.Utc("2030-05-06T09:00:00Z"), player);

        using var client = factory.CreateClient();
        var text = await client.GetStringAsync($"/api/clubs/{club.Slug}", Ct);

        text.ShouldNotContain(owner.Email!);
        text.ShouldNotContain(owner.Id.ToString());
        text.ShouldNotContain("Privada");
        text.ShouldNotContain(club.Id.ToString()); // the club's own id is for the owner area
        text.ShouldNotContain(player.Email!);
        text.ShouldNotContain(player.Id.ToString());
        text.ShouldNotContain("Privado");
        text.ShouldNotContain(booking.Id.ToString());
    }
}
