using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Bookings;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Bookings;

/// <summary>US18 and T03: a player books a slot; no two confirmed bookings of a pitch ever overlap.</summary>
public sealed class BookingTests(ApiFactory factory)
{
    private const string BookingsPath = "/api/bookings";
    private static readonly string[] Days = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    // Friday 1 May 2026, 11:00 in Lisbon (WEST). Monday 4 May is open 09:00-13:00 in most tests.
    private const string MayClock = "2026-05-01T10:00:00Z";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The API on the same database with its clock at <paramref name="utcNow"/>. Sessions come from the shared app (see PitchScheduleTests).</summary>
    private WebApplicationFactory<Program> AppAt(string utcNow) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture)))));

    private sealed record OwnerPitch(string OwnerToken, Guid OwnerId, Guid ClubId, Guid PitchId)
    {
        public string Base => $"/api/manage/clubs/{ClubId}/pitches/{PitchId}";
    }

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

    private async Task<string> PlayerAsync() => (await factory.CreateSignedInPlayerAsync()).Tokens.AccessToken;

    /// <summary>An owner's club and pitch (20 € per slot) with <paramref name="minutes"/> slots and the given open days.</summary>
    private async Task<OwnerPitch> PitchAsync(int minutes = 60, params (string Day, string Opens, string Closes)[] open)
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var token = owner.Tokens.AccessToken;
        using var club = await SendAsync(factory, HttpMethod.Post, "/api/manage/clubs", token, new
        {
            name = $"Clube {Guid.NewGuid():N}",
            address = "Rua 1",
            locality = "Porto",
            phone = "912345678",
            contactEmail = "geral@clube.pt",
        });
        var clubId = (await club.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        using var pitch = await SendAsync(factory, HttpMethod.Post, $"/api/manage/clubs/{clubId}/pitches", token, new { name = "Campo 1", sport = "padel", pricePerSlot = 20m });
        var result = new OwnerPitch(token, owner.User.Id, clubId, (await pitch.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid());
        await SetWeekAsync(result, minutes, open.Length == 0 ? [("monday", "09:00", "13:00")] : open);
        return result;
    }

    private async Task SetWeekAsync(OwnerPitch pitch, int minutes, params (string Day, string Opens, string Closes)[] open)
    {
        var week = new Dictionary<string, object?> { ["slotDurationMinutes"] = minutes };
        foreach (var day in Days)
        {
            week[day] = new { closed = true };
        }

        foreach (var (day, opens, closes) in open)
        {
            week[day] = new { closed = false, opens, closes };
        }

        using var response = await SendAsync(factory, HttpMethod.Put, $"{pitch.Base}/schedule", pitch.OwnerToken, week);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private static Task<HttpResponseMessage> BookAsync(WebApplicationFactory<Program> app, string player, Guid pitchId, string startsAt) =>
        SendAsync(app, HttpMethod.Post, BookingsPath, player, new { pitchId, startsAt });

    private static async Task<JsonElement> BookedAsync(WebApplicationFactory<Program> app, string player, Guid pitchId, string startsAt)
    {
        using var response = await BookAsync(app, player, pitchId, startsAt);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(Ct));
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        return problem;
    }

    private async Task<Booking> StoredAsync(Guid bookingId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.AsNoTracking().SingleAsync(booking => booking.Id == bookingId, Ct);
    }

    private async Task SqlAsync(FormattableString sql)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlAsync(sql, Ct);
    }

    private static string Utc(JsonElement element, string property) =>
        element.GetProperty(property).GetDateTime().ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    private static async Task<(bool Blocked, bool Booked)[]> PreviewAsync(WebApplicationFactory<Program> app, OwnerPitch pitch, string date)
    {
        using var response = await SendAsync(app, HttpMethod.Get, $"{pitch.Base}/slots?date={date}", pitch.OwnerToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("slots").EnumerateArray()
            .Select(slot => (slot.GetProperty("blocked").GetBoolean(), slot.GetProperty("booked").GetBoolean()))
            .ToArray();
    }

    // ---- The database rule (T03)

    [Fact]
    public async Task The_exclusion_constraint_is_the_one_in_the_stack_doc()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var definition = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .SqlQuery<string>($"SELECT pg_get_constraintdef(oid) AS \"Value\" FROM pg_constraint WHERE conname = 'bookings_no_overlap' AND contype = 'x'")
            .SingleAsync(Ct);

        definition.ShouldBe("EXCLUDE USING gist (pitch_id WITH =, tstzrange(starts_at, ends_at) WITH &&) WHERE (((status)::text = 'Confirmed'::text))");
    }

    [Fact]
    public async Task The_database_refuses_an_overlapping_confirmed_booking_even_without_the_api()
    {
        await using var app = AppAt(MayClock);
        var pitch = await PitchAsync();
        var booked = await BookedAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T08:00:00Z");
        var player = (await StoredAsync(booked.GetProperty("id").GetGuid())).PlayerId!.Value;

        // Overlaps by half an hour: refused with 23P01 (exclusion_violation).
        var exception = await Should.ThrowAsync<Npgsql.PostgresException>(() => SqlAsync($"""
            INSERT INTO bookings (id, pitch_id, player_id, starts_at, ends_at, price, status, created_at)
            VALUES ({Guid.CreateVersion7()}, {pitch.PitchId}, {player}, {new DateTime(2026, 5, 4, 8, 30, 0, DateTimeKind.Utc)}, {new DateTime(2026, 5, 4, 9, 30, 0, DateTimeKind.Utc)}, 20, 'Confirmed', now())
            """));
        exception.SqlState.ShouldBe("23P01");
        exception.ConstraintName.ShouldBe("bookings_no_overlap");

        // Touching is fine ([) ranges), and so is an overlapping booking that is not confirmed.
        await SqlAsync($"""
            INSERT INTO bookings (id, pitch_id, player_id, starts_at, ends_at, price, status, created_at)
            VALUES ({Guid.CreateVersion7()}, {pitch.PitchId}, {player}, {new DateTime(2026, 5, 4, 9, 0, 0, DateTimeKind.Utc)}, {new DateTime(2026, 5, 4, 10, 0, 0, DateTimeKind.Utc)}, 20, 'Confirmed', now()),
                   ({Guid.CreateVersion7()}, {pitch.PitchId}, {player}, {new DateTime(2026, 5, 4, 8, 30, 0, DateTimeKind.Utc)}, {new DateTime(2026, 5, 4, 9, 30, 0, DateTimeKind.Utc)}, 20, 'CancelledByClub', now())
            """);
    }

    // ---- Booking (US18)

    [Fact]
    public async Task A_player_books_a_slot_and_the_booking_keeps_the_price_of_that_moment()
    {
        await using var app = AppAt(MayClock);
        var pitch = await PitchAsync();
        var player = await factory.CreateSignedInPlayerAsync();

        using var response = await BookAsync(app, player.Tokens.AccessToken, pitch.PitchId, "2026-05-04T08:00:00Z");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var booking = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = booking.GetProperty("id").GetGuid();
        id.Version.ShouldBe(7);
        booking.GetProperty("pitchId").GetGuid().ShouldBe(pitch.PitchId);
        booking.GetProperty("clubId").GetGuid().ShouldBe(pitch.ClubId);
        // 09:00-10:00 WEST, the end taken from the 60-minute schedule.
        Utc(booking, "startsAt").ShouldBe("2026-05-04T08:00");
        Utc(booking, "endsAt").ShouldBe("2026-05-04T09:00");
        booking.GetProperty("price").GetDecimal().ShouldBe(20m);
        booking.GetProperty("status").GetString().ShouldBe("confirmed");

        var stored = await StoredAsync(id);
        stored.PlayerId.ShouldBe(player.User.Id);
        stored.Status.ShouldBe(BookingStatus.Confirmed);
        stored.Price.ShouldBe(20m);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
                .SqlQuery<string>($"SELECT status || '|' || price::text AS \"Value\" FROM bookings WHERE id = {id}")
                .SingleAsync(Ct)).ShouldBe("Confirmed|20.00");
        }

        // A later price change is for new bookings only.
        using var price = await SendAsync(factory, HttpMethod.Put, pitch.Base, pitch.OwnerToken, new { name = "Campo 1", sport = "padel", pricePerSlot = 35m });
        price.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StoredAsync(id)).Price.ShouldBe(20m);
        var next = await BookedAsync(app, player.Tokens.AccessToken, pitch.PitchId, "2026-05-04T09:00:00Z");
        next.GetProperty("price").GetDecimal().ShouldBe(35m);
    }

    [Fact]
    public async Task The_player_and_the_end_never_come_from_the_request()
    {
        await using var app = AppAt(MayClock);
        var pitch = await PitchAsync();
        var player = await factory.CreateSignedInPlayerAsync();
        var other = await factory.CreateSignedInPlayerAsync();

        using var response = await SendAsync(app, HttpMethod.Post, BookingsPath, player.Tokens.AccessToken, new
        {
            pitchId = pitch.PitchId,
            startsAt = "2026-05-04T08:00:00Z",
            endsAt = "2026-05-04T12:00:00Z",
            playerId = other.User.Id,
            price = 1m,
            status = "cancelled_by_club",
        });

        var booking = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var stored = await StoredAsync(booking.GetProperty("id").GetGuid());
        stored.PlayerId.ShouldBe(player.User.Id);
        stored.EndsAt.ShouldBe(new DateTime(2026, 5, 4, 9, 0, 0, DateTimeKind.Utc));
        stored.Price.ShouldBe(20m);
        stored.Status.ShouldBe(BookingStatus.Confirmed);
    }

    [Fact]
    public async Task The_start_may_carry_any_offset_and_means_the_same_instant()
    {
        await using var app = AppAt(MayClock);
        var pitch = await PitchAsync();

        var booking = await BookedAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T10:00:00+01:00");

        Utc(booking, "startsAt").ShouldBe("2026-05-04T09:00");
    }

    [Fact]
    public async Task A_slot_already_booked_is_409_slot_taken()
    {
        await using var app = AppAt(MayClock);
        var pitch = await PitchAsync();
        await BookedAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T08:00:00Z");

        using var again = await BookAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T08:00:00Z");

        var problem = await ProblemAsync(again, HttpStatusCode.Conflict, "slot_taken");
        problem.GetProperty("detail").GetString().ShouldBe("Este horário acabou de ser reservado. Escolhe outro.");
    }

    [Fact]
    public async Task An_unknown_pitch_is_404()
    {
        await using var app = AppAt(MayClock);

        using var response = await BookAsync(app, await PlayerAsync(), Guid.CreateVersion7(), "2026-05-04T08:00:00Z");

        await ProblemAsync(response, HttpStatusCode.NotFound, "pitch_not_found");
    }

    [Fact]
    public async Task An_inactive_pitch_is_422_pitch_inactive()
    {
        await using var app = AppAt(MayClock);
        var pitch = await PitchAsync();
        using (var deactivate = await SendAsync(factory, HttpMethod.Post, $"{pitch.Base}/deactivate", pitch.OwnerToken))
        {
            deactivate.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await BookAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T08:00:00Z");

        await ProblemAsync(response, HttpStatusCode.UnprocessableEntity, "pitch_inactive");
    }

    [Fact]
    public async Task A_club_whose_owner_is_deactivated_takes_no_bookings()
    {
        await using var app = AppAt(MayClock);
        var pitch = await PitchAsync();
        await SqlAsync($"UPDATE users SET deactivated_at = now() WHERE id = {pitch.OwnerId}");

        using var response = await BookAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T08:00:00Z");

        await ProblemAsync(response, HttpStatusCode.UnprocessableEntity, "club_unavailable");
    }

    [Theory]
    [InlineData("2026-05-05T08:00:00Z")] // Tuesday: closed
    [InlineData("2026-05-04T08:30:00Z")] // 09:30: off the 60-minute grid
    [InlineData("2026-05-04T07:00:00Z")] // 08:00: before opening
    [InlineData("2026-05-04T12:00:00Z")] // 13:00: closing time, no slot starts there
    [InlineData("2026-05-04T08:00:30Z")] // seconds off
    public async Task A_start_that_is_not_a_generated_slot_is_422_invalid_slot(string startsAt)
    {
        await using var app = AppAt(MayClock);
        var pitch = await PitchAsync();

        using var response = await BookAsync(app, await PlayerAsync(), pitch.PitchId, startsAt);

        await ProblemAsync(response, HttpStatusCode.UnprocessableEntity, "invalid_slot");
    }

    [Theory]
    [InlineData("2026-05-04T08:00:00Z")] // starts right now
    [InlineData("2026-05-04T08:30:00Z")] // half an hour ago
    public async Task A_slot_that_has_started_is_422_slot_started(string now)
    {
        await using var app = AppAt(now);
        var pitch = await PitchAsync();

        using var response = await BookAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T08:00:00Z");

        await ProblemAsync(response, HttpStatusCode.UnprocessableEntity, "slot_started");
    }

    [Theory]
    [InlineData("2026-04-27T08:00:00Z")] // the Monday before
    [InlineData("2026-06-01T08:00:00Z")] // 31 days ahead
    public async Task A_date_outside_today_to_30_days_ahead_is_422_outside_booking_window(string startsAt)
    {
        await using var app = AppAt(MayClock);
        var pitch = await PitchAsync();

        using var response = await BookAsync(app, await PlayerAsync(), pitch.PitchId, startsAt);

        await ProblemAsync(response, HttpStatusCode.UnprocessableEntity, "outside_booking_window");
    }

    [Fact]
    public async Task The_window_uses_the_lisbon_date_and_its_last_day_is_bookable()
    {
        // 23:30 UTC on 30 April is already 1 May in Lisbon: 31 May is today + 30 there, but today + 31 in UTC.
        await using var app = AppAt("2026-04-30T23:30:00Z");
        var pitch = await PitchAsync(60, ("sunday", "09:00", "13:00"));

        await BookedAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-31T08:00:00Z");
    }

    [Fact]
    public async Task The_window_is_judged_on_the_slots_lisbon_date_not_its_utc_date()
    {
        await using var app = AppAt(MayClock);
        var pitch = await PitchAsync(60, ("monday", "00:00", "02:00"));

        // 00:00 WEST on Monday 1 June is 23:00 UTC on 31 May: the UTC date is today + 30, the Lisbon date is today + 31.
        using var response = await BookAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-31T23:00:00Z");

        await ProblemAsync(response, HttpStatusCode.UnprocessableEntity, "outside_booking_window");
    }

    [Fact]
    public async Task A_slot_overlapping_a_block_is_409_slot_blocked_even_partly()
    {
        await using var app = AppAt(MayClock);
        var pitch = await PitchAsync();
        using (var block = await SendAsync(app, HttpMethod.Post, $"{pitch.Base}/blocks", pitch.OwnerToken, new { start = "2026-05-04T09:30", end = "2026-05-04T10:00" }))
        {
            block.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using var response = await BookAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T08:00:00Z");

        await ProblemAsync(response, HttpStatusCode.Conflict, "slot_blocked");
        await BookedAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T09:00:00Z"); // 10:00, touching the block
    }

    [Theory]
    [InlineData("2026-05-04T09:00")]
    [InlineData("04/05/2026 09:00")]
    [InlineData("amanha")]
    public async Task A_start_without_a_zone_or_malformed_is_400_on_starts_at(string startsAt)
    {
        await using var app = AppAt(MayClock);
        var pitch = await PitchAsync();

        using var response = await BookAsync(app, await PlayerAsync(), pitch.PitchId, startsAt);

        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        problem.GetProperty("errors").GetProperty("startsAt").EnumerateArray().Select(message => message.GetString())
            .ShouldBe(["Indica o início do slot como instante com fuso, por exemplo 2026-10-25T09:00:00Z."]);
    }

    [Fact]
    public async Task Missing_fields_are_400_with_a_message_each()
    {
        await using var app = AppAt(MayClock);

        using var response = await SendAsync(app, HttpMethod.Post, BookingsPath, await PlayerAsync(), new { });

        var errors = (await ProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed")).GetProperty("errors");
        errors.GetProperty("pitchId").EnumerateArray().Select(message => message.GetString()).ShouldBe(["Indica o campo a reservar."]);
        errors.GetProperty("startsAt").EnumerateArray().Select(message => message.GetString()).ShouldBe(["Indica o início do slot."]);
    }

    // ---- Concurrency (T03)

    [Fact]
    public async Task Twenty_concurrent_requests_for_the_same_slot_give_exactly_one_201_and_409_for_the_rest()
    {
        await using var app = AppAt(MayClock);
        var pitch = await PitchAsync();
        var players = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PlayerAsync()));

        var statuses = await Task.WhenAll(players.Select(async player =>
        {
            using var response = await BookAsync(app, player, pitch.PitchId, "2026-05-04T08:00:00Z");
            var code = response.StatusCode == HttpStatusCode.Conflict
                ? (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
                : null;
            return (response.StatusCode, code);
        }));

        statuses.Count(result => result.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        statuses.Count(result => result.StatusCode == HttpStatusCode.Conflict && result.code == "slot_taken").ShouldBe(19);
        statuses.ShouldNotContain(result => result.StatusCode == HttpStatusCode.InternalServerError);
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.CountAsync(booking => booking.PitchId == pitch.PitchId, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task A_booking_and_a_block_for_the_same_time_at_the_same_moment_never_both_win()
    {
        await using var app = AppAt(MayClock);
        var pitch = await PitchAsync(60, ("monday", "09:00", "13:00"), ("tuesday", "09:00", "13:00"), ("wednesday", "09:00", "13:00"));
        var rounds = new[] { ("2026-05-04", "2026-05-04T08:00:00Z"), ("2026-05-05", "2026-05-05T09:00:00Z"), ("2026-05-06", "2026-05-06T10:00:00Z") };

        foreach (var (date, startsAt) in rounds)
        {
            var local = DateTime.Parse(startsAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal).AddHours(1);
            var player = await PlayerAsync();
            var booking = BookAsync(app, player, pitch.PitchId, startsAt);
            var block = SendAsync(app, HttpMethod.Post, $"{pitch.Base}/blocks", pitch.OwnerToken, new
            {
                start = $"{date}T{local:HH:mm}",
                end = $"{date}T{local.AddMinutes(30):HH:mm}",
            });
            await Task.WhenAll(booking, block);
            using var bookingResponse = booking.Result;
            using var blockResponse = block.Result;

            var wins = new[] { bookingResponse.StatusCode, blockResponse.StatusCode }.Count(status => status == HttpStatusCode.Created);
            wins.ShouldBe(1, $"{date}: booking {bookingResponse.StatusCode}, block {blockResponse.StatusCode}");
            if (bookingResponse.StatusCode == HttpStatusCode.Created)
            {
                await ProblemAsync(blockResponse, HttpStatusCode.Conflict, "block_overlaps_bookings");
            }
            else
            {
                await ProblemAsync(bookingResponse, HttpStatusCode.Conflict, "slot_blocked");
            }
        }
    }

    // ---- Grid changes and cancellations

    [Fact]
    public async Task A_90_minute_booking_keeps_the_overlapping_60_minute_slots_taken_after_a_duration_change()
    {
        await using var app = AppAt(MayClock);
        var pitch = await PitchAsync(90);
        await BookedAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T08:00:00Z"); // 09:00-10:30

        await SetWeekAsync(pitch, 60, ("monday", "09:00", "13:00"));

        // 10:00-11:00 is only half covered by the old booking, and still taken: the database decides.
        using var partial = await BookAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T09:00:00Z");
        await ProblemAsync(partial, HttpStatusCode.Conflict, "slot_taken");
        using var same = await BookAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T08:00:00Z");
        await ProblemAsync(same, HttpStatusCode.Conflict, "slot_taken");
        await BookedAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T10:00:00Z"); // 11:00

        (await PreviewAsync(app, pitch, "2026-05-04")).Select(slot => slot.Booked).ShouldBe([true, true, true, false]);
    }

    [Fact]
    public async Task A_cancelled_booking_frees_the_slot()
    {
        await using var app = AppAt(MayClock);
        var pitch = await PitchAsync();
        var first = await BookedAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T08:00:00Z");
        (await PreviewAsync(app, pitch, "2026-05-04"))[0].Booked.ShouldBeTrue();

        // US19 and US12 will do this; the test sets the state directly.
        await SqlAsync($"UPDATE bookings SET status = 'CancelledByPlayer' WHERE id = {first.GetProperty("id").GetGuid()}");

        (await PreviewAsync(app, pitch, "2026-05-04"))[0].Booked.ShouldBeFalse();
        await BookedAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T08:00:00Z");
    }

    [Fact]
    public async Task The_owner_preview_marks_booked_and_blocked_slots_separately()
    {
        await using var app = AppAt(MayClock);
        var pitch = await PitchAsync();
        await BookedAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T09:00:00Z"); // 10:00
        using (var block = await SendAsync(app, HttpMethod.Post, $"{pitch.Base}/blocks", pitch.OwnerToken, new { start = "2026-05-04T12:00", end = "2026-05-04T12:30" }))
        {
            block.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        (await PreviewAsync(app, pitch, "2026-05-04")).ShouldBe([(false, false), (false, true), (false, false), (true, false)]);
    }

    // ---- The rules parked for this story (US09, US07)

    [Fact]
    public async Task A_block_over_confirmed_bookings_is_409_and_lists_them()
    {
        await using var app = AppAt(MayClock);
        var pitch = await PitchAsync();
        var first = await BookedAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T08:00:00Z"); // 09:00-10:00
        var second = await BookedAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T10:00:00Z"); // 11:00-12:00

        using var response = await SendAsync(app, HttpMethod.Post, $"{pitch.Base}/blocks", pitch.OwnerToken, new { start = "2026-05-04T09:30", end = "2026-05-04T11:30" });

        var problem = await ProblemAsync(response, HttpStatusCode.Conflict, "block_overlaps_bookings");
        problem.GetProperty("detail").GetString()!.ShouldContain("04/05/2026 09:00–10:00, 04/05/2026 11:00–12:00");
        var listed = problem.GetProperty("bookings").EnumerateArray().ToList();
        listed.Select(booking => booking.GetProperty("id").GetGuid()).ShouldBe([first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid()]);
        Utc(listed[0], "startsAt").ShouldBe("2026-05-04T08:00");
        Utc(listed[0], "endsAt").ShouldBe("2026-05-04T09:00");

        // Touching a booking is fine, and a cancelled booking does not stand in the way.
        using (var touching = await SendAsync(app, HttpMethod.Post, $"{pitch.Base}/blocks", pitch.OwnerToken, new { start = "2026-05-04T10:00", end = "2026-05-04T11:00" }))
        {
            touching.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        await SqlAsync($"UPDATE bookings SET status = 'CancelledByClub' WHERE id = {second.GetProperty("id").GetGuid()}");
        using var afterCancel = await SendAsync(app, HttpMethod.Post, $"{pitch.Base}/blocks", pitch.OwnerToken, new { start = "2026-05-04T11:00", end = "2026-05-04T12:00" });
        afterCancel.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Deactivating_a_pitch_with_future_confirmed_bookings_is_refused_with_the_count()
    {
        await using var app = AppAt(MayClock);
        var pitch = await PitchAsync();
        await BookedAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T08:00:00Z");
        var second = await BookedAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T09:00:00Z");
        var cancelled = await BookedAsync(app, await PlayerAsync(), pitch.PitchId, "2026-05-04T10:00:00Z");
        await SqlAsync($"UPDATE bookings SET status = 'NoShow' WHERE id = {cancelled.GetProperty("id").GetGuid()}");

        using var refused = await SendAsync(app, HttpMethod.Post, $"{pitch.Base}/deactivate", pitch.OwnerToken);

        var problem = await ProblemAsync(refused, HttpStatusCode.UnprocessableEntity, "pitch_has_future_bookings");
        problem.GetProperty("count").GetInt32().ShouldBe(2);
        problem.GetProperty("detail").GetString().ShouldBe("Este campo tem 2 reservas futuras confirmadas. Cancela-as antes de o desativar.");

        await SqlAsync($"UPDATE bookings SET status = 'CancelledByClub' WHERE id = {second.GetProperty("id").GetGuid()}");
        using var one = await SendAsync(app, HttpMethod.Post, $"{pitch.Base}/deactivate", pitch.OwnerToken);
        (await ProblemAsync(one, HttpStatusCode.UnprocessableEntity, "pitch_has_future_bookings")).GetProperty("detail").GetString()
            .ShouldBe("Este campo tem 1 reserva futura confirmada. Cancela-a antes de o desativar.");

        // Once the booking has ended (10:00 WEST is 09:00Z), the pitch can go.
        await using var later = AppAt("2026-05-04T09:00:00Z");
        using var allowed = await SendAsync(later, HttpMethod.Post, $"{pitch.Base}/deactivate", pitch.OwnerToken);
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_ongoing_booking_also_stops_the_deactivation()
    {
        await using var before = AppAt(MayClock);
        var pitch = await PitchAsync();
        await BookedAsync(before, await PlayerAsync(), pitch.PitchId, "2026-05-04T08:00:00Z");

        await using var during = AppAt("2026-05-04T08:30:00Z");
        using var response = await SendAsync(during, HttpMethod.Post, $"{pitch.Base}/deactivate", pitch.OwnerToken);

        (await ProblemAsync(response, HttpStatusCode.UnprocessableEntity, "pitch_has_future_bookings")).GetProperty("count").GetInt32().ShouldBe(1);
    }

    // ---- Daylight saving time (T04)

    [Fact]
    public async Task On_the_october_clock_change_both_01_00_slots_can_be_booked_with_the_right_instants()
    {
        await using var app = AppAt("2026-10-20T12:00:00Z");
        var pitch = await PitchAsync(60, ("sunday", "00:00", "04:00"));
        var player = await PlayerAsync();

        var summer = await BookedAsync(app, player, pitch.PitchId, "2026-10-25T00:00:00Z"); // 01:00 WEST
        var winter = await BookedAsync(app, player, pitch.PitchId, "2026-10-25T01:00:00+00:00"); // 01:00 WET

        Utc(summer, "startsAt").ShouldBe("2026-10-25T00:00");
        Utc(summer, "endsAt").ShouldBe("2026-10-25T01:00");
        Utc(winter, "startsAt").ShouldBe("2026-10-25T01:00");
        Utc(winter, "endsAt").ShouldBe("2026-10-25T02:00");
        (await StoredAsync(summer.GetProperty("id").GetGuid())).EndsAt.ShouldBe(new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc));
        // 01:00 WEST written with its own offset is the first of the two.
        using var again = await BookAsync(app, player, pitch.PitchId, "2026-10-25T01:00:00+01:00");
        await ProblemAsync(again, HttpStatusCode.Conflict, "slot_taken");
    }

    [Fact]
    public async Task A_90_minute_slot_across_the_october_clock_change_stores_90_real_minutes()
    {
        await using var app = AppAt("2026-10-20T12:00:00Z");
        var pitch = await PitchAsync(90, ("sunday", "00:00", "04:00"));

        // The second slot: 01:30 WEST (00:30Z) to 01:00 WET (02:00Z), across the change.
        var booking = await BookedAsync(app, await PlayerAsync(), pitch.PitchId, "2026-10-25T00:30:00Z");

        var stored = await StoredAsync(booking.GetProperty("id").GetGuid());
        stored.StartsAt.ShouldBe(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc));
        stored.EndsAt.ShouldBe(new DateTime(2026, 10, 25, 2, 0, 0, DateTimeKind.Utc));
        (stored.EndsAt - stored.StartsAt).ShouldBe(TimeSpan.FromMinutes(90));
    }

    // ---- Access

    [Theory]
    [InlineData(Profiles.Owner)]
    [InlineData(Profiles.Admin)]
    public async Task Owners_and_admins_get_403(string profile)
    {
        var (_, tokens) = await factory.CreateSignedInAsync(profile);

        using var response = await SendAsync(factory, HttpMethod.Post, BookingsPath, tokens.AccessToken, new { pitchId = Guid.CreateVersion7(), startsAt = "2026-05-04T08:00:00Z" });

        await ProblemAsync(response, HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task A_visitor_gets_401()
    {
        using var response = await SendAsync(factory, HttpMethod.Post, BookingsPath, accessToken: null, new { pitchId = Guid.CreateVersion7(), startsAt = "2026-05-04T08:00:00Z" });

        await ProblemAsync(response, HttpStatusCode.Unauthorized, "unauthorized");
    }
}
