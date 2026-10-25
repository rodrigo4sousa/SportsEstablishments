using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Bookings;

/// <summary>US13: the owner books a slot of the club for a customer without an account.</summary>
public sealed class CustomerBookingTests(ApiFactory factory)
{
    // Friday 1 May 2026, 11:00 in Lisbon; Monday 4 May is open 09:00-13:00, so its slots start at 08:00Z, 09:00Z, 10:00Z, 11:00Z.
    private const string Now = "2026-05-01T10:00:00Z";
    private const string Start = "2026-05-04T08:00:00Z";
    private const string End = "2026-05-04T09:00:00Z";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> AppAt(string utcNow) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture)))));

    private static string BookingsPath(Guid clubId) => $"/api/manage/clubs/{clubId}/bookings";

    private static async Task<HttpResponseMessage> SendAsync(WebApplicationFactory<Program> app, HttpMethod method, string path, string? accessToken, object? body = null)
    {
        using var client = app.CreateSiteClient();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (accessToken is not null)
        {
            request.WithCookies((SessionCookies.AccessToken, accessToken));
        }

        return await client.SendAsync(request, Ct);
    }

    private static object CustomerBody(Guid pitchId, string startsAt = Start, string name = "Maria Cliente", string phone = "912 345 678") =>
        new { pitchId, startsAt, customerName = name, customerPhone = phone };

    private static Task<HttpResponseMessage> OwnerBooksAsync(WebApplicationFactory<Program> app, Setup setup, object? body = null, string? token = null) =>
        SendAsync(app, HttpMethod.Post, BookingsPath(setup.Club.Id), token ?? setup.OwnerToken, body ?? CustomerBody(setup.Pitch.Id));

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode status)
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

    private sealed record Setup(TestClub Club, TestPitch Pitch, string OwnerToken, Guid OwnerId);

    /// <summary>An owner with a session and a club with one pitch (Monday 09:00-13:00, 60-minute slots, 20 € a slot).</summary>
    private async Task<Setup> SetupAsync(decimal price = 20m)
    {
        var (owner, tokens) = await factory.CreateSignedInAsync(Profiles.Owner, "Dono do Clube");
        var club = await factory.CreateClubAsync(owner.Id);
        var pitch = await factory.CreatePitchAsync(club.Id, price: price);
        return new Setup(club, pitch, tokens.AccessToken, owner.Id);
    }

    private async Task<List<(string Status, Guid? PlayerId, string? Name, string? Phone, decimal Price, DateTime StartsAt, DateTime EndsAt)>> RowsAsync(Guid pitchId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.AsNoTracking()
            .Where(booking => booking.PitchId == pitchId)
            .OrderBy(booking => booking.CreatedAt)
            .ToListAsync(Ct);
        return rows.Select(booking => (booking.Status.ToString(), booking.PlayerId, booking.CustomerName, booking.CustomerPhone, booking.Price, booking.StartsAt, booking.EndsAt)).ToList();
    }

    // ---- Success

    [Fact]
    public async Task The_owner_books_a_slot_for_a_customer_with_201_a_location_and_the_price_of_the_moment()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync(price: 25m);

        using var response = await OwnerBooksAsync(app, setup);
        var text = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, text);
        var booking = JsonDocument.Parse(text).RootElement;
        var id = booking.GetProperty("id").GetGuid();
        response.Headers.Location?.ToString().ShouldBe($"/api/manage/clubs/{setup.Club.Id}/bookings/{id}");
        booking.GetProperty("pitchId").GetGuid().ShouldBe(setup.Pitch.Id);
        booking.GetProperty("pitchName").GetString().ShouldBe("Campo 1");
        booking.GetProperty("startsAt").GetDateTime().ToUniversalTime().ShouldBe(TestVenues.Utc(Start));
        booking.GetProperty("endsAt").GetDateTime().ToUniversalTime().ShouldBe(TestVenues.Utc(End));
        booking.GetProperty("price").GetDecimal().ShouldBe(25m);
        booking.GetProperty("status").GetString().ShouldBe("confirmed");
        booking.GetProperty("bookerName").GetString().ShouldBe("Maria Cliente");
        booking.GetProperty("createdByClub").GetBoolean().ShouldBeTrue();
        booking.GetProperty("customerPhone").GetString().ShouldBe("912345678"); // spaces removed
        booking.GetProperty("canCancel").GetBoolean().ShouldBeTrue();

        var rows = await RowsAsync(setup.Pitch.Id);
        rows.ShouldBe([("Confirmed", null, "Maria Cliente", "912345678", 25m, TestVenues.Utc(Start), TestVenues.Utc(End))]);
    }

    [Fact]
    public async Task The_price_is_kept_when_the_pitchs_price_changes_later()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync(price: 25m);
        var created = await JsonAsync(await OwnerBooksAsync(app, setup), HttpStatusCode.Created);
        await factory.ChangePriceAsync(setup.Pitch.Id, 40m);

        var details = await JsonAsync(
            await SendAsync(app, HttpMethod.Get, BookingsPath(setup.Club.Id) + "/" + created.GetProperty("id").GetGuid(), setup.OwnerToken), HttpStatusCode.OK);

        details.GetProperty("price").GetDecimal().ShouldBe(25m);
        (await RowsAsync(setup.Pitch.Id)).Single().Price.ShouldBe(25m);
    }

    [Fact]
    public async Task The_location_resolves_to_the_owners_details_with_the_customers_name_and_phone_marked_as_created_by_the_club()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        using var created = await OwnerBooksAsync(app, setup, CustomerBody(setup.Pitch.Id, name: "  Maria Cliente  ", phone: "+351 912 345 678"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var details = await JsonAsync(await SendAsync(app, HttpMethod.Get, created.Headers.Location!.ToString(), setup.OwnerToken), HttpStatusCode.OK);

        details.GetProperty("id").GetGuid().ShouldBe((await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid());
        details.GetProperty("bookerName").GetString().ShouldBe("Maria Cliente"); // trimmed
        details.GetProperty("customerPhone").GetString().ShouldBe("+351912345678");
        details.GetProperty("createdByClub").GetBoolean().ShouldBeTrue();
        details.GetProperty("status").GetString().ShouldBe("confirmed");
        details.GetProperty("canCancel").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task The_calendar_shows_it_with_the_customers_name_marked_as_created_by_the_club_next_to_a_players_booking()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        var player = (await factory.CreateSignedInPlayerAsync("Rita Jogadora")).User;
        var playerBooking = await factory.AddBookingAsync(setup.Pitch.Id, TestVenues.Utc("2026-05-04T09:00:00Z"), TestVenues.Utc("2026-05-04T10:00:00Z"), player);
        var customer = await JsonAsync(await OwnerBooksAsync(app, setup), HttpStatusCode.Created);

        using var response = await SendAsync(app, HttpMethod.Get, $"/api/manage/clubs/{setup.Club.Id}/calendar?from=2026-05-04&to=2026-05-04", setup.OwnerToken);
        var text = await response.Content.ReadAsStringAsync(Ct);
        var calendar = JsonDocument.Parse(text).RootElement;

        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        var bookings = calendar.GetProperty("bookings").EnumerateArray().ToList();
        bookings.Select(booking => booking.GetProperty("id").GetGuid()).ShouldBe([customer.GetProperty("id").GetGuid(), playerBooking.Id]);
        bookings[0].GetProperty("bookerName").GetString().ShouldBe("Maria Cliente");
        bookings[0].GetProperty("createdByClub").GetBoolean().ShouldBeTrue();
        bookings[1].GetProperty("bookerName").GetString().ShouldBe("Rita Jogadora");
        bookings[1].GetProperty("createdByClub").GetBoolean().ShouldBeFalse();
        // The calendar has the name; the phone is only in the details.
        text.ShouldNotContain("912345678");
        text.ShouldNotContain(player.Email!);
    }

    // ---- The check constraint

    private async Task InsertRawAsync(Guid pitchId, Guid? playerId, string? name, string? phone, string startsAt = "2026-05-04 12:00:00+00", string endsAt = "2026-05-04 13:00:00+00")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO bookings (id, pitch_id, player_id, customer_name, customer_phone, starts_at, ends_at, price, status, created_at)
             VALUES ({Guid.CreateVersion7()}, {pitchId}, {playerId}, {name}, {phone}, {DateTime.Parse(startsAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal)}, {DateTime.Parse(endsAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal)}, 20, 'Confirmed', now())
             """,
            Ct);
    }

    [Fact]
    public async Task The_database_refuses_a_booking_with_both_a_player_and_a_customer()
    {
        var setup = await SetupAsync();
        var player = await factory.CreateAccountAsync(Profiles.Player, password: null, name: "Jogador");

        var exception = await Should.ThrowAsync<PostgresException>(() => InsertRawAsync(setup.Pitch.Id, player.Id, "Maria Cliente", "912345678"));

        exception.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        exception.ConstraintName.ShouldBe("ck_bookings_player_or_customer");
        (await RowsAsync(setup.Pitch.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_database_refuses_a_booking_with_neither_a_player_nor_a_customer()
    {
        var setup = await SetupAsync();

        var exception = await Should.ThrowAsync<PostgresException>(() => InsertRawAsync(setup.Pitch.Id, null, null, null));

        exception.ConstraintName.ShouldBe("ck_bookings_player_or_customer");
        (await RowsAsync(setup.Pitch.Id)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("name only")]
    [InlineData("phone only")]
    public async Task The_database_refuses_a_customer_without_both_name_and_phone(string which)
    {
        var setup = await SetupAsync();

        var exception = await Should.ThrowAsync<PostgresException>(() => InsertRawAsync(
            setup.Pitch.Id, null, which == "name only" ? "Maria Cliente" : null, which == "phone only" ? "912345678" : null));

        exception.ConstraintName.ShouldBe("ck_bookings_player_or_customer");
    }

    [Fact]
    public async Task The_database_refuses_a_player_booking_that_carries_customer_data()
    {
        var setup = await SetupAsync();
        var player = await factory.CreateAccountAsync(Profiles.Player, password: null, name: "Jogador");

        var exception = await Should.ThrowAsync<PostgresException>(() => InsertRawAsync(setup.Pitch.Id, player.Id, "Maria Cliente", null));

        exception.ConstraintName.ShouldBe("ck_bookings_player_or_customer");
    }

    [Fact]
    public async Task The_database_accepts_exactly_a_player_or_exactly_a_customer()
    {
        var setup = await SetupAsync();
        var player = await factory.CreateAccountAsync(Profiles.Player, password: null, name: "Jogador");

        await InsertRawAsync(setup.Pitch.Id, player.Id, null, null, "2026-05-04 08:00:00+00", "2026-05-04 09:00:00+00");
        await InsertRawAsync(setup.Pitch.Id, null, "Maria Cliente", "912345678", "2026-05-04 09:00:00+00", "2026-05-04 10:00:00+00");

        (await RowsAsync(setup.Pitch.Id)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_overlap_constraint_still_applies_between_a_customer_and_a_player_in_the_database()
    {
        var setup = await SetupAsync();
        var player = await factory.CreateAccountAsync(Profiles.Player, password: null, name: "Jogador");
        await InsertRawAsync(setup.Pitch.Id, player.Id, null, null, "2026-05-04 08:00:00+00", "2026-05-04 09:00:00+00");

        var exception = await Should.ThrowAsync<PostgresException>(() => InsertRawAsync(setup.Pitch.Id, null, "Maria Cliente", "912345678", "2026-05-04 08:30:00+00", "2026-05-04 09:30:00+00"));

        exception.SqlState.ShouldBe(PostgresErrorCodes.ExclusionViolation);
        exception.ConstraintName.ShouldBe("bookings_no_overlap");
    }

    // ---- Every refusal of US18 applies, with the same codes

    [Fact]
    public async Task A_slot_a_player_took_is_409_slot_taken_never_a_500()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        await factory.AddBookingAsync(setup.Pitch.Id, TestVenues.Utc(Start), TestVenues.Utc(End));

        await ProblemAsync(await OwnerBooksAsync(app, setup), HttpStatusCode.Conflict, "slot_taken");

        (await RowsAsync(setup.Pitch.Id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_slot_another_customer_has_is_409_slot_taken_and_a_cancelled_one_frees_it()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        var first = await JsonAsync(await OwnerBooksAsync(app, setup), HttpStatusCode.Created);

        await ProblemAsync(await OwnerBooksAsync(app, setup, CustomerBody(setup.Pitch.Id, name: "Outro Cliente")), HttpStatusCode.Conflict, "slot_taken");
        await JsonAsync(
            await SendAsync(app, HttpMethod.Post, $"{BookingsPath(setup.Club.Id)}/{first.GetProperty("id").GetGuid()}/cancel", setup.OwnerToken, new { reason = "Engano" }),
            HttpStatusCode.OK);

        await JsonAsync(await OwnerBooksAsync(app, setup, CustomerBody(setup.Pitch.Id, name: "Outro Cliente")), HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_slot_that_overlaps_a_booking_made_on_an_older_grid_is_409_slot_taken()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        await factory.AddBookingAsync(setup.Pitch.Id, TestVenues.Utc("2026-05-04T08:30:00Z"), TestVenues.Utc("2026-05-04T09:30:00Z"));

        await ProblemAsync(await OwnerBooksAsync(app, setup), HttpStatusCode.Conflict, "slot_taken");
    }

    [Fact]
    public async Task A_blocked_slot_is_409_slot_blocked()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        await factory.AddBlockAsync(setup.Pitch.Id, TestVenues.Utc("2026-05-04T07:30:00Z"), TestVenues.Utc("2026-05-04T08:30:00Z"));

        await ProblemAsync(await OwnerBooksAsync(app, setup), HttpStatusCode.Conflict, "slot_blocked");

        (await RowsAsync(setup.Pitch.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_inactive_pitch_is_422_pitch_inactive()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        await factory.DeactivatePitchAsync(setup.Pitch.Id);

        await ProblemAsync(await OwnerBooksAsync(app, setup), HttpStatusCode.UnprocessableEntity, "pitch_inactive");
    }

    [Theory]
    [InlineData("2026-05-04T08:30:00Z")] // not on the grid
    [InlineData("2026-05-04T12:00:00Z")] // 13:00 in Lisbon: the pitch has closed
    [InlineData("2026-05-05T08:00:00Z")] // Tuesday: closed
    public async Task A_start_that_is_not_a_slot_is_422_invalid_slot(string startsAt)
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();

        await ProblemAsync(await OwnerBooksAsync(app, setup, CustomerBody(setup.Pitch.Id, startsAt)), HttpStatusCode.UnprocessableEntity, "invalid_slot");
    }

    [Theory]
    [InlineData("2026-05-04T08:00:00Z")] // starting now counts as started
    [InlineData("2026-05-04T08:30:00Z")]
    [InlineData("2026-05-04T09:30:00Z")]
    public async Task A_slot_that_has_started_is_422_slot_started(string now)
    {
        await using var app = AppAt(now);
        var setup = await SetupAsync();

        await ProblemAsync(await OwnerBooksAsync(app, setup), HttpStatusCode.UnprocessableEntity, "slot_started");

        (await RowsAsync(setup.Pitch.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_date_before_today_is_422_outside_booking_window()
    {
        await using var app = AppAt("2026-05-05T10:00:00Z"); // Tuesday: Monday 4 May is yesterday

        var setup = await SetupAsync();

        await ProblemAsync(await OwnerBooksAsync(app, setup), HttpStatusCode.UnprocessableEntity, "outside_booking_window");
    }

    [Fact]
    public async Task Thirty_days_ahead_is_fine_and_thirty_one_is_422_outside_booking_window_provisional_for_owners()
    {
        // Today is Friday 1 May 2026: +30 is Sunday 31 May, +31 is Monday 1 June. Open both Sunday and Monday, 09:00-13:00.
        await using var app = AppAt(Now);
        var (owner, tokens) = await factory.CreateSignedInAsync(Profiles.Owner);
        var club = await factory.CreateClubAsync(owner.Id);
        var pitch = await factory.CreatePitchAsync(club.Id, open: [(DayOfWeek.Sunday, "09:00", "13:00"), (DayOfWeek.Monday, "09:00", "13:00")]);
        var setup = new Setup(club, pitch, tokens.AccessToken, owner.Id);

        await JsonAsync(await OwnerBooksAsync(app, setup, CustomerBody(pitch.Id, "2026-05-31T08:00:00Z")), HttpStatusCode.Created);
        await ProblemAsync(await OwnerBooksAsync(app, setup, CustomerBody(pitch.Id, "2026-06-01T08:00:00Z")), HttpStatusCode.UnprocessableEntity, "outside_booking_window");
    }

    [Fact]
    public async Task A_pitch_of_another_club_even_the_same_owners_and_an_unknown_pitch_are_404_pitch_not_found()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        var otherClub = await factory.CreateClubAsync(setup.OwnerId);
        var otherPitch = await factory.CreatePitchAsync(otherClub.Id);

        await ProblemAsync(await OwnerBooksAsync(app, setup, CustomerBody(otherPitch.Id)), HttpStatusCode.NotFound, "pitch_not_found");
        await ProblemAsync(await OwnerBooksAsync(app, setup, CustomerBody(Guid.CreateVersion7())), HttpStatusCode.NotFound, "pitch_not_found");

        (await RowsAsync(otherPitch.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_deactivated_owner_whose_session_is_still_valid_gets_422_club_unavailable()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        await factory.DeactivateAccountAsync(setup.OwnerId);

        await ProblemAsync(await OwnerBooksAsync(app, setup), HttpStatusCode.UnprocessableEntity, "club_unavailable");

        (await RowsAsync(setup.Pitch.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_refusals_come_in_the_order_of_a_players_booking_inactive_before_window_before_slot_before_started_before_blocks()
    {
        await using var app = AppAt("2026-05-04T08:30:00Z");
        var setup = await SetupAsync();
        await factory.AddBlockAsync(setup.Pitch.Id, TestVenues.Utc("2026-05-04T07:00:00Z"), TestVenues.Utc("2026-05-04T09:00:00Z"));

        // Started comes before the block.
        await ProblemAsync(await OwnerBooksAsync(app, setup), HttpStatusCode.UnprocessableEntity, "slot_started");

        // Not a slot comes before started.
        await ProblemAsync(await OwnerBooksAsync(app, setup, CustomerBody(setup.Pitch.Id, "2026-05-04T08:10:00Z")), HttpStatusCode.UnprocessableEntity, "invalid_slot");

        await factory.DeactivatePitchAsync(setup.Pitch.Id);
        // An inactive pitch comes before everything else.
        await ProblemAsync(await OwnerBooksAsync(app, setup, CustomerBody(setup.Pitch.Id, "2026-05-04T08:10:00Z")), HttpStatusCode.UnprocessableEntity, "pitch_inactive");
    }

    // ---- Validation

    [Theory]
    [InlineData("", "Indica o nome do cliente.")]
    [InlineData("   ", "Indica o nome do cliente.")]
    public async Task A_missing_name_is_400_on_customerName(string name, string message)
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();

        var problem = await ProblemAsync(await OwnerBooksAsync(app, setup, CustomerBody(setup.Pitch.Id, name: name)), HttpStatusCode.BadRequest, "validation_failed");

        problem.GetProperty("errors").GetProperty("customerName").EnumerateArray().Single().GetString().ShouldBe(message);
        (await RowsAsync(setup.Pitch.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_name_of_100_characters_is_fine_and_101_is_400()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();

        var problem = await ProblemAsync(
            await OwnerBooksAsync(app, setup, CustomerBody(setup.Pitch.Id, name: new string('a', 101))), HttpStatusCode.BadRequest, "validation_failed");
        var created = await JsonAsync(await OwnerBooksAsync(app, setup, CustomerBody(setup.Pitch.Id, name: new string('b', 100))), HttpStatusCode.Created);

        problem.GetProperty("errors").GetProperty("customerName").EnumerateArray().Single().GetString().ShouldBe("O nome pode ter no máximo 100 caracteres.");
        created.GetProperty("bookerName").GetString()!.Length.ShouldBe(100);
    }

    [Theory]
    [InlineData("", "Indica o telemóvel do cliente.")]
    [InlineData("   ", "Indica o telemóvel do cliente.")]
    [InlineData("abc", "Indica um número de telemóvel válido, com 9 a 15 algarismos.")]
    [InlineData("12345678", "Indica um número de telemóvel válido, com 9 a 15 algarismos.")] // 8 digits
    [InlineData("1234567890123456", "Indica um número de telemóvel válido, com 9 a 15 algarismos.")] // 16 digits
    [InlineData("912-345-678", "Indica um número de telemóvel válido, com 9 a 15 algarismos.")]
    [InlineData("++351912345678", "Indica um número de telemóvel válido, com 9 a 15 algarismos.")]
    public async Task A_missing_or_wrong_phone_is_400_on_customerPhone(string phone, string message)
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();

        var problem = await ProblemAsync(await OwnerBooksAsync(app, setup, CustomerBody(setup.Pitch.Id, phone: phone)), HttpStatusCode.BadRequest, "validation_failed");

        problem.GetProperty("errors").GetProperty("customerPhone").EnumerateArray().Single().GetString().ShouldBe(message);
        (await RowsAsync(setup.Pitch.Id)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("912345678")]
    [InlineData("912 345 678")]
    [InlineData("+351 912 345 678")]
    [InlineData("123456789012345")] // 15 digits
    public async Task The_phones_of_the_player_sign_up_are_accepted_here_too(string phone)
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();

        using var response = await OwnerBooksAsync(app, setup, CustomerBody(setup.Pitch.Id, phone: phone));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task An_empty_body_is_400_with_a_message_on_each_field_in_portuguese()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();

        var problem = await ProblemAsync(await OwnerBooksAsync(app, setup, new { }), HttpStatusCode.BadRequest, "validation_failed");

        var errors = problem.GetProperty("errors");
        errors.GetProperty("pitchId").EnumerateArray().Single().GetString().ShouldBe("Indica o campo a reservar.");
        errors.GetProperty("startsAt").EnumerateArray().Single().GetString().ShouldBe("Indica o início do slot.");
        errors.GetProperty("customerName").EnumerateArray().Single().GetString().ShouldBe("Indica o nome do cliente.");
        errors.GetProperty("customerPhone").EnumerateArray().Single().GetString().ShouldBe("Indica o telemóvel do cliente.");
    }

    [Theory]
    [InlineData("2026-05-04T09:00:00")] // no zone
    [InlineData("amanhã")]
    public async Task A_start_without_a_zone_or_that_is_not_a_date_is_400_on_startsAt(string startsAt)
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();

        var problem = await ProblemAsync(await OwnerBooksAsync(app, setup, CustomerBody(setup.Pitch.Id, startsAt)), HttpStatusCode.BadRequest, "validation_failed");

        problem.GetProperty("errors").GetProperty("startsAt").EnumerateArray().Single().GetString()
            .ShouldBe("Indica o início do slot como instante com fuso, por exemplo 2026-10-25T09:00:00Z.");
    }

    [Fact]
    public async Task The_price_and_the_end_cannot_be_sent_the_pitch_decides()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync(price: 25m);

        var created = await JsonAsync(
            await SendAsync(app, HttpMethod.Post, BookingsPath(setup.Club.Id), setup.OwnerToken,
                new { pitchId = setup.Pitch.Id, startsAt = Start, customerName = "Maria Cliente", customerPhone = "912345678", price = 1m, endsAt = "2026-05-04T20:00:00Z", status = "NoShow", playerId = Guid.NewGuid() }),
            HttpStatusCode.Created);

        created.GetProperty("price").GetDecimal().ShouldBe(25m);
        created.GetProperty("endsAt").GetDateTime().ToUniversalTime().ShouldBe(TestVenues.Utc(End));
        created.GetProperty("status").GetString().ShouldBe("confirmed");
        (await RowsAsync(setup.Pitch.Id)).Single().PlayerId.ShouldBeNull();
    }

    // ---- T02 and who may ask

    [Fact]
    public async Task Another_owners_club_is_404_club_not_found_whatever_the_body_says_and_nothing_is_created()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        var (_, intruder) = await factory.CreateSignedInAsync(Profiles.Owner);

        await ProblemAsync(await OwnerBooksAsync(app, setup, token: intruder.AccessToken), HttpStatusCode.NotFound, "club_not_found");
        await ProblemAsync(await OwnerBooksAsync(app, setup, new { }, intruder.AccessToken), HttpStatusCode.NotFound, "club_not_found");
        await ProblemAsync(await OwnerBooksAsync(app, setup, CustomerBody(Guid.CreateVersion7()), intruder.AccessToken), HttpStatusCode.NotFound, "club_not_found");

        (await RowsAsync(setup.Pitch.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Players_and_admins_get_403_and_visitors_401_and_nothing_is_created()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        var player = await factory.CreateSignedInPlayerAsync();
        var admin = await factory.CreateSignedInAsync(Profiles.Admin);

        await ProblemAsync(await OwnerBooksAsync(app, setup, token: player.Tokens.AccessToken), HttpStatusCode.Forbidden, "forbidden");
        await ProblemAsync(await OwnerBooksAsync(app, setup, token: admin.Tokens.AccessToken), HttpStatusCode.Forbidden, "forbidden");
        using var anonymous = await SendAsync(app, HttpMethod.Post, BookingsPath(setup.Club.Id), accessToken: null, CustomerBody(setup.Pitch.Id));
        await ProblemAsync(anonymous, HttpStatusCode.Unauthorized, "unauthorized");

        (await RowsAsync(setup.Pitch.Id)).ShouldBeEmpty();
    }

    // ---- Concurrency

    [Fact]
    public async Task A_player_and_the_owner_booking_the_same_slot_at_the_same_time_exactly_one_gets_201_and_none_is_a_500()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        var players = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            players.Add((await factory.CreateSignedInPlayerAsync($"Jogador {i}")).Tokens.AccessToken);
        }

        var tasks = Enumerable.Range(0, 6).Select(_ => Task.Run(() => OwnerBooksAsync(app, setup), Ct))
            .Concat(players.Select(token => Task.Run(() => SendAsync(app, HttpMethod.Post, "/api/bookings", token, new { pitchId = setup.Pitch.Id, startsAt = Start }), Ct)))
            .ToList();
        var responses = await Task.WhenAll(tasks);

        var statuses = responses.Select(response => response.StatusCode).ToList();
        statuses.Count(status => status == HttpStatusCode.Created).ShouldBe(1, string.Join(", ", statuses));
        statuses.Count(status => status == HttpStatusCode.Conflict).ShouldBe(11, string.Join(", ", statuses));
        foreach (var response in responses.Where(response => response.StatusCode == HttpStatusCode.Conflict))
        {
            (await JsonAsync(response, HttpStatusCode.Conflict)).GetProperty("code").GetString().ShouldBe("slot_taken");
        }

        (await RowsAsync(setup.Pitch.Id)).Count(row => row.Status == "Confirmed").ShouldBe(1);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Several_owner_bookings_of_one_slot_at_the_same_time_give_one_201_and_the_rest_409()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(index =>
            Task.Run(() => OwnerBooksAsync(app, setup, CustomerBody(setup.Pitch.Id, name: $"Cliente {index}")), Ct)));

        responses.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Conflict).ShouldBe(9);
        (await RowsAsync(setup.Pitch.Id)).Count.ShouldBe(1);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    // ---- Cancelling, and the slot

    [Fact]
    public async Task The_owner_cancels_a_customer_booking_as_in_us12_and_the_slot_is_free_again()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        var created = await JsonAsync(await OwnerBooksAsync(app, setup), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        var player = await factory.CreateSignedInPlayerAsync();

        var cancelled = await JsonAsync(
            await SendAsync(app, HttpMethod.Post, $"{BookingsPath(setup.Club.Id)}/{id}/cancel", setup.OwnerToken, new { reason = "  O cliente desistiu  " }), HttpStatusCode.OK);

        cancelled.GetProperty("status").GetString().ShouldBe("cancelled_by_club");
        cancelled.GetProperty("cancellationReason").GetString().ShouldBe("O cliente desistiu");
        cancelled.GetProperty("createdByClub").GetBoolean().ShouldBeTrue();
        cancelled.GetProperty("bookerName").GetString().ShouldBe("Maria Cliente");
        cancelled.GetProperty("canCancel").GetBoolean().ShouldBeFalse();
        (await RowsAsync(setup.Pitch.Id)).Single().Status.ShouldBe("CancelledByClub"); // kept, never deleted
        using var rebooked = await SendAsync(app, HttpMethod.Post, "/api/bookings", player.Tokens.AccessToken, new { pitchId = setup.Pitch.Id, startsAt = Start });
        rebooked.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_customer_booking_that_has_started_cannot_be_cancelled_and_one_already_cancelled_is_422_too()
    {
        await using var started = AppAt("2026-05-04T08:30:00Z");
        var setup = await SetupAsync();
        var booking = await factory.AddCustomerBookingAsync(setup.Pitch.Id, TestVenues.Utc(Start), TestVenues.Utc(End));

        await ProblemAsync(
            await SendAsync(started, HttpMethod.Post, $"{BookingsPath(setup.Club.Id)}/{booking.Id}/cancel", setup.OwnerToken, new { reason = "Tarde" }),
            HttpStatusCode.UnprocessableEntity,
            "booking_already_started");

        await using var before = AppAt(Now);
        await JsonAsync(await SendAsync(before, HttpMethod.Post, $"{BookingsPath(setup.Club.Id)}/{booking.Id}/cancel", setup.OwnerToken, new { reason = "Cedo" }), HttpStatusCode.OK);
        await ProblemAsync(
            await SendAsync(before, HttpMethod.Post, $"{BookingsPath(setup.Club.Id)}/{booking.Id}/cancel", setup.OwnerToken, new { reason = "Outra vez" }),
            HttpStatusCode.UnprocessableEntity,
            "booking_not_confirmed");
    }

    [Fact]
    public async Task The_cancelled_customer_booking_is_hidden_in_the_calendar_by_default_and_shown_with_the_flag()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        var created = await JsonAsync(await OwnerBooksAsync(app, setup), HttpStatusCode.Created);
        await JsonAsync(
            await SendAsync(app, HttpMethod.Post, $"{BookingsPath(setup.Club.Id)}/{created.GetProperty("id").GetGuid()}/cancel", setup.OwnerToken, new { reason = "Engano" }), HttpStatusCode.OK);

        var hidden = await JsonAsync(await SendAsync(app, HttpMethod.Get, $"/api/manage/clubs/{setup.Club.Id}/calendar?from=2026-05-04&to=2026-05-04", setup.OwnerToken), HttpStatusCode.OK);
        var shown = await JsonAsync(await SendAsync(app, HttpMethod.Get, $"/api/manage/clubs/{setup.Club.Id}/calendar?from=2026-05-04&to=2026-05-04&includeCancelled=true", setup.OwnerToken), HttpStatusCode.OK);

        hidden.GetProperty("bookings").GetArrayLength().ShouldBe(0);
        var booking = shown.GetProperty("bookings").EnumerateArray().Single();
        booking.GetProperty("status").GetString().ShouldBe("cancelled_by_club");
        booking.GetProperty("createdByClub").GetBoolean().ShouldBeTrue();
        booking.GetProperty("bookerName").GetString().ShouldBe("Maria Cliente");
    }

    // ---- Nothing about them is visible to players or the admin

    [Fact]
    public async Task A_customer_booking_is_in_no_players_list_and_a_player_cannot_read_or_cancel_it()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        var player = await factory.CreateSignedInPlayerAsync("Rita Jogadora");
        var own = await factory.AddBookingAsync(setup.Pitch.Id, TestVenues.Utc("2026-05-04T10:00:00Z"), TestVenues.Utc("2026-05-04T11:00:00Z"), player.User);
        var created = await JsonAsync(await OwnerBooksAsync(app, setup), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();

        var upcoming = await JsonAsync(await SendAsync(app, HttpMethod.Get, "/api/me/bookings?scope=upcoming", player.Tokens.AccessToken), HttpStatusCode.OK);
        var past = await JsonAsync(await SendAsync(app, HttpMethod.Get, "/api/me/bookings?scope=past", player.Tokens.AccessToken), HttpStatusCode.OK);

        upcoming.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ShouldBe([own.Id]);
        upcoming.GetProperty("totalCount").GetInt32().ShouldBe(1);
        past.GetProperty("totalCount").GetInt32().ShouldBe(0);
        await ProblemAsync(await SendAsync(app, HttpMethod.Get, $"/api/bookings/{id}", player.Tokens.AccessToken), HttpStatusCode.NotFound, "booking_not_found");
        await ProblemAsync(await SendAsync(app, HttpMethod.Post, $"/api/bookings/{id}/cancel", player.Tokens.AccessToken), HttpStatusCode.NotFound, "booking_not_found");
        (await RowsAsync(setup.Pitch.Id)).Single(row => row.PlayerId is null).Status.ShouldBe("Confirmed");
    }

    [Fact]
    public async Task A_customer_booking_is_in_no_players_list_even_for_a_new_player_with_nothing()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        await JsonAsync(await OwnerBooksAsync(app, setup), HttpStatusCode.Created);
        var player = await factory.CreateSignedInPlayerAsync();

        var upcoming = await JsonAsync(await SendAsync(app, HttpMethod.Get, "/api/me/bookings?scope=upcoming", player.Tokens.AccessToken), HttpStatusCode.OK);

        upcoming.GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task The_public_pages_show_the_slot_as_taken_and_nothing_about_the_customer()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        var created = await JsonAsync(await OwnerBooksAsync(app, setup, CustomerBody(setup.Pitch.Id, name: "Maria Muito Secreta", phone: "987654321")), HttpStatusCode.Created);
        using var client = app.CreateClient(); // no session

        using var slots = await client.GetAsync($"/api/clubs/{setup.Club.Slug}/pitches/{setup.Pitch.Id}/slots?date=2026-05-04", Ct);
        using var club = await client.GetAsync($"/api/clubs/{setup.Club.Slug}", Ct);
        using var search = await client.GetAsync($"/api/clubs?date=2026-05-04&locality={Uri.EscapeDataString(setup.Club.Locality)}", Ct);

        var slotsText = await slots.Content.ReadAsStringAsync(Ct);
        slots.StatusCode.ShouldBe(HttpStatusCode.OK, slotsText);
        var slotList = JsonDocument.Parse(slotsText).RootElement.GetProperty("slots").EnumerateArray().ToList();
        slotList[0].GetProperty("available").GetBoolean().ShouldBeFalse(); // 09:00 is taken
        slotList[1].GetProperty("available").GetBoolean().ShouldBeTrue();
        foreach (var response in new[] { slots, club, search })
        {
            var text = await response.Content.ReadAsStringAsync(Ct);
            text.ShouldNotContain("Maria Muito Secreta");
            text.ShouldNotContain("987654321");
            text.ShouldNotContain(created.GetProperty("id").GetGuid().ToString());
        }
    }

    [Fact]
    public async Task The_owners_slot_preview_marks_the_customers_slot_as_booked()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        await JsonAsync(await OwnerBooksAsync(app, setup), HttpStatusCode.Created);

        var preview = await JsonAsync(
            await SendAsync(app, HttpMethod.Get, $"/api/manage/clubs/{setup.Club.Id}/pitches/{setup.Pitch.Id}/slots?date=2026-05-04", setup.OwnerToken), HttpStatusCode.OK);

        preview.GetProperty("slots").EnumerateArray().First().GetProperty("booked").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Deactivating_a_pitch_with_a_future_customer_booking_is_refused_like_with_a_players()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        await JsonAsync(await OwnerBooksAsync(app, setup), HttpStatusCode.Created);

        var problem = await ProblemAsync(
            await SendAsync(app, HttpMethod.Post, $"/api/manage/clubs/{setup.Club.Id}/pitches/{setup.Pitch.Id}/deactivate", setup.OwnerToken), HttpStatusCode.UnprocessableEntity, "pitch_has_future_bookings");

        problem.GetProperty("count").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task A_block_over_a_customer_booking_is_refused_like_over_a_players()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        await JsonAsync(await OwnerBooksAsync(app, setup), HttpStatusCode.Created);

        await ProblemAsync(
            await SendAsync(app, HttpMethod.Post, $"/api/manage/clubs/{setup.Club.Id}/pitches/{setup.Pitch.Id}/blocks", setup.OwnerToken, new { start = "2026-05-04T09:30", end = "2026-05-04T10:30", reason = "Obras" }),
            HttpStatusCode.Conflict,
            "block_overlaps_bookings");
    }
}
