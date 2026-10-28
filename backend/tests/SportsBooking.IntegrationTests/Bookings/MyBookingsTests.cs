using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Pitches;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Bookings;

/// <summary>US19: a player reads their own bookings, and only theirs.</summary>
public sealed class MyBookingsTests(ApiFactory factory)
{
    // Monday 4 May 2026, 13:00 in Lisbon (WEST). Monday is open 09:00-13:00 (TestVenues.MondayMorning) but these tests
    // write bookings straight to the database, so any time will do.
    private const string Clock = "2026-05-04T12:00:00Z";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> AppAt(string utcNow) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture)))));

    private static async Task<HttpResponseMessage> GetAsync(WebApplicationFactory<Program> app, string path, string? accessToken)
    {
        using var client = app.CreateSiteClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (accessToken is not null)
        {
            request.WithCookies((SessionCookies.AccessToken, accessToken));
        }

        return await client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(status, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var problem = await JsonAsync(response, status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        return problem;
    }

    private static List<JsonElement> Items(JsonElement page) => page.GetProperty("items").EnumerateArray().ToList();

    private static List<Guid> Ids(JsonElement page) => Items(page).Select(item => item.GetProperty("id").GetGuid()).ToList();

    private async Task<(AppUser User, string Token)> PlayerAsync()
    {
        var player = await factory.CreateSignedInPlayerAsync();
        return (player.User, player.Tokens.AccessToken);
    }

    private static DateTime At(string instant) => TestVenues.Utc(instant);

    // ---- The list

    [Fact]
    public async Task Upcoming_is_confirmed_bookings_that_have_not_ended_oldest_first_and_past_is_the_rest_newest_first()
    {
        await using var app = AppAt(Clock);
        var club = await factory.CreateClubAsync();
        var pitch = await factory.CreatePitchAsync(club.Id);
        var (me, token) = await PlayerAsync();
        var other = await factory.CreateAccountAsync(Profiles.Player, password: null);

        var later = await factory.AddBookingAsync(pitch.Id, At("2026-05-07T10:00:00Z"), At("2026-05-07T11:00:00Z"), me);
        var soon = await factory.AddBookingAsync(pitch.Id, At("2026-05-06T10:00:00Z"), At("2026-05-06T11:00:00Z"), me);
        var underWay = await factory.AddBookingAsync(pitch.Id, At("2026-05-04T11:30:00Z"), At("2026-05-04T12:30:00Z"), me);
        var ended = await factory.AddBookingAsync(pitch.Id, At("2026-05-03T10:00:00Z"), At("2026-05-03T11:00:00Z"), me);
        var noShow = await factory.AddBookingAsync(pitch.Id, At("2026-05-02T10:00:00Z"), At("2026-05-02T11:00:00Z"), me);
        var cancelledByMe = await factory.AddBookingAsync(pitch.Id, At("2026-05-10T10:00:00Z"), At("2026-05-10T11:00:00Z"), me);
        var cancelledByClub = await factory.AddBookingAsync(pitch.Id, At("2026-05-08T10:00:00Z"), At("2026-05-08T11:00:00Z"), me);
        var someoneElses = await factory.AddBookingAsync(pitch.Id, At("2026-05-06T12:00:00Z"), At("2026-05-06T13:00:00Z"), other);
        await factory.SetBookingStatusAsync(noShow.Id, "NoShow");
        await factory.SetBookingStatusAsync(cancelledByMe.Id, "CancelledByPlayer");
        await factory.SetBookingStatusAsync(cancelledByClub.Id, "CancelledByClub");

        var upcoming = await JsonAsync(await GetAsync(app, "/api/me/bookings?scope=upcoming", token));
        var past = await JsonAsync(await GetAsync(app, "/api/me/bookings?scope=past", token));

        // A booking under way has not ended: it is still upcoming, and it is the oldest.
        Ids(upcoming).ShouldBe([underWay.Id, soon.Id, later.Id]);
        Ids(past).ShouldBe([cancelledByMe.Id, cancelledByClub.Id, ended.Id, noShow.Id]);
        Ids(upcoming).Concat(Ids(past)).ShouldNotContain(someoneElses.Id);
        upcoming.GetProperty("totalCount").GetInt32().ShouldBe(3);
        past.GetProperty("totalCount").GetInt32().ShouldBe(4);
        Items(past).Select(item => item.GetProperty("status").GetString()).ShouldBe(["cancelled_by_player", "cancelled_by_club", "confirmed", "no_show"]);
    }

    [Fact]
    public async Task A_booking_shows_club_pitch_times_the_price_paid_status_and_whether_it_can_still_be_cancelled()
    {
        await using var app = AppAt(Clock);
        var club = await factory.CreateClubAsync(name: "Clube da Foz " + Guid.NewGuid().ToString("N")[..6]);
        var pitch = await factory.CreatePitchAsync(club.Id, "Padel 1", Sport.Padel, price: 20m);
        var (me, token) = await PlayerAsync();
        var booking = await factory.AddBookingAsync(pitch.Id, At("2026-05-06T10:00:00Z"), At("2026-05-06T11:00:00Z"), me);
        await factory.ChangePriceAsync(pitch.Id, 35m); // a later price change never alters the booking

        var page = await JsonAsync(await GetAsync(app, "/api/me/bookings?scope=upcoming", token));

        var item = Items(page).ShouldHaveSingleItem();
        item.EnumerateObject().Select(property => property.Name).ShouldBe(
            ["id", "clubName", "clubSlug", "pitchName", "sport", "startsAt", "endsAt", "price", "status", "canCancel", "cancellationDeadline", "series"]);
        item.GetProperty("id").GetGuid().ShouldBe(booking.Id);
        item.GetProperty("clubName").GetString().ShouldBe(club.Name);
        item.GetProperty("clubSlug").GetString().ShouldBe(club.Slug);
        item.GetProperty("pitchName").GetString().ShouldBe("Padel 1");
        item.GetProperty("sport").GetString().ShouldBe("padel");
        item.GetProperty("startsAt").GetDateTime().ToUniversalTime().ShouldBe(At("2026-05-06T10:00:00Z"));
        item.GetProperty("endsAt").GetDateTime().ToUniversalTime().ShouldBe(At("2026-05-06T11:00:00Z"));
        item.GetProperty("price").GetDecimal().ShouldBe(20m);
        item.GetProperty("status").GetString().ShouldBe("confirmed");
        item.GetProperty("canCancel").GetBoolean().ShouldBeTrue();
        // The club's window is 24 hours: the deadline is the start minus 24 hours, as a UTC instant.
        item.GetProperty("cancellationDeadline").GetDateTime().ToUniversalTime().ShouldBe(At("2026-05-05T10:00:00Z"));
        page.GetRawText().ShouldContain("\"cancellationDeadline\":\"2026-05-05T10:00:00Z\"");
    }

    [Fact]
    public async Task Can_cancel_is_false_after_the_deadline_and_for_bookings_that_are_not_confirmed_which_have_no_deadline()
    {
        // 11:00 UTC on 5 May: one hour past the deadline (10:00 UTC) of a booking on 6 May at 10:00 UTC.
        await using var app = AppAt("2026-05-05T11:00:00Z");
        var club = await factory.CreateClubAsync();
        var pitch = await factory.CreatePitchAsync(club.Id);
        var (me, token) = await PlayerAsync();
        var tooLate = await factory.AddBookingAsync(pitch.Id, At("2026-05-06T10:00:00Z"), At("2026-05-06T11:00:00Z"), me);
        var roomLeft = await factory.AddBookingAsync(pitch.Id, At("2026-05-08T10:00:00Z"), At("2026-05-08T11:00:00Z"), me);
        var cancelled = await factory.AddBookingAsync(pitch.Id, At("2026-05-12T10:00:00Z"), At("2026-05-12T11:00:00Z"), me);
        await factory.SetBookingStatusAsync(cancelled.Id, "CancelledByPlayer");

        var upcoming = await JsonAsync(await GetAsync(app, "/api/me/bookings?scope=upcoming", token));
        var past = await JsonAsync(await GetAsync(app, "/api/me/bookings?scope=past", token));

        Items(upcoming).Select(item => (item.GetProperty("id").GetGuid(), item.GetProperty("canCancel").GetBoolean()))
            .ShouldBe([(tooLate.Id, false), (roomLeft.Id, true)]);
        var cancelledItem = Items(past).ShouldHaveSingleItem();
        cancelledItem.GetProperty("canCancel").GetBoolean().ShouldBeFalse();
        cancelledItem.GetProperty("cancellationDeadline").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_clubs_current_cancellation_window_applies_not_the_one_at_the_time_of_booking()
    {
        // 24 hours before the 6 May 10:00 UTC start is 5 May 10:00 UTC; "now" is 5 May 04:00 UTC, 30 hours before the start.
        await using var app = AppAt("2026-05-05T04:00:00Z");
        var club = await factory.CreateClubAsync();
        var pitch = await factory.CreatePitchAsync(club.Id);
        var (me, token) = await PlayerAsync();
        var booking = await factory.AddBookingAsync(pitch.Id, At("2026-05-06T10:00:00Z"), At("2026-05-06T11:00:00Z"), me);

        var withDay = Items(await JsonAsync(await GetAsync(app, "/api/me/bookings?scope=upcoming", token))).Single(item => item.GetProperty("id").GetGuid() == booking.Id);
        await factory.ChangeCancellationWindowAsync(club.Id, 48);
        var withTwoDays = Items(await JsonAsync(await GetAsync(app, "/api/me/bookings?scope=upcoming", token))).Single(item => item.GetProperty("id").GetGuid() == booking.Id);
        await factory.ChangeCancellationWindowAsync(club.Id, 6);
        var withSixHours = Items(await JsonAsync(await GetAsync(app, "/api/me/bookings?scope=upcoming", token))).Single(item => item.GetProperty("id").GetGuid() == booking.Id);

        withDay.GetProperty("canCancel").GetBoolean().ShouldBeTrue();
        withTwoDays.GetProperty("canCancel").GetBoolean().ShouldBeFalse();
        withTwoDays.GetProperty("cancellationDeadline").GetDateTime().ToUniversalTime().ShouldBe(At("2026-05-04T10:00:00Z"));
        withSixHours.GetProperty("canCancel").GetBoolean().ShouldBeTrue();
        withSixHours.GetProperty("cancellationDeadline").GetDateTime().ToUniversalTime().ShouldBe(At("2026-05-06T04:00:00Z"));
    }

    [Fact]
    public async Task Pages_have_20_bookings_in_order_and_the_total_is_given()
    {
        await using var app = AppAt(Clock);
        var club = await factory.CreateClubAsync();
        var pitch = await factory.CreatePitchAsync(club.Id);
        var (me, token) = await PlayerAsync();
        var expected = new List<Guid>();
        for (var day = 0; day < 25; day++)
        {
            var start = At("2026-06-01T10:00:00Z").AddDays(day);
            expected.Add((await factory.AddBookingAsync(pitch.Id, start, start.AddHours(1), me)).Id);
        }

        var first = await JsonAsync(await GetAsync(app, "/api/me/bookings?scope=upcoming", token));
        var second = await JsonAsync(await GetAsync(app, "/api/me/bookings?scope=upcoming&page=2", token));
        var third = await JsonAsync(await GetAsync(app, "/api/me/bookings?scope=upcoming&page=3", token));
        var pastFirst = await JsonAsync(await GetAsync(app, "/api/me/bookings?scope=past", token));

        Ids(first).ShouldBe(expected.Take(20));
        Ids(second).ShouldBe(expected.Skip(20));
        Items(third).ShouldBeEmpty();
        foreach (var (page, number) in new[] { (first, 1), (second, 2), (third, 3) })
        {
            page.GetProperty("totalCount").GetInt32().ShouldBe(25);
            page.GetProperty("pageSize").GetInt32().ShouldBe(20);
            page.GetProperty("page").GetInt32().ShouldBe(number);
        }

        pastFirst.GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task A_player_with_no_bookings_gets_empty_pages()
    {
        await using var app = AppAt(Clock);
        var (_, token) = await PlayerAsync();

        foreach (var scope in new[] { "upcoming", "past" })
        {
            var page = await JsonAsync(await GetAsync(app, $"/api/me/bookings?scope={scope}", token));
            Items(page).ShouldBeEmpty();
            page.GetProperty("totalCount").GetInt32().ShouldBe(0);
        }
    }

    [Theory]
    [InlineData("", "scope")]
    [InlineData("scope=", "scope")]
    [InlineData("scope=all", "scope")]
    [InlineData("scope=Upcoming", "scope")]
    [InlineData("scope=upcoming&page=0", "page")]
    [InlineData("scope=upcoming&page=-1", "page")]
    [InlineData("scope=upcoming&page=abc", "page")]
    [InlineData("scope=past&page=10001", "page")]
    public async Task A_missing_or_invalid_scope_or_page_is_a_400_on_that_field(string query, string field)
    {
        await using var app = AppAt(Clock);
        var (_, token) = await PlayerAsync();

        var problem = await ProblemAsync(await GetAsync(app, $"/api/me/bookings?{query}", token), HttpStatusCode.BadRequest, "validation_failed");

        problem.GetProperty("errors").TryGetProperty(field, out var messages).ShouldBeTrue(field);
        messages[0].GetString().ShouldNotBeNullOrWhiteSpace();
    }

    // ---- One booking, and the Location of a new one

    [Fact]
    public async Task A_player_reads_their_own_booking_by_id()
    {
        await using var app = AppAt(Clock);
        var club = await factory.CreateClubAsync();
        var pitch = await factory.CreatePitchAsync(club.Id);
        var (me, token) = await PlayerAsync();
        var booking = await factory.AddBookingAsync(pitch.Id, At("2026-05-06T10:00:00Z"), At("2026-05-06T11:00:00Z"), me);

        var item = await JsonAsync(await GetAsync(app, $"/api/bookings/{booking.Id}", token));

        item.GetProperty("id").GetGuid().ShouldBe(booking.Id);
        item.GetProperty("clubSlug").GetString().ShouldBe(club.Slug);
        item.GetProperty("canCancel").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Another_players_booking_and_an_unknown_id_are_the_same_404()
    {
        await using var app = AppAt(Clock);
        var club = await factory.CreateClubAsync();
        var pitch = await factory.CreatePitchAsync(club.Id);
        var (_, token) = await PlayerAsync();
        var other = await factory.CreateAccountAsync(Profiles.Player, password: null);
        var theirs = await factory.AddBookingAsync(pitch.Id, At("2026-05-06T10:00:00Z"), At("2026-05-06T11:00:00Z"), other);

        var notMine = await ProblemAsync(await GetAsync(app, $"/api/bookings/{theirs.Id}", token), HttpStatusCode.NotFound, "booking_not_found");
        var unknown = await ProblemAsync(await GetAsync(app, $"/api/bookings/{Guid.CreateVersion7()}", token), HttpStatusCode.NotFound, "booking_not_found");

        notMine.GetProperty("title").GetString().ShouldBe(unknown.GetProperty("title").GetString());
        notMine.GetProperty("detail").GetString().ShouldBe(unknown.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Booking_a_slot_returns_a_location_that_resolves_to_that_booking()
    {
        await using var app = AppAt("2026-05-01T10:00:00Z");
        var club = await factory.CreateClubAsync();
        var pitch = await factory.CreatePitchAsync(club.Id);
        var (_, token) = await PlayerAsync();

        using var client = app.CreateSiteClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/bookings") { Content = JsonContent.Create(new { pitchId = pitch.Id, startsAt = "2026-05-04T08:00:00Z" }) };
        request.WithCookies((SessionCookies.AccessToken, token));
        using var created = await client.SendAsync(request, Ct);

        var body = await JsonAsync(created, HttpStatusCode.Created);
        var id = body.GetProperty("id").GetGuid();
        var location = created.Headers.Location.ShouldNotBeNull();
        location.IsAbsoluteUri.ShouldBeFalse();
        location.OriginalString.ShouldBe($"/api/bookings/{id}");
        var resolved = await JsonAsync(await GetAsync(app, location.OriginalString, token));
        resolved.GetProperty("id").GetGuid().ShouldBe(id);
        resolved.GetProperty("status").GetString().ShouldBe("confirmed");
        resolved.GetProperty("price").GetDecimal().ShouldBe(body.GetProperty("price").GetDecimal());
    }

    // ---- Who may ask

    [Theory]
    [InlineData("/api/me/bookings?scope=upcoming")]
    [InlineData("/api/bookings/0199f7a0-0000-7000-8000-0000000000b1")]
    public async Task Owners_and_admins_get_403_and_visitors_401(string path)
    {
        await using var app = AppAt(Clock);
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var admin = await factory.CreateSignedInAsync(Profiles.Admin);

        await ProblemAsync(await GetAsync(app, path, owner.Tokens.AccessToken), HttpStatusCode.Forbidden, "forbidden");
        await ProblemAsync(await GetAsync(app, path, admin.Tokens.AccessToken), HttpStatusCode.Forbidden, "forbidden");
        await ProblemAsync(await GetAsync(app, path, accessToken: null), HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task The_list_says_nothing_about_other_players_or_the_owner()
    {
        await using var app = AppAt(Clock);
        var owner = await factory.CreateAccountAsync(Profiles.Owner, password: null, email: $"dono-{Guid.NewGuid():N}@exemplo.test", name: "Dona Privada Silva");
        var club = await factory.CreateClubAsync(owner.Id);
        var pitch = await factory.CreatePitchAsync(club.Id);
        var (me, token) = await PlayerAsync();
        var other = await factory.CreateAccountAsync(Profiles.Player, password: null, email: $"outro-{Guid.NewGuid():N}@exemplo.test", name: "Jogador Privado Costa");
        await factory.AddBookingAsync(pitch.Id, At("2026-05-06T10:00:00Z"), At("2026-05-06T11:00:00Z"), me);
        var theirs = await factory.AddBookingAsync(pitch.Id, At("2026-05-06T12:00:00Z"), At("2026-05-06T13:00:00Z"), other);

        using var response = await GetAsync(app, "/api/me/bookings?scope=upcoming", token);
        var text = await response.Content.ReadAsStringAsync(Ct);

        foreach (var secret in new[] { other.Email!, other.Id.ToString(), "Privado", theirs.Id.ToString(), owner.Email!, owner.Id.ToString(), "Privada", club.Id.ToString(), pitch.Id.ToString() })
        {
            text.ShouldNotContain(secret);
        }
    }
}
