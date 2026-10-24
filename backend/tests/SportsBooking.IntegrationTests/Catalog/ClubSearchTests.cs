using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Clubs;
using SportsBooking.Domain.Pitches;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Catalog;

/// <summary>US16: anyone can search the clubs that can take bookings. Every test uses its own locality, because the assembly shares one database.</summary>
public sealed class ClubSearchTests(ApiFactory factory)
{
    // Friday 1 May 2026, 11:00 in Lisbon. Monday 4 May is open 09:00-13:00 (TestVenues.MondayMorning): slots at 08, 09, 10 and 11 UTC.
    private const string Clock = "2026-05-01T10:00:00Z";
    private const string Monday = "2026-05-04";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> AppAt(string utcNow, bool captureLogs = false) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture))));
            if (captureLogs)
            {
                builder.ConfigureLogging(logging => logging.AddFakeLogging());
            }
        });

    private static string NewLocality(string prefix = "Cidade") => $"{prefix}{Guid.NewGuid():N}";

    private static async Task<JsonElement> GetAsync(WebApplicationFactory<Program> app, string url, HttpStatusCode status = HttpStatusCode.OK)
    {
        using var client = app.CreateClient();
        using var response = await client.GetAsync(url, Ct);
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static Task<JsonElement> SearchAsync(WebApplicationFactory<Program> app, string locality, string? query = null, HttpStatusCode status = HttpStatusCode.OK) =>
        GetAsync(app, $"/api/clubs?locality={Uri.EscapeDataString(locality)}{(query is null ? string.Empty : "&" + query)}", status);

    private static List<JsonElement> Items(JsonElement page) => page.GetProperty("items").EnumerateArray().ToList();

    private static List<string> Slugs(JsonElement page) => Items(page).Select(item => item.GetProperty("slug").GetString()!).ToList();

    private static void ShouldBeFieldError(JsonElement problem, string field)
    {
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("errors").TryGetProperty(field, out var messages).ShouldBeTrue($"errors.{field}");
        messages.GetArrayLength().ShouldBeGreaterThan(0);
    }

    /// <summary>A visible club (one active pitch open on Mondays) in <paramref name="locality"/>.</summary>
    private async Task<(TestClub Club, TestPitch Pitch)> ClubAsync(string locality, string? name = null, Sport sport = Sport.Padel, Guid? ownerId = null)
    {
        var club = await factory.CreateClubAsync(ownerId, name, locality);
        return (club, await factory.CreatePitchAsync(club.Id, sport: sport));
    }

    // ---- Access and shape

    [Fact]
    public async Task Anyone_can_search_without_a_session()
    {
        var locality = NewLocality();
        var (club, _) = await ClubAsync(locality);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/clubs?locality={locality}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Slugs(page).ShouldBe([club.Slug]);
    }

    [Fact]
    public async Task A_result_has_only_public_fields()
    {
        var locality = NewLocality();
        var (club, _) = await ClubAsync(locality, sport: Sport.Futsal);

        var page = await SearchAsync(factory, locality);

        page.EnumerateObject().Select(property => property.Name).ShouldBe(["items", "page", "pageSize", "totalCount"]);
        page.GetProperty("page").GetInt32().ShouldBe(1);
        page.GetProperty("pageSize").GetInt32().ShouldBe(20);
        page.GetProperty("totalCount").GetInt32().ShouldBe(1);
        var item = Items(page).Single();
        item.EnumerateObject().Select(property => property.Name).ShouldBe(["slug", "name", "locality", "sports", "hasFreeSlots"]);
        item.GetProperty("slug").GetString().ShouldBe(club.Slug);
        item.GetProperty("name").GetString().ShouldBe(club.Name);
        item.GetProperty("locality").GetString().ShouldBe(locality);
        item.GetProperty("sports").EnumerateArray().Select(sport => sport.GetString()).ShouldBe(["futsal"]);
    }

    [Fact]
    public async Task A_search_with_no_matches_is_an_empty_page_not_an_error()
    {
        var page = await SearchAsync(factory, NewLocality("Nenhures"));

        Items(page).ShouldBeEmpty();
        page.GetProperty("totalCount").GetInt32().ShouldBe(0);
        page.GetProperty("page").GetInt32().ShouldBe(1);
    }

    // ---- Filters

    [Fact]
    public async Task The_sport_filter_keeps_clubs_with_an_active_pitch_of_that_sport_and_lists_all_their_sports()
    {
        var locality = NewLocality();
        var padelOnly = (await ClubAsync(locality, sport: Sport.Padel)).Club;
        var tennisOnly = (await ClubAsync(locality, sport: Sport.Tennis)).Club;
        var both = await factory.CreateClubAsync(name: null, locality: locality);
        await factory.CreatePitchAsync(both.Id, "Campo A", Sport.Tennis);
        await factory.CreatePitchAsync(both.Id, "Campo B", Sport.Padel);

        var padel = await SearchAsync(factory, locality, "sport=padel");
        var all = await SearchAsync(factory, locality);

        Slugs(padel).Order().ShouldBe(new[] { padelOnly.Slug, both.Slug }.Order());
        Items(padel).Single(item => item.GetProperty("slug").GetString() == both.Slug)
            .GetProperty("sports").EnumerateArray().Select(sport => sport.GetString()).ShouldBe(["padel", "tennis"]);
        Slugs(all).Order().ShouldBe(new[] { padelOnly.Slug, tennisOnly.Slug, both.Slug }.Order());
    }

    [Fact]
    public async Task A_sport_only_an_inactive_pitch_has_is_neither_found_nor_listed()
    {
        var locality = NewLocality();
        var (club, _) = await ClubAsync(locality, sport: Sport.Padel);
        var tennisPitch = await factory.CreatePitchAsync(club.Id, "Campo de ténis", Sport.Tennis);
        await factory.DeactivatePitchAsync(tennisPitch.Id);

        var tennis = await SearchAsync(factory, locality, "sport=tennis");
        var all = await SearchAsync(factory, locality);

        Items(tennis).ShouldBeEmpty();
        Items(all).Single().GetProperty("sports").EnumerateArray().Select(sport => sport.GetString()).ShouldBe(["padel"]);
    }

    [Fact]
    public async Task The_locality_matches_ignoring_case_accents_and_surrounding_spaces_but_must_be_the_whole_locality()
    {
        var token = Guid.NewGuid().ToString("N")[..10];
        var club = (await ClubAsync($"São João {token}")).Club;

        foreach (var typed in new[] { $"São João {token}", $"sao joao {token}", $"SAO JOAO {token.ToUpperInvariant()}", $"  são   joão {token} " })
        {
            Slugs(await SearchAsync(factory, typed)).ShouldBe([club.Slug], typed);
        }

        Items(await SearchAsync(factory, $"sao joao {token[..5]}")).ShouldBeEmpty();
        Items(await SearchAsync(factory, $"joao {token}")).ShouldBeEmpty();
    }

    // ---- Who is visible

    [Fact]
    public async Task Clubs_without_an_active_pitch_are_not_listed()
    {
        var locality = NewLocality();
        var visible = (await ClubAsync(locality)).Club;
        await factory.CreateClubAsync(name: null, locality: locality); // no pitch at all
        var onlyInactive = await factory.CreateClubAsync(name: null, locality: locality);
        await factory.DeactivatePitchAsync((await factory.CreatePitchAsync(onlyInactive.Id)).Id);

        var page = await SearchAsync(factory, locality);

        Slugs(page).ShouldBe([visible.Slug]);
        page.GetProperty("totalCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Clubs_of_a_deactivated_owner_are_not_listed()
    {
        var locality = NewLocality();
        var visible = (await ClubAsync(locality)).Club;
        var gone = (await ClubAsync(locality)).Club;
        await factory.DeactivateAccountAsync(gone.OwnerId);

        var page = await SearchAsync(factory, locality);

        Slugs(page).ShouldBe([visible.Slug]);
        page.GetProperty("totalCount").GetInt32().ShouldBe(1);
    }

    // ---- Pages

    [Fact]
    public async Task Results_are_20_per_page_ordered_by_name_ignoring_case()
    {
        var locality = NewLocality();
        var owner = await factory.CreateOwnerAsync();
        var names = Enumerable.Range(0, 23).Select(index => $"Clube {index:00}").Append("alfa clube").Append("ZETA clube").ToList();
        foreach (var name in names)
        {
            await ClubAsync(locality, name, ownerId: owner);
        }

        var first = await SearchAsync(factory, locality);
        var second = await SearchAsync(factory, locality, "page=2");
        var third = await SearchAsync(factory, locality, "page=3");

        var expected = names.OrderBy(name => name.ToLowerInvariant(), StringComparer.Ordinal).ToList();
        var firstNames = Items(first).Select(item => item.GetProperty("name").GetString()).ToList();
        var secondNames = Items(second).Select(item => item.GetProperty("name").GetString()).ToList();
        firstNames.ShouldBe(expected.Take(20));
        secondNames.ShouldBe(expected.Skip(20));
        firstNames[0].ShouldBe("alfa clube");
        secondNames[^1].ShouldBe("ZETA clube");
        Items(third).ShouldBeEmpty();
        foreach (var (page, number) in new[] { (first, 1), (second, 2), (third, 3) })
        {
            page.GetProperty("totalCount").GetInt32().ShouldBe(25);
            page.GetProperty("page").GetInt32().ShouldBe(number);
            page.GetProperty("pageSize").GetInt32().ShouldBe(20);
        }
    }

    [Fact]
    public async Task The_number_of_database_queries_does_not_depend_on_the_clubs_on_the_page()
    {
        var locality = NewLocality();
        var owner = await factory.CreateOwnerAsync();
        for (var index = 0; index < 22; index++)
        {
            await ClubAsync(locality, $"Clube {index:00}", ownerId: owner);
        }

        // Count, page, pitches, blocks and bookings: five commands for a page of 20 clubs and for a page of 2.
        foreach (var query in new[] { "page=1", "page=2" })
        {
            await using var app = AppAt(Clock, captureLogs: true);
            var logs = app.Services.GetFakeLogCollector(); // starting the host runs its own commands (migrations); count from here
            var before = logs.GetSnapshot().Count(record => record.Id == RelationalEventId.CommandExecuted);

            await SearchAsync(app, locality, $"{query}&date={Monday}");

            var after = logs.GetSnapshot().Count(record => record.Id == RelationalEventId.CommandExecuted);
            (after - before).ShouldBe(5, query);
        }
    }

    // ---- Free slots

    [Fact]
    public async Task A_club_has_free_slots_when_an_active_pitch_has_one_on_the_date()
    {
        await using var app = AppAt(Clock);
        var locality = NewLocality();
        var free = (await ClubAsync(locality)).Club;
        var booked = await ClubAsync(locality);
        var blocked = await ClubAsync(locality);
        var closedMonday = await factory.CreateClubAsync(name: null, locality: locality);
        await factory.CreatePitchAsync(closedMonday.Id, open: (DayOfWeek.Tuesday, "09:00", "13:00"));
        var mixed = await factory.CreateClubAsync(name: null, locality: locality);
        var mixedBooked = await factory.CreatePitchAsync(mixed.Id, "Campo cheio");
        await factory.CreatePitchAsync(mixed.Id, "Campo livre");
        var inactiveFree = await factory.CreateClubAsync(name: null, locality: locality);
        var inactiveBooked = await factory.CreatePitchAsync(inactiveFree.Id, "Campo cheio");
        await factory.DeactivatePitchAsync((await factory.CreatePitchAsync(inactiveFree.Id, "Campo livre mas inativo")).Id);

        foreach (var pitchId in new[] { booked.Pitch.Id, mixedBooked.Id, inactiveBooked.Id })
        {
            foreach (var hour in new[] { 8, 9, 10, 11 })
            {
                await factory.AddBookingAsync(pitchId, TestVenues.Utc($"2026-05-04T{hour:00}:00:00Z"), TestVenues.Utc($"2026-05-04T{hour + 1:00}:00:00Z"));
            }
        }

        await factory.AddBlockAsync(blocked.Pitch.Id, TestVenues.Utc("2026-05-04T07:00:00Z"), TestVenues.Utc("2026-05-04T13:00:00Z"));

        var page = await SearchAsync(app, locality, $"date={Monday}");

        var flags = Items(page).ToDictionary(item => item.GetProperty("slug").GetString()!, item => item.GetProperty("hasFreeSlots").GetBoolean());
        flags.Count.ShouldBe(6);
        flags[free.Slug].ShouldBeTrue("nothing taken");
        flags[booked.Club.Slug].ShouldBeFalse("every slot booked");
        flags[blocked.Club.Slug].ShouldBeFalse("blocked all morning");
        flags[closedMonday.Slug].ShouldBeFalse("closed on Mondays");
        flags[mixed.Slug].ShouldBeTrue("one pitch is full, the other free");
        flags[inactiveFree.Slug].ShouldBeFalse("the only free pitch is inactive");
    }

    [Theory]
    [InlineData("2026-05-04T10:59:59Z", true)] // the 11:00 UTC slot has not started yet
    [InlineData("2026-05-04T11:00:00Z", false)] // starting right now counts as started
    public async Task A_slot_that_has_started_is_not_free(string now, bool expected)
    {
        await using var app = AppAt(now);
        var locality = NewLocality();
        var (_, pitch) = await ClubAsync(locality);
        foreach (var hour in new[] { 8, 9, 10 })
        {
            await factory.AddBookingAsync(pitch.Id, TestVenues.Utc($"2026-05-04T{hour:00}:00:00Z"), TestVenues.Utc($"2026-05-04T{hour + 1:00}:00:00Z"));
        }

        var page = await SearchAsync(app, locality); // no date: today

        Items(page).Single().GetProperty("hasFreeSlots").GetBoolean().ShouldBe(expected);
    }

    [Fact]
    public async Task The_default_date_is_today_in_lisbon_not_in_utc()
    {
        // 23:30 UTC on Sunday 3 May is already 00:30 on Monday in Lisbon, so "today" is Monday, which is open.
        await using var app = AppAt("2026-05-03T23:30:00Z");
        var locality = NewLocality();
        await ClubAsync(locality);

        var byDefault = await SearchAsync(app, locality);
        var tuesday = await SearchAsync(app, locality, "date=2026-05-05");
        var yesterdayInLisbon = await SearchAsync(app, locality, "date=2026-05-03", HttpStatusCode.BadRequest);

        Items(byDefault).Single().GetProperty("hasFreeSlots").GetBoolean().ShouldBeTrue("Monday is open");
        Items(tuesday).Single().GetProperty("hasFreeSlots").GetBoolean().ShouldBeFalse("Tuesday is closed");
        ShouldBeFieldError(yesterdayInLisbon, "date"); // still Sunday in UTC, but already yesterday in Lisbon
    }

    [Fact]
    public async Task Older_bookings_that_overlap_a_new_slot_make_it_not_free_after_a_slot_length_change()
    {
        await using var app = AppAt(Clock);
        var locality = NewLocality();
        var (_, pitch) = await ClubAsync(locality);
        // Booked on the old 60-minute grid (Monday 09:00-10:00 Lisbon), then the pitch moves to 90-minute slots (US08):
        // the new slots are 09:00-10:30 and 10:30-12:00 Lisbon, that is 08:00-09:30Z and 09:30-11:00Z.
        await factory.AddBookingAsync(pitch.Id, TestVenues.Utc("2026-05-04T08:00:00Z"), TestVenues.Utc("2026-05-04T09:00:00Z"));
        await factory.ChangeScheduleAsync(pitch.Id, 90, (DayOfWeek.Monday, "09:00", "12:00"));

        var oneOverlapped = await SearchAsync(app, locality, $"date={Monday}");

        // Only the first new slot overlaps the old booking: the second is free.
        Items(oneOverlapped).Single().GetProperty("hasFreeSlots").GetBoolean().ShouldBeTrue();

        // A second old booking overlaps the second new slot by half an hour: nothing is free any more.
        await factory.AddBookingAsync(pitch.Id, TestVenues.Utc("2026-05-04T10:00:00Z"), TestVenues.Utc("2026-05-04T11:00:00Z"));

        var bothOverlapped = await SearchAsync(app, locality, $"date={Monday}");

        Items(bothOverlapped).Single().GetProperty("hasFreeSlots").GetBoolean().ShouldBeFalse();
    }

    // ---- Bad input

    [Fact]
    public async Task The_date_is_limited_to_today_up_to_30_days_ahead()
    {
        await using var app = AppAt(Clock);
        var locality = NewLocality();
        await ClubAsync(locality);

        await SearchAsync(app, locality, "date=2026-05-01"); // today
        await SearchAsync(app, locality, "date=2026-05-31"); // today + 30
        foreach (var date in new[] { "2026-04-30", "2026-06-01" })
        {
            ShouldBeFieldError(await SearchAsync(app, locality, $"date={date}", HttpStatusCode.BadRequest), "date");
        }
    }

    [Theory]
    [InlineData("date=04-05-2026", "date")]
    [InlineData("date=2026-5-4", "date")]
    [InlineData("date=amanha", "date")]
    [InlineData("sport=golf", "sport")]
    [InlineData("sport=Padel", "sport")]
    [InlineData("page=0", "page")]
    [InlineData("page=-1", "page")]
    [InlineData("page=10001", "page")]
    public async Task Invalid_input_is_a_400_on_the_right_field(string query, string field)
    {
        await using var app = AppAt(Clock);

        var problem = await GetAsync(app, $"/api/clubs?{query}", HttpStatusCode.BadRequest);

        ShouldBeFieldError(problem, field);
    }

    [Fact]
    public async Task A_page_that_is_not_a_number_and_a_very_long_locality_are_400s_never_500s()
    {
        (await GetAsync(factory, "/api/clubs?page=abc", HttpStatusCode.BadRequest)).GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        ShouldBeFieldError(await SearchAsync(factory, new string('x', ClubRules.LocalityMaximumLength + 1), status: HttpStatusCode.BadRequest), "locality");
    }

    [Fact]
    public async Task Blank_filters_mean_no_filter()
    {
        var locality = NewLocality();
        var (club, _) = await ClubAsync(locality);

        var page = await GetAsync(factory, $"/api/clubs?sport=&locality={locality}&date=");

        Slugs(page).ShouldBe([club.Slug]);
    }

    // ---- Localities

    [Fact]
    public async Task The_localities_are_those_of_visible_clubs_one_per_spelling()
    {
        var token = Guid.NewGuid().ToString("N")[..10];
        await ClubAsync($"Évora{token}");
        await ClubAsync($"EVORA{token}"); // same locality, written differently
        await ClubAsync($"Porto{token}");
        await factory.CreateClubAsync(name: null, locality: $"SemCampos{token}");
        var gone = (await ClubAsync($"Fechado{token}")).Club;
        await factory.DeactivateAccountAsync(gone.OwnerId);

        using var client = factory.CreateClient(); // no session
        using var response = await client.GetAsync("/api/localities", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var localities = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().Select(item => item.GetString()!).ToList();
        var ours = localities.Where(locality => locality.EndsWith(token, StringComparison.Ordinal)).ToList();
        ours.Count.ShouldBe(2, string.Join(", ", ours));
        ClubRules.LocalityKey(ours[0]).ShouldBe($"evora{token}");
        ClubRules.LocalityKey(ours[1]).ShouldBe($"porto{token}");
        localities.ShouldNotContain($"SemCampos{token}");
        localities.ShouldNotContain($"Fechado{token}");
        localities.Distinct().Count().ShouldBe(localities.Count);
    }

    // ---- Privacy

    [Fact]
    public async Task Search_and_localities_say_nothing_about_players_owners_or_bookings()
    {
        await using var app = AppAt(Clock);
        var locality = NewLocality();
        var owner = await factory.CreateAccountAsync(Profiles.Owner, password: null, email: $"dono-{Guid.NewGuid():N}@exemplo.test", name: "Dona Privada Silva");
        var (_, pitch) = await ClubAsync(locality, ownerId: owner.Id);
        var player = await factory.CreateAccountAsync(Profiles.Player, password: null, email: $"jogador-{Guid.NewGuid():N}@exemplo.test", name: "Jogador Privado Costa");
        var booking = await factory.AddBookingAsync(pitch.Id, TestVenues.Utc("2026-05-04T08:00:00Z"), TestVenues.Utc("2026-05-04T09:00:00Z"), player);
        await factory.AddBlockAsync(pitch.Id, TestVenues.Utc("2026-05-04T10:00:00Z"), TestVenues.Utc("2026-05-04T11:00:00Z"));

        using var client = app.CreateClient();
        var bodies = new[]
        {
            await client.GetStringAsync($"/api/clubs?locality={locality}&date={Monday}", Ct),
            await client.GetStringAsync("/api/localities", Ct),
        };

        foreach (var body in bodies)
        {
            body.ShouldNotContain(player.Email!);
            body.ShouldNotContain(player.Id.ToString());
            body.ShouldNotContain("Privado");
            body.ShouldNotContain(owner.Email!);
            body.ShouldNotContain(owner.Id.ToString());
            body.ShouldNotContain("Privada");
            body.ShouldNotContain(booking.Id.ToString());
            body.ShouldNotContain("Manutenção"); // the block's reason
        }
    }
}
