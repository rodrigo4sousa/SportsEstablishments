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
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Pitches;

/// <summary>US09 and T02: an owner blocks periods of a pitch, and only of their own pitches.</summary>
public sealed class BlockTests(ApiFactory factory)
{
    private static readonly string[] Days = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The API on the same database with its clock at <paramref name="utcNow"/>. Sessions come from the shared app (see PitchScheduleTests).</summary>
    private WebApplicationFactory<Program> AppAt(string utcNow) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture)))));

    private sealed record OwnerPitch(string AccessToken, Guid ClubId, Guid PitchId)
    {
        public string Base => $"/api/manage/clubs/{ClubId}/pitches/{PitchId}";

        public string Blocks => $"{Base}/blocks";
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

    private async Task<OwnerPitch> OwnerWithPitchAsync(string? token = null, Guid? clubId = null, string pitchName = "Campo 1")
    {
        token ??= (await factory.CreateSignedInAsync(Profiles.Owner)).Tokens.AccessToken;
        if (clubId is null)
        {
            using var club = await SendAsync(factory, HttpMethod.Post, "/api/manage/clubs", token, new
            {
                name = $"Clube {Guid.NewGuid():N}",
                address = "Rua 1",
                locality = "Porto",
                phone = "912345678",
                contactEmail = "geral@clube.pt",
            });
            club.StatusCode.ShouldBe(HttpStatusCode.Created);
            clubId = (await club.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        }

        using var pitch = await SendAsync(factory, HttpMethod.Post, $"/api/manage/clubs/{clubId}/pitches", token, new { name = pitchName, sport = "padel", pricePerSlot = 20m });
        pitch.StatusCode.ShouldBe(HttpStatusCode.Created);
        return new OwnerPitch(token, clubId.Value, (await pitch.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> CreateBlockAsync(WebApplicationFactory<Program> app, OwnerPitch pitch, string start, string end, string? reason = null)
    {
        using var response = await SendAsync(app, HttpMethod.Post, pitch.Blocks, pitch.AccessToken, new { start, end, reason });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

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

    private static string Utc(JsonElement element, string property) =>
        element.GetProperty(property).GetDateTime().ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    private static async Task SetWeekAsync(WebApplicationFactory<Program> app, OwnerPitch pitch, int minutes, string day, string opens, string closes)
    {
        var week = new Dictionary<string, object?> { ["slotDurationMinutes"] = minutes };
        foreach (var d in Days)
        {
            week[d] = d == day ? new { closed = false, opens, closes } : new { closed = true };
        }

        using var response = await SendAsync(app, HttpMethod.Put, $"{pitch.Base}/schedule", pitch.AccessToken, week);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<bool[]> BlockedFlagsAsync(WebApplicationFactory<Program> app, OwnerPitch pitch, string date)
    {
        using var response = await SendAsync(app, HttpMethod.Get, $"{pitch.Base}/slots?date={date}", pitch.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var slots = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("slots");
        return slots.EnumerateArray().Select(slot => slot.GetProperty("blocked").GetBoolean()).ToArray();
    }

    private async Task<Domain.Pitches.Block> StoredAsync(Guid blockId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Blocks.AsNoTracking().SingleAsync(block => block.Id == blockId, Ct);
    }

    [Fact]
    public async Task An_owner_blocks_a_period_given_in_lisbon_time_and_gets_it_back_in_utc()
    {
        await using var app = AppAt("2026-05-01T10:00:00Z");
        var pitch = await OwnerWithPitchAsync();

        using var response = await SendAsync(app, HttpMethod.Post, pitch.Blocks, pitch.AccessToken, new { start = "2026-05-04T09:00", end = "2026-05-04T11:00", reason = "  Manutenção do piso " });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var block = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = block.GetProperty("id").GetGuid();
        id.Version.ShouldBe(7);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"{pitch.Blocks}/{id}");
        block.GetProperty("pitchId").GetGuid().ShouldBe(pitch.PitchId);
        // May is summer time in Lisbon (UTC+1).
        Utc(block, "startsAt").ShouldBe("2026-05-04T08:00");
        Utc(block, "endsAt").ShouldBe("2026-05-04T10:00");
        block.GetProperty("reason").GetString().ShouldBe("Manutenção do piso");
        var stored = await StoredAsync(id);
        stored.StartsAt.ShouldBe(new DateTime(2026, 5, 4, 8, 0, 0, DateTimeKind.Utc));
        stored.EndsAt.ShouldBe(new DateTime(2026, 5, 4, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task The_reason_is_optional()
    {
        await using var app = AppAt("2026-05-01T10:00:00Z");
        var pitch = await OwnerWithPitchAsync();

        var block = await CreateBlockAsync(app, pitch, "2026-05-04T09:00", "2026-05-04T10:00");

        block.GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_list_has_ongoing_and_future_blocks_by_start_one_page_at_a_time()
    {
        await using var past = AppAt("2026-05-01T10:00:00Z");
        var pitch = await OwnerWithPitchAsync();
        await CreateBlockAsync(past, pitch, "2026-05-04T14:00", "2026-05-04T15:00", "terceiro");
        await CreateBlockAsync(past, pitch, "2026-05-04T09:00", "2026-05-04T11:00", "ongoing");
        await CreateBlockAsync(past, pitch, "2026-05-02T09:00", "2026-05-02T10:00", "already over");
        await CreateBlockAsync(past, pitch, "2026-05-05T09:00", "2026-05-05T10:00", "last");

        // 10:30 WEST on the 4th: the 09:00-11:00 block is ongoing, the one on the 2nd is over.
        await using var app = AppAt("2026-05-04T09:30:00Z");
        using var response = await SendAsync(app, HttpMethod.Get, pitch.Blocks, pitch.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        page.GetProperty("totalCount").GetInt32().ShouldBe(3);
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("reason").GetString()).ShouldBe(["ongoing", "terceiro", "last"]);

        using var second = await SendAsync(app, HttpMethod.Get, $"{pitch.Blocks}?page=2&pageSize=2", pitch.AccessToken);
        var secondPage = await second.Content.ReadFromJsonAsync<JsonElement>(Ct);
        secondPage.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("reason").GetString()).ShouldBe(["last"]);
    }

    [Fact]
    public async Task A_block_not_started_can_be_deleted_and_the_slots_are_free_again()
    {
        await using var app = AppAt("2026-05-01T10:00:00Z");
        var pitch = await OwnerWithPitchAsync();
        await SetWeekAsync(app, pitch, 60, "monday", "09:00", "12:00");
        var block = await CreateBlockAsync(app, pitch, "2026-05-04T10:00", "2026-05-04T11:00");
        (await BlockedFlagsAsync(app, pitch, "2026-05-04")).ShouldBe([false, true, false]);

        using var delete = await SendAsync(app, HttpMethod.Delete, $"{pitch.Blocks}/{block.GetProperty("id").GetGuid()}", pitch.AccessToken);

        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await BlockedFlagsAsync(app, pitch, "2026-05-04")).ShouldBe([false, false, false]);
        using var again = await SendAsync(app, HttpMethod.Delete, $"{pitch.Blocks}/{block.GetProperty("id").GetGuid()}", pitch.AccessToken);
        await ProblemAsync(again, HttpStatusCode.NotFound, "block_not_found");
    }

    [Theory]
    [InlineData("2026-05-04T08:00:00Z")] // exactly at the start (09:00 WEST)
    [InlineData("2026-05-04T08:30:00Z")] // ongoing
    [InlineData("2026-05-04T12:00:00Z")] // over
    public async Task A_block_that_has_started_or_is_over_cannot_be_deleted(string now)
    {
        await using var before = AppAt("2026-05-01T10:00:00Z");
        var pitch = await OwnerWithPitchAsync();
        var block = await CreateBlockAsync(before, pitch, "2026-05-04T09:00", "2026-05-04T11:00");
        var id = block.GetProperty("id").GetGuid();

        await using var app = AppAt(now);
        using var response = await SendAsync(app, HttpMethod.Delete, $"{pitch.Blocks}/{id}", pitch.AccessToken);

        await ProblemAsync(response, HttpStatusCode.UnprocessableEntity, "block_already_started");
        (await StoredAsync(id)).Id.ShouldBe(id);
    }

    [Fact]
    public async Task A_block_one_minute_before_it_starts_can_still_be_deleted()
    {
        await using var before = AppAt("2026-05-01T10:00:00Z");
        var pitch = await OwnerWithPitchAsync();
        var block = await CreateBlockAsync(before, pitch, "2026-05-04T09:00", "2026-05-04T11:00");

        await using var app = AppAt("2026-05-04T07:59:00Z");
        using var response = await SendAsync(app, HttpMethod.Delete, $"{pitch.Blocks}/{block.GetProperty("id").GetGuid()}", pitch.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Overlapping_blocks_of_a_pitch_are_rejected_with_409_and_touching_blocks_are_allowed()
    {
        await using var app = AppAt("2026-05-01T10:00:00Z");
        var pitch = await OwnerWithPitchAsync();
        await CreateBlockAsync(app, pitch, "2026-05-04T09:00", "2026-05-04T11:00");

        foreach (var (start, end) in new[]
                 {
                     ("2026-05-04T09:00", "2026-05-04T11:00"), // the same
                     ("2026-05-04T10:00", "2026-05-04T10:30"), // inside
                     ("2026-05-04T08:00", "2026-05-04T09:30"), // overlaps the start
                     ("2026-05-04T10:30", "2026-05-04T12:00"), // overlaps the end
                     ("2026-05-04T08:00", "2026-05-04T12:00"), // around it
                 })
        {
            using var response = await SendAsync(app, HttpMethod.Post, pitch.Blocks, pitch.AccessToken, new { start, end });
            await ProblemAsync(response, HttpStatusCode.Conflict, "block_overlap");
        }

        await CreateBlockAsync(app, pitch, "2026-05-04T11:00", "2026-05-04T12:00"); // starts when the first ends
        await CreateBlockAsync(app, pitch, "2026-05-04T08:00", "2026-05-04T09:00"); // ends when the first starts
        using var list = await SendAsync(app, HttpMethod.Get, pitch.Blocks, pitch.AccessToken);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("totalCount").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task Blocks_of_different_pitches_do_not_overlap_each_other()
    {
        await using var app = AppAt("2026-05-01T10:00:00Z");
        var first = await OwnerWithPitchAsync();
        var second = await OwnerWithPitchAsync(first.AccessToken, first.ClubId, "Campo 2");

        await CreateBlockAsync(app, first, "2026-05-04T09:00", "2026-05-04T11:00");
        await CreateBlockAsync(app, second, "2026-05-04T09:00", "2026-05-04T11:00");
    }

    [Fact]
    public async Task A_block_that_starts_in_the_past_is_rejected_on_the_start_field()
    {
        await using var app = AppAt("2026-05-04T08:00:00Z"); // 09:00 WEST
        var pitch = await OwnerWithPitchAsync();

        using var response = await SendAsync(app, HttpMethod.Post, pitch.Blocks, pitch.AccessToken, new { start = "2026-05-04T08:30", end = "2026-05-04T10:00" });
        (await FieldErrorsAsync(response, "start")).ShouldBe(["O início não pode estar no passado."]);

        await CreateBlockAsync(app, pitch, "2026-05-04T09:00", "2026-05-04T10:00"); // starting right now is not the past
    }

    [Fact]
    public async Task Each_invalid_field_gets_its_own_message_and_nothing_is_created()
    {
        await using var app = AppAt("2026-05-01T10:00:00Z");
        var pitch = await OwnerWithPitchAsync();

        using var response = await SendAsync(app, HttpMethod.Post, pitch.Blocks, pitch.AccessToken, new { start = "2026-05-04T09:15", end = "04/05/2026", reason = new string('x', 201) });

        var errors = (await ProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed")).GetProperty("errors");
        errors.GetProperty("start").EnumerateArray().Select(m => m.GetString()).ShouldBe(["As horas vão de meia em meia hora, por exemplo 09:00 ou 09:30."]);
        errors.GetProperty("end").EnumerateArray().Select(m => m.GetString()).ShouldBe(["Indica a data e a hora no formato AAAA-MM-DDTHH:mm, por exemplo 2026-10-25T09:00."]);
        errors.GetProperty("reason").EnumerateArray().Select(m => m.GetString()).ShouldBe(["O motivo pode ter no máximo 200 caracteres."]);
        using var list = await SendAsync(app, HttpMethod.Get, pitch.Blocks, pitch.AccessToken);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task The_end_must_be_after_the_start_and_within_90_days()
    {
        await using var app = AppAt("2026-05-01T10:00:00Z");
        var pitch = await OwnerWithPitchAsync();

        using var before = await SendAsync(app, HttpMethod.Post, pitch.Blocks, pitch.AccessToken, new { start = "2026-05-04T10:00", end = "2026-05-04T09:00" });
        (await FieldErrorsAsync(before, "end")).ShouldBe(["O fim tem de ser depois do início."]);
        using var same = await SendAsync(app, HttpMethod.Post, pitch.Blocks, pitch.AccessToken, new { start = "2026-05-04T10:00", end = "2026-05-04T10:00" });
        (await FieldErrorsAsync(same, "end")).ShouldBe(["O fim tem de ser depois do início."]);
        using var far = await SendAsync(app, HttpMethod.Post, pitch.Blocks, pitch.AccessToken, new { start = "2026-07-30T10:00", end = "2026-08-02T10:00" });
        (await FieldErrorsAsync(far, "end")).ShouldBe(["O bloqueio não pode acabar daqui a mais de 90 dias."]);
        await CreateBlockAsync(app, pitch, "2026-07-29T10:00", "2026-07-30T10:00"); // ends within 90 days of 1 May + 10:00Z
    }

    [Fact]
    public async Task A_block_across_the_october_clock_change_is_stored_in_the_right_utc_instants()
    {
        await using var app = AppAt("2026-10-20T12:00:00Z");
        var pitch = await OwnerWithPitchAsync();

        // Lisbon, Sunday 25 October 2026: 02:00 WEST goes back to 01:00 WET. 00:00 WEST is 23:00Z the day before; 04:00 WET is 04:00Z.
        var block = await CreateBlockAsync(app, pitch, "2026-10-25T00:00", "2026-10-25T04:00");

        Utc(block, "startsAt").ShouldBe("2026-10-24T23:00");
        Utc(block, "endsAt").ShouldBe("2026-10-25T04:00");
        (await StoredAsync(block.GetProperty("id").GetGuid())).EndsAt.ShouldBe(new DateTime(2026, 10, 25, 4, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task A_repeated_local_time_uses_its_first_occurrence_and_a_missing_one_moves_forward()
    {
        await using var autumn = AppAt("2026-10-20T12:00:00Z");
        var pitch = await OwnerWithPitchAsync();
        // 01:30 happens twice on 25 October: the first (WEST) is 00:30Z. 02:30 is WET, 02:30Z.
        var repeated = await CreateBlockAsync(autumn, pitch, "2026-10-25T01:30", "2026-10-25T02:30");
        Utc(repeated, "startsAt").ShouldBe("2026-10-25T00:30");
        Utc(repeated, "endsAt").ShouldBe("2026-10-25T02:30");

        await using var spring = AppAt("2026-03-20T12:00:00Z");
        var other = await OwnerWithPitchAsync();
        // 01:30 does not exist on 29 March: it moves forward to 02:30 WEST, 01:30Z. 03:00 WEST is 02:00Z.
        var missing = await CreateBlockAsync(spring, other, "2026-03-29T01:30", "2026-03-29T03:00");
        Utc(missing, "startsAt").ShouldBe("2026-03-29T01:30");
        Utc(missing, "endsAt").ShouldBe("2026-03-29T02:00");
    }

    [Fact]
    public async Task The_preview_marks_slots_that_overlap_a_block_including_a_partial_overlap_on_the_clock_change_day()
    {
        await using var app = AppAt("2026-10-20T12:00:00Z");
        var pitch = await OwnerWithPitchAsync();
        await SetWeekAsync(app, pitch, 60, "sunday", "00:00", "04:00");
        // 01:30 WEST (00:30Z) to 02:30 WET (02:30Z): cuts into slots 2, 3 and 4 of the five 25 October slots.
        await CreateBlockAsync(app, pitch, "2026-10-25T01:30", "2026-10-25T02:30");

        (await BlockedFlagsAsync(app, pitch, "2026-10-25")).ShouldBe([false, true, true, true, false]);
        (await BlockedFlagsAsync(app, pitch, "2026-11-01")).ShouldBe([false, false, false, false]);
    }

    [Fact]
    public async Task A_slot_that_only_touches_a_block_is_not_marked()
    {
        await using var app = AppAt("2026-05-01T10:00:00Z");
        var pitch = await OwnerWithPitchAsync();
        await SetWeekAsync(app, pitch, 60, "monday", "08:00", "13:00");
        await CreateBlockAsync(app, pitch, "2026-05-04T10:00", "2026-05-04T11:00");

        (await BlockedFlagsAsync(app, pitch, "2026-05-04")).ShouldBe([false, false, true, false, false]);
    }

    [Fact]
    public async Task A_block_spanning_several_slots_marks_all_of_them_and_other_pitches_are_not_affected()
    {
        await using var app = AppAt("2026-05-01T10:00:00Z");
        var pitch = await OwnerWithPitchAsync();
        var other = await OwnerWithPitchAsync(pitch.AccessToken, pitch.ClubId, "Campo 2");
        await SetWeekAsync(app, pitch, 90, "monday", "09:00", "15:00");
        await SetWeekAsync(app, other, 90, "monday", "09:00", "15:00");
        await CreateBlockAsync(app, pitch, "2026-05-04T10:00", "2026-05-04T13:00");

        // Slots: 09:00-10:30, 10:30-12:00, 12:00-13:30, 13:30-15:00.
        (await BlockedFlagsAsync(app, pitch, "2026-05-04")).ShouldBe([true, true, true, false]);
        (await BlockedFlagsAsync(app, other, "2026-05-04")).ShouldBe([false, false, false, false]);
    }

    [Fact]
    public async Task Concurrent_overlapping_creates_produce_exactly_one_block()
    {
        var pitch = await OwnerWithPitchAsync();
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var statuses = await Task.WhenAll(Enumerable.Range(0, 8).Select(async attempt =>
        {
            // Different but overlapping periods, so only the lock (not an identical-row constraint) can order them.
            using var response = await SendAsync(factory, HttpMethod.Post, pitch.Blocks, pitch.AccessToken, new { start = $"{day}T10:00", end = attempt % 2 == 0 ? $"{day}T11:00" : $"{day}T11:30" });
            return response.StatusCode;
        }));

        statuses.Count(status => status == HttpStatusCode.Created).ShouldBe(1);
        statuses.Count(status => status == HttpStatusCode.Conflict).ShouldBe(7);
        using var list = await SendAsync(factory, HttpMethod.Get, pitch.Blocks, pitch.AccessToken);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("totalCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Another_owner_gets_404_on_everything_and_changes_nothing()
    {
        await using var app = AppAt("2026-05-01T10:00:00Z");
        var pitchOfA = await OwnerWithPitchAsync();
        var block = await CreateBlockAsync(app, pitchOfA, "2026-05-04T09:00", "2026-05-04T10:00");
        var blockId = block.GetProperty("id").GetGuid();
        var ownerB = (await factory.CreateSignedInAsync(Profiles.Owner)).Tokens.AccessToken;

        var attempts = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Post, pitchOfA.Blocks, new { start = "2026-05-05T09:00", end = "2026-05-05T10:00" }),
            (HttpMethod.Post, pitchOfA.Blocks, new { }), // invalid, and still 404: ownership comes first
            (HttpMethod.Get, pitchOfA.Blocks, null),
            (HttpMethod.Delete, $"{pitchOfA.Blocks}/{blockId}", null),
        };
        foreach (var (method, path, body) in attempts)
        {
            using var response = await SendAsync(app, method, path, ownerB, body);
            await ProblemAsync(response, HttpStatusCode.NotFound, "club_not_found");
        }

        (await StoredAsync(blockId)).Id.ShouldBe(blockId);
        using var list = await SendAsync(app, HttpMethod.Get, pitchOfA.Blocks, pitchOfA.AccessToken);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("totalCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task A_pitch_through_the_wrong_club_and_a_block_through_the_wrong_pitch_are_404()
    {
        await using var app = AppAt("2026-05-01T10:00:00Z");
        var pitch = await OwnerWithPitchAsync();
        var otherPitch = await OwnerWithPitchAsync(pitch.AccessToken, pitch.ClubId, "Campo 2");
        var block = await CreateBlockAsync(app, pitch, "2026-05-04T09:00", "2026-05-04T10:00");
        var blockId = block.GetProperty("id").GetGuid();

        // Another club of the same owner.
        var otherClubPitch = await OwnerWithPitchAsync(pitch.AccessToken);
        var wrongClub = $"/api/manage/clubs/{otherClubPitch.ClubId}/pitches/{pitch.PitchId}/blocks";
        foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Get, wrongClub, null),
                     (HttpMethod.Post, wrongClub, new { start = "2026-05-05T09:00", end = "2026-05-05T10:00" }),
                     (HttpMethod.Delete, $"{wrongClub}/{blockId}", null),
                 })
        {
            using var response = await SendAsync(app, method, path, pitch.AccessToken, body);
            await ProblemAsync(response, HttpStatusCode.NotFound, "pitch_not_found");
        }

        // The block through another pitch of the same club, and an unknown block.
        using var wrongPitch = await SendAsync(app, HttpMethod.Delete, $"{otherPitch.Blocks}/{blockId}", pitch.AccessToken);
        await ProblemAsync(wrongPitch, HttpStatusCode.NotFound, "block_not_found");
        using var unknown = await SendAsync(app, HttpMethod.Delete, $"{pitch.Blocks}/{Guid.CreateVersion7()}", pitch.AccessToken);
        await ProblemAsync(unknown, HttpStatusCode.NotFound, "block_not_found");
        (await StoredAsync(blockId)).Id.ShouldBe(blockId);
    }

    [Fact]
    public async Task A_player_gets_403_and_a_visitor_401_on_every_block_endpoint()
    {
        var pitch = await OwnerWithPitchAsync();
        var player = (await factory.CreateSignedInPlayerAsync()).Tokens.AccessToken;
        var calls = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Post, pitch.Blocks, new { start = "2026-12-05T09:00", end = "2026-12-05T10:00" }),
            (HttpMethod.Get, pitch.Blocks, null),
            (HttpMethod.Delete, $"{pitch.Blocks}/{Guid.CreateVersion7()}", null),
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
