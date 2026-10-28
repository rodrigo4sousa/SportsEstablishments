using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Bookings;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Pitches;

/// <summary>US10: price bands per pitch, the one price function behind every price, and T02 around them.</summary>
public sealed class PriceBandTests(ApiFactory factory)
{
    // Friday 1 May 2026, 11:00 in Lisbon (WEST). Monday 4 May is open 09:00-13:00 (TestVenues.MondayMorning): slots at 08, 09, 10 and 11 UTC.
    private const string MayClock = "2026-05-01T10:00:00Z";
    private const string Monday = "2026-05-04";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> AppAt(string utcNow) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture)))));

    private sealed record Venue(string OwnerToken, Guid OwnerId, TestClub Club, TestPitch Pitch)
    {
        public string Bands => $"/api/manage/clubs/{Club.Id}/pitches/{Pitch.Id}/price-bands";

        public string Base => $"/api/manage/clubs/{Club.Id}/pitches/{Pitch.Id}";
    }

    /// <summary>An owner with a club and a pitch of 20 € (Monday 09:00-13:00 by default), signed in.</summary>
    private async Task<Venue> VenueAsync(int slotMinutes = 60, params (DayOfWeek Day, string Opens, string Closes)[] open)
    {
        var (owner, tokens) = await factory.CreateSignedInAsync(Profiles.Owner);
        var club = await factory.CreateClubAsync(owner.Id);
        var pitch = await factory.CreatePitchAsync(club.Id, price: 20m, slotMinutes: slotMinutes, open: open);
        return new Venue(tokens.AccessToken, owner.Id, club, pitch);
    }

    private static async Task<HttpResponseMessage> SendAsync(WebApplicationFactory<Program> app, HttpMethod method, string path, string? token, object? body = null)
    {
        using var client = app.CreateSiteClient();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (token is not null)
        {
            request.WithCookies((SessionCookies.AccessToken, token));
        }

        return await client.SendAsync(request, Ct);
    }

    private static object Band(string[] days, string start, string end, decimal price) => new { days, start, end, price };

    private static readonly string[] Weekdays = ["monday", "tuesday", "wednesday", "thursday", "friday"];

    private async Task<JsonElement> CreateBandAsync(Venue venue, string[] days, string start, string end, decimal price)
    {
        using var response = await SendAsync(factory, HttpMethod.Post, venue.Bands, venue.OwnerToken, Band(days, start, end, price));
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

    private static async Task<JsonElement> FieldErrorsAsync(HttpResponseMessage response)
    {
        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        return problem.GetProperty("errors");
    }

    // ---- CRUD

    [Fact]
    public async Task An_owner_creates_lists_updates_and_deletes_bands_of_their_pitch()
    {
        var venue = await VenueAsync();

        using var created = await SendAsync(factory, HttpMethod.Post, venue.Bands, venue.OwnerToken, Band(["friday", "monday"], "18:00", "22:00", 30m));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var band = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = band.GetProperty("id").GetGuid();
        id.Version.ShouldBe(7);
        created.Headers.Location!.ToString().ShouldBe($"{venue.Bands}/{id}");
        band.GetProperty("pitchId").GetGuid().ShouldBe(venue.Pitch.Id);
        band.GetProperty("days").EnumerateArray().Select(day => day.GetString()).ShouldBe(["monday", "friday"]); // week order
        band.GetProperty("start").GetString().ShouldBe("18:00");
        band.GetProperty("end").GetString().ShouldBe("22:00");
        band.GetProperty("price").GetDecimal().ShouldBe(30m);

        await CreateBandAsync(venue, ["saturday", "sunday"], "09:00", "13:00", 24m);
        using var list = await SendAsync(factory, HttpMethod.Get, venue.Bands, venue.OwnerToken);
        var listed = (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().ToList();
        listed.Select(item => item.GetProperty("start").GetString()).ShouldBe(["09:00", "18:00"]); // by start time

        using var updated = await SendAsync(factory, HttpMethod.Put, $"{venue.Bands}/{id}", venue.OwnerToken, Band(["monday"], "19:00", "00:00", 35.5m));
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync(Ct));
        var after = await updated.Content.ReadFromJsonAsync<JsonElement>(Ct);
        after.GetProperty("id").GetGuid().ShouldBe(id);
        after.GetProperty("days").EnumerateArray().Select(day => day.GetString()).ShouldBe(["monday"]);
        after.GetProperty("end").GetString().ShouldBe("00:00");
        after.GetProperty("price").GetDecimal().ShouldBe(35.5m);

        using var deleted = await SendAsync(factory, HttpMethod.Delete, $"{venue.Bands}/{id}", venue.OwnerToken);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var again = await SendAsync(factory, HttpMethod.Get, venue.Bands, venue.OwnerToken);
        (await again.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(1);
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().PriceBands.AsNoTracking().AnyAsync(row => row.Id == id, Ct)).ShouldBeFalse(); // a real delete
    }

    [Fact]
    public async Task A_pitch_without_bands_lists_none()
    {
        var venue = await VenueAsync();

        using var list = await SendAsync(factory, HttpMethod.Get, venue.Bands, venue.OwnerToken);

        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Bands_of_one_pitch_are_not_listed_on_another()
    {
        var venue = await VenueAsync();
        var other = await factory.CreatePitchAsync(venue.Club.Id, "Campo 2");
        await CreateBandAsync(venue, Weekdays, "18:00", "22:00", 30m);

        using var list = await SendAsync(factory, HttpMethod.Get, $"/api/manage/clubs/{venue.Club.Id}/pitches/{other.Id}/price-bands", venue.OwnerToken);

        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(0);
    }

    // ---- Validation

    [Fact]
    public async Task An_empty_set_of_weekdays_is_a_400_on_days()
    {
        var venue = await VenueAsync();

        using var response = await SendAsync(factory, HttpMethod.Post, venue.Bands, venue.OwnerToken, Band([], "18:00", "22:00", 30m));

        (await FieldErrorsAsync(response)).GetProperty("days")[0].GetString().ShouldBe("Escolhe pelo menos um dia da semana.");
    }

    [Fact]
    public async Task An_end_before_the_start_is_a_400_on_end()
    {
        var venue = await VenueAsync();

        using var response = await SendAsync(factory, HttpMethod.Post, venue.Bands, venue.OwnerToken, Band(Weekdays, "22:00", "18:00", 30m));

        (await FieldErrorsAsync(response)).GetProperty("end")[0].GetString()!.ShouldStartWith("O fim tem de ser depois do início.");
    }

    [Theory]
    [InlineData("18:15", "22:00", "start")]
    [InlineData("18:00", "22:10", "end")]
    [InlineData("", "22:00", "start")]
    [InlineData("18:00", "late", "end")]
    public async Task A_time_off_the_step_or_unreadable_is_a_400_on_its_field(string start, string end, string field)
    {
        var venue = await VenueAsync();

        using var response = await SendAsync(factory, HttpMethod.Post, venue.Bands, venue.OwnerToken, Band(Weekdays, start, end, 30m));

        (await FieldErrorsAsync(response)).TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, "O preço tem de ser maior que 0 €.")]
    [InlineData(-1, "O preço tem de ser maior que 0 €.")]
    [InlineData(500.5, "O preço não pode passar de 500 €.")]
    [InlineData(10.123, "O preço pode ter no máximo duas casas decimais.")]
    public async Task A_price_out_of_range_is_a_400_on_price(double price, string message)
    {
        var venue = await VenueAsync();

        using var response = await SendAsync(factory, HttpMethod.Post, venue.Bands, venue.OwnerToken, Band(Weekdays, "18:00", "22:00", (decimal)price));

        (await FieldErrorsAsync(response)).GetProperty("price")[0].GetString().ShouldBe(message);
        (await (await SendAsync(factory, HttpMethod.Get, venue.Bands, venue.OwnerToken)).Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_band_past_midnight_is_refused_and_one_ending_at_midnight_is_accepted()
    {
        var venue = await VenueAsync();

        using var past = await SendAsync(factory, HttpMethod.Post, venue.Bands, venue.OwnerToken, Band(Weekdays, "22:00", "02:00", 30m));
        (await FieldErrorsAsync(past)).TryGetProperty("end", out _).ShouldBeTrue();

        var ok = await CreateBandAsync(venue, Weekdays, "22:00", "00:00", 30m);
        ok.GetProperty("end").GetString().ShouldBe("00:00");
    }

    [Fact]
    public async Task Validation_is_judged_on_update_too_and_the_band_is_left_alone()
    {
        var venue = await VenueAsync();
        var band = await CreateBandAsync(venue, Weekdays, "18:00", "22:00", 30m);
        var id = band.GetProperty("id").GetGuid();

        using var response = await SendAsync(factory, HttpMethod.Put, $"{venue.Bands}/{id}", venue.OwnerToken, Band([], "18:00", "22:00", 0m));

        var errors = await FieldErrorsAsync(response);
        errors.TryGetProperty("days", out _).ShouldBeTrue();
        errors.TryGetProperty("price", out _).ShouldBeTrue();
        using var list = await SendAsync(factory, HttpMethod.Get, venue.Bands, venue.OwnerToken);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct))[0].GetProperty("price").GetDecimal().ShouldBe(30m);
    }

    // ---- Overlap and the limit

    [Fact]
    public async Task A_band_that_overlaps_another_on_a_shared_weekday_is_a_409()
    {
        var venue = await VenueAsync();
        await CreateBandAsync(venue, ["monday", "tuesday"], "18:00", "20:00", 30m);

        using var response = await SendAsync(factory, HttpMethod.Post, venue.Bands, venue.OwnerToken, Band(["tuesday", "wednesday"], "19:00", "21:00", 35m));

        await ProblemAsync(response, HttpStatusCode.Conflict, ErrorCodes.PriceBandOverlap);
    }

    [Fact]
    public async Task Bands_that_touch_or_share_hours_on_different_weekdays_are_accepted()
    {
        var venue = await VenueAsync();
        await CreateBandAsync(venue, ["monday"], "18:00", "20:00", 30m);

        await CreateBandAsync(venue, ["monday"], "20:00", "22:00", 35m); // touching in time
        await CreateBandAsync(venue, ["tuesday"], "18:00", "20:00", 28m); // the same hours, another weekday
        await CreateBandAsync(venue, ["monday"], "16:00", "18:00", 25m); // touching from the other side
    }

    [Fact]
    public async Task A_band_to_midnight_overlaps_a_late_band()
    {
        var venue = await VenueAsync();
        await CreateBandAsync(venue, ["monday"], "22:00", "00:00", 30m);

        using var response = await SendAsync(factory, HttpMethod.Post, venue.Bands, venue.OwnerToken, Band(["monday"], "23:00", "23:30", 35m));

        await ProblemAsync(response, HttpStatusCode.Conflict, ErrorCodes.PriceBandOverlap);
    }

    [Fact]
    public async Task Updating_a_band_overlapping_another_is_a_409_but_it_may_overlap_its_own_old_range()
    {
        var venue = await VenueAsync();
        var first = (await CreateBandAsync(venue, ["monday"], "18:00", "20:00", 30m)).GetProperty("id").GetGuid();
        await CreateBandAsync(venue, ["monday"], "20:00", "22:00", 35m);

        using var inside = await SendAsync(factory, HttpMethod.Put, $"{venue.Bands}/{first}", venue.OwnerToken, Band(["monday"], "17:00", "20:00", 31m));
        inside.StatusCode.ShouldBe(HttpStatusCode.OK, await inside.Content.ReadAsStringAsync(Ct));

        using var clash = await SendAsync(factory, HttpMethod.Put, $"{venue.Bands}/{first}", venue.OwnerToken, Band(["monday"], "17:00", "21:00", 31m));
        await ProblemAsync(clash, HttpStatusCode.Conflict, ErrorCodes.PriceBandOverlap);
    }

    [Fact]
    public async Task Two_simultaneous_overlapping_bands_make_one_201_and_one_409()
    {
        var venue = await VenueAsync();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(async attempt =>
            {
                using var response = await SendAsync(factory, HttpMethod.Post, venue.Bands, venue.OwnerToken, Band(["monday"], "18:00", "20:00", 30m + attempt));
                return response.StatusCode;
            }));

        responses.Count(status => status == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(status => status == HttpStatusCode.Conflict).ShouldBe(3);
    }

    [Fact]
    public async Task A_pitch_takes_at_most_twenty_bands_and_the_next_is_a_422()
    {
        var venue = await VenueAsync();
        for (var slot = 0; slot < 20; slot++)
        {
            var start = TimeSpan.FromMinutes(slot * 30);
            await CreateBandAsync(venue, ["monday"], $"{start:hh\\:mm}", $"{start + TimeSpan.FromMinutes(30):hh\\:mm}", 20m + slot);
        }

        using var response = await SendAsync(factory, HttpMethod.Post, venue.Bands, venue.OwnerToken, Band(["tuesday"], "10:00", "11:00", 30m));

        await ProblemAsync(response, HttpStatusCode.UnprocessableEntity, ErrorCodes.PriceBandLimitReached);
    }

    [Fact]
    public async Task A_band_that_is_not_on_the_pitch_is_a_404_for_update_and_delete()
    {
        var venue = await VenueAsync();
        var otherPitch = await factory.CreatePitchAsync(venue.Club.Id, "Campo 2");
        using var onOther = await SendAsync(factory, HttpMethod.Post, $"/api/manage/clubs/{venue.Club.Id}/pitches/{otherPitch.Id}/price-bands", venue.OwnerToken, Band(Weekdays, "18:00", "22:00", 30m));
        var foreign = (await onOther.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        foreach (var id in new[] { foreign, Guid.CreateVersion7() })
        {
            using var put = await SendAsync(factory, HttpMethod.Put, $"{venue.Bands}/{id}", venue.OwnerToken, Band(Weekdays, "18:00", "22:00", 30m));
            await ProblemAsync(put, HttpStatusCode.NotFound, ErrorCodes.PriceBandNotFound);
            using var delete = await SendAsync(factory, HttpMethod.Delete, $"{venue.Bands}/{id}", venue.OwnerToken);
            await ProblemAsync(delete, HttpStatusCode.NotFound, ErrorCodes.PriceBandNotFound);
        }
    }

    // ---- T02 and the profiles

    [Fact]
    public async Task Another_owners_club_is_a_404_club_not_found_and_a_pitch_of_another_club_a_404_pitch_not_found()
    {
        var venue = await VenueAsync();
        var (_, stranger) = await factory.CreateSignedInAsync(Profiles.Owner);
        var band = (await CreateBandAsync(venue, Weekdays, "18:00", "22:00", 30m)).GetProperty("id").GetGuid();
        var otherClub = await factory.CreateClubAsync(venue.OwnerId, name: "Outro clube");

        foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Get, venue.Bands, null),
                     (HttpMethod.Post, venue.Bands, Band(Weekdays, "10:00", "11:00", 30m)),
                     (HttpMethod.Put, $"{venue.Bands}/{band}", Band(Weekdays, "10:00", "11:00", 30m)),
                     (HttpMethod.Delete, $"{venue.Bands}/{band}", null),
                 })
        {
            using var response = await SendAsync(factory, method, path, stranger.AccessToken, body);
            await ProblemAsync(response, HttpStatusCode.NotFound, ErrorCodes.ClubNotFound);
        }

        // The owner's own pitch addressed through their other club.
        using var wrongClub = await SendAsync(factory, HttpMethod.Get, $"/api/manage/clubs/{otherClub.Id}/pitches/{venue.Pitch.Id}/price-bands", venue.OwnerToken);
        await ProblemAsync(wrongClub, HttpStatusCode.NotFound, ErrorCodes.PitchNotFound);

        // The stranger changed nothing.
        using var list = await SendAsync(factory, HttpMethod.Get, venue.Bands, venue.OwnerToken);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Another_owners_club_is_a_404_before_the_body_is_validated()
    {
        var venue = await VenueAsync();
        var (_, stranger) = await factory.CreateSignedInAsync(Profiles.Owner);

        using var response = await SendAsync(factory, HttpMethod.Post, venue.Bands, stranger.AccessToken, Band([], "bad", "bad", 0m));

        await ProblemAsync(response, HttpStatusCode.NotFound, ErrorCodes.ClubNotFound);
    }

    [Theory]
    [InlineData("Player")]
    [InlineData("Admin")]
    public async Task A_player_or_the_admin_is_a_403_on_every_endpoint(string profile)
    {
        var venue = await VenueAsync();
        var band = (await CreateBandAsync(venue, Weekdays, "18:00", "22:00", 30m)).GetProperty("id").GetGuid();
        var (_, tokens) = await factory.CreateSignedInAsync(profile);

        foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Get, venue.Bands, null),
                     (HttpMethod.Post, venue.Bands, Band(Weekdays, "10:00", "11:00", 30m)),
                     (HttpMethod.Put, $"{venue.Bands}/{band}", Band(Weekdays, "10:00", "11:00", 30m)),
                     (HttpMethod.Delete, $"{venue.Bands}/{band}", null),
                 })
        {
            using var response = await SendAsync(factory, method, path, tokens.AccessToken, body);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {path}");
        }
    }

    [Fact]
    public async Task Without_a_session_every_endpoint_is_a_401()
    {
        var venue = await VenueAsync();
        var band = Guid.CreateVersion7();

        foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Get, venue.Bands, null),
                     (HttpMethod.Post, venue.Bands, Band(Weekdays, "10:00", "11:00", 30m)),
                     (HttpMethod.Put, $"{venue.Bands}/{band}", Band(Weekdays, "10:00", "11:00", 30m)),
                     (HttpMethod.Delete, $"{venue.Bands}/{band}", null),
                 })
        {
            using var response = await SendAsync(factory, method, path, token: null, body);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {path}");
        }
    }

    // ---- The price function behind every price

    [Fact]
    public async Task A_booking_stores_the_band_price_of_its_slot_or_the_base_price_outside_every_band()
    {
        var venue = await VenueAsync();
        await CreateBandAsync(venue, ["monday"], "10:00", "12:00", 35m); // local 10:00 and 11:00
        var app = AppAt(MayClock);
        var player = (await factory.CreateSignedInPlayerAsync()).Tokens.AccessToken;

        var prices = new List<decimal>();
        foreach (var startsAt in new[] { "2026-05-04T08:00:00Z", "2026-05-04T09:00:00Z", "2026-05-04T10:00:00Z", "2026-05-04T11:00:00Z" })
        {
            using var response = await SendAsync(app, HttpMethod.Post, "/api/bookings", player, new { pitchId = venue.Pitch.Id, startsAt });
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
            prices.Add((await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("price").GetDecimal());
        }

        prices.ShouldBe([20m, 35m, 35m, 20m]); // 09:00 and 12:00 local are outside the band
    }

    [Fact]
    public async Task A_booking_the_club_makes_for_a_customer_stores_the_band_price_too()
    {
        var venue = await VenueAsync();
        await CreateBandAsync(venue, ["monday"], "10:00", "12:00", 35m);
        var app = AppAt(MayClock);

        var prices = new List<decimal>();
        foreach (var startsAt in new[] { "2026-05-04T08:00:00Z", "2026-05-04T09:00:00Z" })
        {
            using var response = await SendAsync(app, HttpMethod.Post, $"/api/manage/clubs/{venue.Club.Id}/bookings", venue.OwnerToken,
                new { pitchId = venue.Pitch.Id, startsAt, customerName = "Cliente", customerPhone = "912345678" });
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
            prices.Add((await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("price").GetDecimal());
        }

        prices.ShouldBe([20m, 35m]);
    }

    [Fact]
    public async Task The_public_slots_the_owner_preview_and_the_stored_booking_price_all_agree_for_every_slot()
    {
        var venue = await VenueAsync(slotMinutes: 30, open: [(DayOfWeek.Monday, "09:00", "13:00")]);
        await CreateBandAsync(venue, ["monday"], "10:00", "11:30", 31m);
        await CreateBandAsync(venue, ["monday"], "11:30", "00:00", 27.5m);
        var app = AppAt(MayClock);
        var player = (await factory.CreateSignedInPlayerAsync()).Tokens.AccessToken;

        using var publicResponse = await app.CreateClient().GetAsync($"/api/clubs/{venue.Club.Slug}/pitches/{venue.Pitch.Id}/slots?date={Monday}", Ct);
        var publicSlots = (await publicResponse.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("slots").EnumerateArray().ToList();
        using var previewResponse = await SendAsync(app, HttpMethod.Get, $"{venue.Base}/slots?date={Monday}", venue.OwnerToken);
        var previewSlots = (await previewResponse.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("slots").EnumerateArray().ToList();

        publicSlots.Count.ShouldBe(8);
        previewSlots.Count.ShouldBe(8);
        publicSlots.Select(slot => slot.GetProperty("price").GetDecimal()).ShouldBe([20m, 20m, 31m, 31m, 31m, 27.5m, 27.5m, 27.5m]);
        for (var index = 0; index < publicSlots.Count; index++)
        {
            var startsAt = publicSlots[index].GetProperty("startsAt").GetDateTime();
            previewSlots[index].GetProperty("startsAt").GetDateTime().ShouldBe(startsAt);
            var shown = publicSlots[index].GetProperty("price").GetDecimal();
            previewSlots[index].GetProperty("price").GetDecimal().ShouldBe(shown);

            using var booked = await SendAsync(app, HttpMethod.Post, "/api/bookings", player, new { pitchId = venue.Pitch.Id, startsAt = startsAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) });
            booked.StatusCode.ShouldBe(HttpStatusCode.Created, await booked.Content.ReadAsStringAsync(Ct));
            var id = (await booked.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
            await using var scope = factory.Services.CreateAsyncScope();
            (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.AsNoTracking().SingleAsync(booking => booking.Id == id, Ct)).Price.ShouldBe(shown);
        }
    }

    [Fact]
    public async Task A_slot_longer_than_the_band_takes_the_price_of_the_band_where_it_starts()
    {
        // 90-minute slots from 09:00: 09:00, 10:30, 12:00 (it ends at 13:30; a slot from 13:30 would end after the closing time, so it does not exist).
        var venue = await VenueAsync(slotMinutes: 90, open: [(DayOfWeek.Monday, "09:00", "14:00")]);
        await CreateBandAsync(venue, ["monday"], "10:00", "11:00", 40m); // slot 10:30 starts inside it and runs to 12:00

        var app = AppAt(MayClock);
        using var response = await app.CreateClient().GetAsync($"/api/clubs/{venue.Club.Slug}/pitches/{venue.Pitch.Id}/slots?date={Monday}", Ct);
        var slots = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("slots").EnumerateArray().ToList();

        slots.Select(slot => slot.GetProperty("price").GetDecimal()).ShouldBe([20m, 40m, 20m]);
    }

    [Fact]
    public async Task On_the_day_the_clocks_go_back_both_one_oclock_slots_get_the_band_price_by_local_time()
    {
        // Sunday 2026-10-25 lasts 25 hours: 00:00 and 01:00 WEST, then 01:00, 02:00 and 03:00 WET. The band is 01:00-03:00 local.
        var venue = await VenueAsync(open: [(DayOfWeek.Sunday, "00:00", "04:00")]);
        await CreateBandAsync(venue, ["sunday"], "01:00", "03:00", 33m);
        var app = AppAt("2026-10-20T10:00:00Z");

        using var publicResponse = await app.CreateClient().GetAsync($"/api/clubs/{venue.Club.Slug}/pitches/{venue.Pitch.Id}/slots?date=2026-10-25", Ct);
        var publicSlots = (await publicResponse.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("slots").EnumerateArray().ToList();
        using var previewResponse = await SendAsync(app, HttpMethod.Get, $"{venue.Base}/slots?date=2026-10-25", venue.OwnerToken);
        var previewSlots = (await previewResponse.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("slots").EnumerateArray().ToList();

        publicSlots.Select(slot => slot.GetProperty("startsAt").GetDateTime().ToUniversalTime().ToString("HH:mm", CultureInfo.InvariantCulture))
            .ShouldBe(["23:00", "00:00", "01:00", "02:00", "03:00"]); // 00:00 WEST, 01:00 WEST, 01:00 WET, 02:00 WET, 03:00 WET
        publicSlots.Select(slot => slot.GetProperty("price").GetDecimal()).ShouldBe([20m, 33m, 33m, 33m, 20m]);
        previewSlots.Select(slot => slot.GetProperty("price").GetDecimal()).ShouldBe([20m, 33m, 33m, 33m, 20m]);

        // And a booking of each of the two one o'clock slots stores the band price.
        var player = (await factory.CreateSignedInPlayerAsync()).Tokens.AccessToken;
        foreach (var startsAt in new[] { "2026-10-25T00:00:00Z", "2026-10-25T01:00:00Z" })
        {
            using var booked = await SendAsync(app, HttpMethod.Post, "/api/bookings", player, new { pitchId = venue.Pitch.Id, startsAt });
            booked.StatusCode.ShouldBe(HttpStatusCode.Created, await booked.Content.ReadAsStringAsync(Ct));
            (await booked.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("price").GetDecimal().ShouldBe(33m);
        }
    }

    // ---- Existing bookings keep their price

    [Fact]
    public async Task A_booking_keeps_its_price_when_the_band_is_changed_or_deleted_later()
    {
        var venue = await VenueAsync();
        var band = (await CreateBandAsync(venue, ["monday"], "10:00", "12:00", 35m)).GetProperty("id").GetGuid();
        var app = AppAt(MayClock);
        var player = (await factory.CreateSignedInPlayerAsync()).Tokens.AccessToken;
        using var booked = await SendAsync(app, HttpMethod.Post, "/api/bookings", player, new { pitchId = venue.Pitch.Id, startsAt = "2026-05-04T09:00:00Z" });
        var id = (await booked.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        (await SendAsync(app, HttpMethod.Put, $"{venue.Bands}/{band}", venue.OwnerToken, Band(["monday"], "10:00", "12:00", 50m))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await AssertStoredAsync(id, 35m);
        (await SendAsync(app, HttpMethod.Delete, $"{venue.Bands}/{band}", venue.OwnerToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await AssertStoredAsync(id, 35m);

        // The slot's next booking (another slot, now without the band) costs the base price.
        using var next = await SendAsync(app, HttpMethod.Post, "/api/bookings", player, new { pitchId = venue.Pitch.Id, startsAt = "2026-05-04T10:00:00Z" });
        (await next.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("price").GetDecimal().ShouldBe(20m);

        using var mine = await SendAsync(app, HttpMethod.Get, $"/api/bookings/{id}", player);
        (await mine.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("price").GetDecimal().ShouldBe(35m);
    }

    private async Task AssertStoredAsync(Guid bookingId, decimal price)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var booking = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.AsNoTracking().SingleAsync(candidate => candidate.Id == bookingId, Ct);
        booking.Price.ShouldBe(price);
        booking.Status.ShouldBe(BookingStatus.Confirmed);
    }

    // ---- The public club page

    [Fact]
    public async Task The_club_page_shows_the_base_price_and_says_which_pitches_have_bands()
    {
        var venue = await VenueAsync();
        var plain = await factory.CreatePitchAsync(venue.Club.Id, "Campo 2", price: 15m);
        using var before = await factory.CreateClient().GetAsync($"/api/clubs/{venue.Club.Slug}", Ct);
        var pitchesBefore = (await before.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("pitches").EnumerateArray().ToList();
        pitchesBefore.ShouldAllBe(pitch => !pitch.GetProperty("hasPriceBands").GetBoolean());

        await CreateBandAsync(venue, Weekdays, "18:00", "22:00", 30m);

        using var after = await factory.CreateClient().GetAsync($"/api/clubs/{venue.Club.Slug}", Ct);
        var pitches = (await after.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("pitches").EnumerateArray().ToList();
        var withBands = pitches.Single(pitch => pitch.GetProperty("id").GetGuid() == venue.Pitch.Id);
        withBands.GetProperty("hasPriceBands").GetBoolean().ShouldBeTrue();
        withBands.GetProperty("pricePerSlot").GetDecimal().ShouldBe(20m); // still the base price
        var without = pitches.Single(pitch => pitch.GetProperty("id").GetGuid() == plain.Id);
        without.GetProperty("hasPriceBands").GetBoolean().ShouldBeFalse();
        without.GetProperty("pricePerSlot").GetDecimal().ShouldBe(15m);
    }

    [Fact]
    public async Task Changing_the_base_price_moves_the_slots_outside_the_bands_and_leaves_the_bands_alone()
    {
        var venue = await VenueAsync();
        await CreateBandAsync(venue, ["monday"], "10:00", "12:00", 35m);
        await factory.ChangePriceAsync(venue.Pitch.Id, 25m);
        var app = AppAt(MayClock);

        using var response = await app.CreateClient().GetAsync($"/api/clubs/{venue.Club.Slug}/pitches/{venue.Pitch.Id}/slots?date={Monday}", Ct);
        var slots = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("slots").EnumerateArray().ToList();

        slots.Select(slot => slot.GetProperty("price").GetDecimal()).ShouldBe([25m, 35m, 35m, 25m]);
    }
}
