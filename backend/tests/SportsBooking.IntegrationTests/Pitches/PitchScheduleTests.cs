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

namespace SportsBooking.IntegrationTests.Pitches;

/// <summary>US08 and T02: a pitch's slot length and weekly hours, and the slots they generate (T04 on DST days).</summary>
public sealed class PitchScheduleTests(ApiFactory factory)
{
    private static readonly string[] Days = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The API with its clock at <paramref name="utcNow"/>, on the same database. Sessions come from the shared app:
    /// the access token's expiry is checked against the system clock (JwtOptions), so a token issued on the fake
    /// clock would already be expired.
    /// </summary>
    private WebApplicationFactory<Program> AppAt(string utcNow) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse(utcNow, System.Globalization.CultureInfo.InvariantCulture)))));

    /// <summary>A schedule body: every day closed except the ones given.</summary>
    private static Dictionary<string, object?> Schedule(int slotDurationMinutes, params (string Day, string Opens, string Closes)[] open)
    {
        var body = new Dictionary<string, object?> { ["slotDurationMinutes"] = slotDurationMinutes };
        foreach (var day in Days)
        {
            body[day] = new { closed = true };
        }

        foreach (var (day, opens, closes) in open)
        {
            body[day] = new { closed = false, opens, closes };
        }

        return body;
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

    private sealed record OwnerPitch(string AccessToken, Guid ClubId, Guid PitchId)
    {
        public string Base => $"/api/manage/clubs/{ClubId}/pitches/{PitchId}";
    }

    private static async Task<Guid> CreateClubAsync(WebApplicationFactory<Program> app, string accessToken)
    {
        using var response = await SendAsync(app, HttpMethod.Post, "/api/manage/clubs", accessToken, new
        {
            name = $"Clube {Guid.NewGuid():N}",
            address = "Rua 1",
            locality = "Porto",
            phone = "912345678",
            contactEmail = "geral@clube.pt",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreatePitchAsync(WebApplicationFactory<Program> app, string accessToken, Guid clubId, string name = "Campo 1")
    {
        using var response = await SendAsync(app, HttpMethod.Post, $"/api/manage/clubs/{clubId}/pitches", accessToken, new { name, sport = "padel", pricePerSlot = 20m });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private async Task<OwnerPitch> OwnerWithPitchAsync(WebApplicationFactory<Program> app)
    {
        var token = (await factory.CreateSignedInAsync(Profiles.Owner)).Tokens.AccessToken;
        var clubId = await CreateClubAsync(app, token);
        return new OwnerPitch(token, clubId, await CreatePitchAsync(app, token, clubId));
    }

    private static async Task PutScheduleAsync(WebApplicationFactory<Program> app, OwnerPitch pitch, object schedule)
    {
        using var response = await SendAsync(app, HttpMethod.Put, $"{pitch.Base}/schedule", pitch.AccessToken, schedule);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<JsonElement> SlotsAsync(WebApplicationFactory<Program> app, OwnerPitch pitch, string date)
    {
        using var response = await SendAsync(app, HttpMethod.Get, $"{pitch.Base}/slots?date={date}", pitch.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    /// <summary>"start-end" per slot, as the JSON has them (UTC, with Z).</summary>
    private static string[] SlotTimes(JsonElement slots) =>
        slots.GetProperty("slots").EnumerateArray()
            .Select(slot => $"{slot.GetProperty("startsAt").GetString()}/{slot.GetProperty("endsAt").GetString()}")
            .ToArray();

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        return problem;
    }

    private static async Task<string[]> FieldErrorsAsync(HttpResponseMessage response, string field)
    {
        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        return problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(message => message.GetString()!).ToArray();
    }

    [Fact]
    public async Task A_new_pitch_is_closed_every_day_with_60_minute_slots_and_has_no_slots()
    {
        var pitch = await OwnerWithPitchAsync(factory);

        using var response = await SendAsync(factory, HttpMethod.Get, $"{pitch.Base}/schedule", pitch.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var schedule = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        schedule.GetProperty("slotDurationMinutes").GetInt32().ShouldBe(60);
        foreach (var day in Days)
        {
            schedule.GetProperty(day).GetProperty("closed").GetBoolean().ShouldBeTrue(day);
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Domain.Scheduling.ClubTimeZones.Lisbon).DateTime);
        for (var offset = 0; offset < 7; offset++)
        {
            var slots = await SlotsAsync(factory, pitch, today.AddDays(offset).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            slots.GetProperty("slots").GetArrayLength().ShouldBe(0);
        }
    }

    [Fact]
    public async Task An_owner_sets_and_reads_back_the_whole_week()
    {
        var pitch = await OwnerWithPitchAsync(factory);
        var week = Schedule(90, ("monday", "09:00", "22:00"), ("saturday", "08:30", "00:00"), ("sunday", "00:00", "00:00"));

        using var put = await SendAsync(factory, HttpMethod.Put, $"{pitch.Base}/schedule", pitch.AccessToken, week);
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var get = await SendAsync(factory, HttpMethod.Get, $"{pitch.Base}/schedule", pitch.AccessToken);

        foreach (var response in new[] { put, get })
        {
            var schedule = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            schedule.GetProperty("slotDurationMinutes").GetInt32().ShouldBe(90);
            schedule.GetProperty("monday").GetProperty("closed").GetBoolean().ShouldBeFalse();
            schedule.GetProperty("monday").GetProperty("opens").GetString().ShouldBe("09:00");
            schedule.GetProperty("monday").GetProperty("closes").GetString().ShouldBe("22:00");
            schedule.GetProperty("saturday").GetProperty("opens").GetString().ShouldBe("08:30");
            schedule.GetProperty("saturday").GetProperty("closes").GetString().ShouldBe("00:00");
            schedule.GetProperty("sunday").GetProperty("opens").GetString().ShouldBe("00:00");
            foreach (var closed in new[] { "tuesday", "wednesday", "thursday", "friday" })
            {
                schedule.GetProperty(closed).GetProperty("closed").GetBoolean().ShouldBeTrue(closed);
                schedule.GetProperty(closed).GetProperty("opens").ValueKind.ShouldBe(JsonValueKind.Null);
            }
        }
    }

    [Fact]
    public async Task Putting_a_new_week_replaces_the_old_one_day_by_day()
    {
        var pitch = await OwnerWithPitchAsync(factory);
        await PutScheduleAsync(factory, pitch, Schedule(60, ("monday", "09:00", "12:00"), ("tuesday", "09:00", "12:00")));

        await PutScheduleAsync(factory, pitch, Schedule(30, ("monday", "10:00", "11:00"), ("friday", "18:00", "20:00")));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Pitches.AsNoTracking().SingleAsync(candidate => candidate.Id == pitch.PitchId, Ct);
        stored.SlotDurationMinutes.ShouldBe(30);
        stored.OpeningDays.Select(day => $"{day.Day} {day.Opens:HH\\:mm}-{day.Closes:HH\\:mm}").Order()
            .ShouldBe(["Friday 18:00-20:00", "Monday 10:00-11:00"]);
    }

    [Fact]
    public async Task Hours_are_stored_as_local_time_without_a_zone_and_read_back_the_same_from_any_session_time_zone()
    {
        var pitch = await OwnerWithPitchAsync(factory);
        await PutScheduleAsync(factory, pitch, Schedule(60, ("sunday", "01:30", "04:00")));

        // T04, repeated with the real table: local "time" columns, whatever the database session's time zone.
        var connection = new NpgsqlConnectionStringBuilder(factory.ConnectionString) { Timezone = "America/New_York" };
        await using var conn = new NpgsqlConnection(connection.ConnectionString);
        await conn.OpenAsync(Ct);
        await using (var types = new NpgsqlCommand(
            "SELECT column_name, data_type FROM information_schema.columns WHERE table_name = 'pitch_opening_hours' AND column_name IN ('opens', 'closes') ORDER BY column_name", conn))
        await using (var reader = await types.ExecuteReaderAsync(Ct))
        {
            var found = new List<string>();
            while (await reader.ReadAsync(Ct))
            {
                found.Add($"{reader.GetString(0)}={reader.GetString(1)}");
            }

            found.ShouldBe(["closes=time without time zone", "opens=time without time zone"]);
        }

        await using var values = new NpgsqlCommand("SELECT opens::text || '-' || closes::text FROM pitch_opening_hours WHERE pitch_id = @id", conn);
        values.Parameters.AddWithValue("id", pitch.PitchId);
        (await values.ExecuteScalarAsync(Ct)).ShouldBe("01:30:00-04:00:00");
    }

    [Fact]
    public async Task A_closed_day_gives_no_slots_and_a_block_past_closing_is_not_generated()
    {
        await using var app = AppAt("2026-05-01T10:00:00Z");
        var pitch = await OwnerWithPitchAsync(app);
        // 2026-05-04 is a Monday, 2026-05-05 a Tuesday (closed).
        await PutScheduleAsync(app, pitch, Schedule(90, ("monday", "09:00", "11:00")));

        // 09:00-10:30 fits; 10:30-12:00 would pass the 11:00 closing.
        SlotTimes(await SlotsAsync(app, pitch, "2026-05-04")).ShouldBe(["2026-05-04T08:00:00Z/2026-05-04T09:30:00Z"]);
        (await SlotsAsync(app, pitch, "2026-05-05")).GetProperty("slots").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task The_preview_answers_the_date_and_slot_length_it_used()
    {
        await using var app = AppAt("2026-05-01T10:00:00Z");
        var pitch = await OwnerWithPitchAsync(app);
        await PutScheduleAsync(app, pitch, Schedule(30, ("monday", "09:00", "10:00")));

        var slots = await SlotsAsync(app, pitch, "2026-05-04");

        slots.GetProperty("date").GetString().ShouldBe("2026-05-04");
        slots.GetProperty("slotDurationMinutes").GetInt32().ShouldBe(30);
        SlotTimes(slots).ShouldBe(["2026-05-04T08:00:00Z/2026-05-04T08:30:00Z", "2026-05-04T08:30:00Z/2026-05-04T09:00:00Z"]);
    }

    [Theory]
    [InlineData(30, 6)]
    [InlineData(60, 3)]
    [InlineData(90, 2)]
    public async Task On_the_spring_forward_day_00_00_to_04_00_is_three_real_hours(int minutes, int count)
    {
        // Lisbon, Sunday 29 March 2026: 01:00 WET jumps to 02:00 WEST.
        await using var app = AppAt("2026-03-20T12:00:00Z");
        var pitch = await OwnerWithPitchAsync(app);
        await PutScheduleAsync(app, pitch, Schedule(minutes, ("sunday", "00:00", "04:00")));

        var slots = (await SlotsAsync(app, pitch, "2026-03-29")).GetProperty("slots").EnumerateArray().ToList();

        slots.Count.ShouldBe(count);
        // 00:00 WET is 00:00 UTC; slots follow each other and each lasts exactly its length.
        var starts = slots.Select(slot => slot.GetProperty("startsAt").GetDateTime().ToUniversalTime()).ToList();
        var ends = slots.Select(slot => slot.GetProperty("endsAt").GetDateTime().ToUniversalTime()).ToList();
        starts[0].ShouldBe(new DateTime(2026, 3, 29, 0, 0, 0, DateTimeKind.Utc));
        for (var i = 0; i < count; i++)
        {
            (ends[i] - starts[i]).ShouldBe(TimeSpan.FromMinutes(minutes));
            if (i > 0)
            {
                starts[i].ShouldBe(ends[i - 1]);
            }
        }

        // The last slot ends by 04:00 WEST, which is 03:00 UTC.
        ends[^1].ShouldBeLessThanOrEqualTo(new DateTime(2026, 3, 29, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task On_the_spring_forward_day_the_utc_instants_are_exact()
    {
        await using var app = AppAt("2026-03-20T12:00:00Z");
        var pitch = await OwnerWithPitchAsync(app);
        await PutScheduleAsync(app, pitch, Schedule(90, ("sunday", "00:00", "04:00")));

        SlotTimes(await SlotsAsync(app, pitch, "2026-03-29")).ShouldBe([
            "2026-03-29T00:00:00Z/2026-03-29T01:30:00Z",
            "2026-03-29T01:30:00Z/2026-03-29T03:00:00Z",
        ]);
    }

    [Theory]
    [InlineData(30, 10)]
    [InlineData(60, 5)]
    [InlineData(90, 3)]
    public async Task On_the_fall_back_day_00_00_to_04_00_is_five_real_hours(int minutes, int count)
    {
        // Lisbon, Sunday 25 October 2026: 02:00 WEST goes back to 01:00 WET.
        await using var app = AppAt("2026-10-20T12:00:00Z");
        var pitch = await OwnerWithPitchAsync(app);
        await PutScheduleAsync(app, pitch, Schedule(minutes, ("sunday", "00:00", "04:00")));

        var slots = (await SlotsAsync(app, pitch, "2026-10-25")).GetProperty("slots").EnumerateArray().ToList();

        slots.Count.ShouldBe(count);
        var starts = slots.Select(slot => slot.GetProperty("startsAt").GetDateTime().ToUniversalTime()).ToList();
        var ends = slots.Select(slot => slot.GetProperty("endsAt").GetDateTime().ToUniversalTime()).ToList();
        // 00:00 WEST is 23:00 UTC the day before; 04:00 WET is 04:00 UTC.
        starts[0].ShouldBe(new DateTime(2026, 10, 24, 23, 0, 0, DateTimeKind.Utc));
        for (var i = 0; i < count; i++)
        {
            (ends[i] - starts[i]).ShouldBe(TimeSpan.FromMinutes(minutes));
            if (i > 0)
            {
                starts[i].ShouldBe(ends[i - 1]);
            }
        }

        ends[^1].ShouldBeLessThanOrEqualTo(new DateTime(2026, 10, 25, 4, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task On_the_fall_back_day_the_utc_instants_are_exact_and_the_repeated_hour_has_both_slots()
    {
        await using var app = AppAt("2026-10-20T12:00:00Z");
        var pitch = await OwnerWithPitchAsync(app);
        await PutScheduleAsync(app, pitch, Schedule(60, ("sunday", "00:00", "04:00")));

        SlotTimes(await SlotsAsync(app, pitch, "2026-10-25")).ShouldBe([
            "2026-10-24T23:00:00Z/2026-10-25T00:00:00Z", // 00:00-01:00 WEST
            "2026-10-25T00:00:00Z/2026-10-25T01:00:00Z", // 01:00-01:00 (verão -> inverno)
            "2026-10-25T01:00:00Z/2026-10-25T02:00:00Z", // 01:00-02:00 WET
            "2026-10-25T02:00:00Z/2026-10-25T03:00:00Z",
            "2026-10-25T03:00:00Z/2026-10-25T04:00:00Z",
        ]);
    }

    [Fact]
    public async Task A_normal_sunday_has_four_hourly_slots_for_the_same_hours()
    {
        await using var app = AppAt("2026-10-20T12:00:00Z");
        var pitch = await OwnerWithPitchAsync(app);
        await PutScheduleAsync(app, pitch, Schedule(60, ("sunday", "00:00", "04:00")));

        (await SlotsAsync(app, pitch, "2026-11-01")).GetProperty("slots").GetArrayLength().ShouldBe(4);
    }

    [Theory]
    [InlineData("2026-10-24")] // the UTC date, but already the 25th in Lisbon
    [InlineData("2026-11-25")] // today + 31
    [InlineData("2025-10-25")]
    public async Task Dates_outside_today_to_30_days_ahead_in_lisbon_are_rejected(string date)
    {
        // 23:30 UTC on the 24th is 00:30 WEST on the 25th in Lisbon.
        await using var app = AppAt("2026-10-24T23:30:00Z");
        var pitch = await OwnerWithPitchAsync(app);

        using var response = await SendAsync(app, HttpMethod.Get, $"{pitch.Base}/slots?date={date}", pitch.AccessToken);

        (await FieldErrorsAsync(response, "date")).ShouldBe(["Escolhe uma data de hoje até 30 dias à frente."]);
    }

    [Theory]
    [InlineData("2026-10-25")] // today in Lisbon
    [InlineData("2026-11-24")] // today + 30
    public async Task Today_and_30_days_ahead_in_lisbon_are_accepted(string date)
    {
        await using var app = AppAt("2026-10-24T23:30:00Z");
        var pitch = await OwnerWithPitchAsync(app);

        using var response = await SendAsync(app, HttpMethod.Get, $"{pitch.Base}/slots?date={date}", pitch.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?date=")]
    [InlineData("?date=2026-3-29")]
    [InlineData("?date=29-03-2026")]
    [InlineData("?date=2026-02-30")]
    [InlineData("?date=amanha")]
    public async Task A_missing_or_malformed_date_is_rejected_on_the_date_field(string query)
    {
        var pitch = await OwnerWithPitchAsync(factory);

        using var response = await SendAsync(factory, HttpMethod.Get, $"{pitch.Base}/slots{query}", pitch.AccessToken);

        (await FieldErrorsAsync(response, "date")).ShouldBe(["Indica a data no formato AAAA-MM-DD, por exemplo 2026-10-25."]);
    }

    [Fact]
    public async Task An_invalid_week_is_rejected_field_by_field_and_the_stored_week_is_kept()
    {
        var pitch = await OwnerWithPitchAsync(factory);
        await PutScheduleAsync(factory, pitch, Schedule(60, ("monday", "09:00", "12:00")));
        var invalid = Schedule(45, ("monday", "22:00", "02:00"), ("tuesday", "09:15", "12:00"), ("wednesday", "9:00", "12:00"), ("thursday", "10:00", "10:00"));
        invalid["friday"] = new { closed = true, opens = "09:00", closes = "12:00" };
        invalid.Remove("sunday");

        using var response = await SendAsync(factory, HttpMethod.Put, $"{pitch.Base}/schedule", pitch.AccessToken, invalid);

        var errors = (await ProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed")).GetProperty("errors");
        string[] Messages(string key) => errors.GetProperty(key).EnumerateArray().Select(message => message.GetString()!).ToArray();
        Messages("slotDurationMinutes").ShouldBe(["A duração do slot tem de ser 30, 60 ou 90 minutos."]);
        Messages("monday.closes").ShouldBe(["A hora de fecho tem de ser depois da abertura. O fecho mais tarde é 00:00 (meia-noite); horários que passam da meia-noite não são aceites."]);
        Messages("tuesday.opens").ShouldBe(["As horas vão de meia em meia hora, por exemplo 09:00 ou 09:30."]);
        Messages("wednesday.opens").ShouldBe(["Indica a hora no formato HH:mm, por exemplo 09:00."]);
        Messages("thursday.closes").ShouldBe(["A hora de fecho tem de ser depois da abertura. O fecho mais tarde é 00:00 (meia-noite); horários que passam da meia-noite não são aceites."]);
        Messages("friday").ShouldBe(["Um dia fechado não tem horas: tira as horas de sexta-feira ou abre o dia."]);
        Messages("sunday").ShouldBe(["Indica o horário de domingo ou marca o dia como fechado."]);

        using var get = await SendAsync(factory, HttpMethod.Get, $"{pitch.Base}/schedule", pitch.AccessToken);
        var kept = await get.Content.ReadFromJsonAsync<JsonElement>(Ct);
        kept.GetProperty("slotDurationMinutes").GetInt32().ShouldBe(60);
        kept.GetProperty("monday").GetProperty("closes").GetString().ShouldBe("12:00");
    }

    [Fact]
    public async Task Another_owner_gets_404_on_the_schedule_and_slots_of_a_pitch_and_changes_nothing()
    {
        var pitchOfA = await OwnerWithPitchAsync(factory);
        await PutScheduleAsync(factory, pitchOfA, Schedule(60, ("monday", "09:00", "12:00")));
        var ownerB = (await factory.CreateSignedInAsync(Profiles.Owner)).Tokens.AccessToken;
        var clubOfB = await CreateClubAsync(factory, ownerB);

        var throughAsClub = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, $"{pitchOfA.Base}/schedule", null),
            (HttpMethod.Put, $"{pitchOfA.Base}/schedule", Schedule(30, ("monday", "00:00", "00:00"))),
            (HttpMethod.Put, $"{pitchOfA.Base}/schedule", new { }), // invalid, and still 404: ownership comes first
            (HttpMethod.Get, $"{pitchOfA.Base}/slots?date=2026-10-26", null),
            (HttpMethod.Get, $"{pitchOfA.Base}/slots", null),
        };
        foreach (var (method, path, body) in throughAsClub)
        {
            using var response = await SendAsync(factory, method, path, ownerB, body);
            await ProblemAsync(response, HttpStatusCode.NotFound, "club_not_found");
        }

        // A's pitch asked for through B's own club.
        var throughOwnClub = $"/api/manage/clubs/{clubOfB}/pitches/{pitchOfA.PitchId}";
        foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Get, $"{throughOwnClub}/schedule", null),
                     (HttpMethod.Put, $"{throughOwnClub}/schedule", Schedule(30)),
                     (HttpMethod.Get, $"{throughOwnClub}/slots?date=2026-10-26", null),
                 })
        {
            using var response = await SendAsync(factory, method, path, ownerB, body);
            await ProblemAsync(response, HttpStatusCode.NotFound, "pitch_not_found");
        }

        using var get = await SendAsync(factory, HttpMethod.Get, $"{pitchOfA.Base}/schedule", pitchOfA.AccessToken);
        var kept = await get.Content.ReadFromJsonAsync<JsonElement>(Ct);
        kept.GetProperty("slotDurationMinutes").GetInt32().ShouldBe(60);
        kept.GetProperty("monday").GetProperty("opens").GetString().ShouldBe("09:00");
    }

    [Fact]
    public async Task A_pitch_through_another_club_of_the_same_owner_is_pitch_not_found()
    {
        var pitch = await OwnerWithPitchAsync(factory);
        var otherClub = await CreateClubAsync(factory, pitch.AccessToken);
        var wrong = $"/api/manage/clubs/{otherClub}/pitches/{pitch.PitchId}";

        using var schedule = await SendAsync(factory, HttpMethod.Get, $"{wrong}/schedule", pitch.AccessToken);
        await ProblemAsync(schedule, HttpStatusCode.NotFound, "pitch_not_found");
        using var put = await SendAsync(factory, HttpMethod.Put, $"{wrong}/schedule", pitch.AccessToken, Schedule(60, ("monday", "09:00", "10:00")));
        await ProblemAsync(put, HttpStatusCode.NotFound, "pitch_not_found");
        using var slots = await SendAsync(factory, HttpMethod.Get, $"{wrong}/slots?date=2026-10-26", pitch.AccessToken);
        await ProblemAsync(slots, HttpStatusCode.NotFound, "pitch_not_found");
    }

    [Fact]
    public async Task A_player_gets_403_and_a_visitor_401()
    {
        var pitch = await OwnerWithPitchAsync(factory);
        var player = (await factory.CreateSignedInPlayerAsync()).Tokens.AccessToken;
        var calls = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, $"{pitch.Base}/schedule", null),
            (HttpMethod.Put, $"{pitch.Base}/schedule", Schedule(60)),
            (HttpMethod.Get, $"{pitch.Base}/slots?date=2026-10-26", null),
        };

        foreach (var (method, path, body) in calls)
        {
            using var asPlayer = await SendAsync(factory, method, path, player, body);
            await ProblemAsync(asPlayer, HttpStatusCode.Forbidden, "forbidden");
            using var anonymous = await SendAsync(factory, method, path, accessToken: null, body);
            await ProblemAsync(anonymous, HttpStatusCode.Unauthorized, "unauthorized");
        }
    }
}
