using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SportsBooking.Application.Common;
using SportsBooking.Application.Statistics;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Admin;

/// <summary>US27 (the clubs with the highest occupation) and US28 (new accounts over time), each on a database of its own.</summary>
public sealed class AdminStatisticsTests(ApiFactory factory)
{
    // Monday 4 to Sunday 10 May 2026. Every club's pitch is open on Mondays 09:00-13:00 (four hours) unless said otherwise;
    // the Monday slots are 08:00, 09:00, 10:00 and 11:00 UTC.
    private const string Week = "from=2026-05-04&to=2026-05-10";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task BookHoursAsync(AdminTestApp admin, Guid pitchId, int hours, int day = 4)
    {
        for (var hour = 0; hour < hours; hour++)
        {
            var start = new DateTime(2026, 5, day, 8 + hour, 0, 0, DateTimeKind.Utc);
            await admin.App.AddBookingAsync(pitchId, start, start.AddHours(1));
        }
    }

    private static async Task<(TestClub Club, TestPitch Pitch)> ClubAsync(AdminTestApp admin, string name, string owner = "Dono", string locality = "Porto")
    {
        var ownerId = await admin.AddOwnerAsync(owner);
        var club = await admin.App.CreateClubAsync(ownerId, name, locality);
        var pitch = await admin.App.CreatePitchAsync(club.Id, "Campo 1");
        return (club, pitch);
    }

    private static List<string> Names(JsonElement top) => top.GetProperty("clubs").EnumerateArray().Select(club => club.GetProperty("clubName").GetString()!).ToList();

    // ---- US27

    [Fact]
    public async Task The_top_ten_is_ordered_by_occupation_then_booked_hours_then_id_and_leaves_out_clubs_with_nothing_open()
    {
        await using var admin = await AdminTestApp.CreateAsync(factory);
        var (full, fullPitch) = await ClubAsync(admin, "Cheio");
        await BookHoursAsync(admin, fullPitch.Id, 4); // 4 of 4: 100 %
        var (_, threeQuarters) = await ClubAsync(admin, "Três quartos");
        await BookHoursAsync(admin, threeQuarters.Id, 3); // 75 %
        // Two at 50 %: the one with more booked hours comes first.
        var biggerOwner = await admin.AddOwnerAsync("Dono");
        var bigger = await admin.App.CreateClubAsync(biggerOwner, "Meio grande");
        var biggerPitch = await admin.App.CreatePitchAsync(bigger.Id, "Campo 1", open: [(DayOfWeek.Monday, "09:00", "13:00"), (DayOfWeek.Tuesday, "09:00", "13:00")]);
        await BookHoursAsync(admin, biggerPitch.Id, 4); // 4 of 8
        var (_, smallerPitch) = await ClubAsync(admin, "Meio pequeno");
        await BookHoursAsync(admin, smallerPitch.Id, 2); // 2 of 4
        // Two exactly alike (25 %, one hour): the club's id decides.
        var (tieA, tieAPitch) = await ClubAsync(admin, "Empate A");
        await BookHoursAsync(admin, tieAPitch.Id, 1);
        var (tieB, tieBPitch) = await ClubAsync(admin, "Empate B");
        await BookHoursAsync(admin, tieBPitch.Id, 1);
        // Five open and empty: 0 %, by id; only four fit in the ten.
        var empty = new List<TestClub>();
        for (var index = 1; index <= 5; index++)
        {
            empty.Add((await ClubAsync(admin, $"Vazio {index}")).Club);
        }

        // Left out: a club whose only pitch is inactive (it has bookings, but nothing open), and one closed every day.
        var (_, inactivePitch) = await ClubAsync(admin, "Inativo");
        await BookHoursAsync(admin, inactivePitch.Id, 2);
        await admin.App.DeactivatePitchAsync(inactivePitch.Id);
        var (_, closedPitch) = await ClubAsync(admin, "Fechado");
        await admin.App.ChangeScheduleAsync(closedPitch.Id, 60);

        var top = await admin.GetJsonAsync($"/api/admin/clubs/top-occupation?{Week}");

        var ties = new[] { tieA, tieB }.OrderBy(club => club.Id.ToString("D"), StringComparer.Ordinal).Select(club => club.Name);
        var zeros = empty.OrderBy(club => club.Id.ToString("D"), StringComparer.Ordinal).Take(4).Select(club => club.Name);
        Names(top).ShouldBe(["Cheio", "Três quartos", "Meio grande", "Meio pequeno", .. ties, .. zeros]);
        Names(top).ShouldNotContain("Inativo");
        Names(top).ShouldNotContain("Fechado");
        var first = top.GetProperty("clubs")[0];
        first.GetProperty("clubId").GetGuid().ShouldBe(full.Id);
        first.GetProperty("slug").GetString().ShouldBe(full.Slug);
        first.GetProperty("ownerName").GetString().ShouldBe("Dono");
        first.GetProperty("locality").GetString().ShouldBe("Porto");
        first.GetProperty("occupation").GetDecimal().ShouldBe(1m);
        first.GetProperty("bookedHours").GetDecimal().ShouldBe(4m);
        first.GetProperty("openHours").GetDecimal().ShouldBe(4m);
        top.GetProperty("clubs")[2].GetProperty("occupation").GetDecimal().ShouldBe(0.5m);
        top.GetProperty("clubs")[2].GetProperty("bookedHours").GetDecimal().ShouldBe(4m);
        top.GetProperty("from").GetString().ShouldBe("2026-05-04");
        top.GetProperty("to").GetString().ShouldBe("2026-05-10");
    }

    [Fact]
    public async Task A_club_has_the_same_occupation_in_the_top_as_with_the_owners_formula()
    {
        await using var admin = await AdminTestApp.CreateAsync(factory);
        var ownerId = await admin.AddOwnerAsync("Dona Clube");
        var club = await admin.App.CreateClubAsync(ownerId, "Clube Medido");
        var hourly = await admin.App.CreatePitchAsync(club.Id, "A");
        var ninety = await admin.App.CreatePitchAsync(club.Id, "B", slotMinutes: 90, open: [(DayOfWeek.Monday, "09:00", "13:00"), (DayOfWeek.Wednesday, "09:00", "12:00")]);
        await BookHoursAsync(admin, hourly.Id, 3);
        var noShow = await admin.App.AddBookingAsync(ninety.Id, new DateTime(2026, 5, 6, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 6, 9, 30, 0, DateTimeKind.Utc));
        await admin.App.SetBookingStatusAsync(noShow.Id, "NoShow");
        var cancelled = await admin.App.AddBookingAsync(ninety.Id, new DateTime(2026, 5, 4, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 4, 9, 30, 0, DateTimeKind.Utc));
        await admin.App.SetBookingStatusAsync(cancelled.Id, "CancelledByClub");

        var top = await admin.GetJsonAsync($"/api/admin/clubs/top-occupation?{Week}");

        await using var scope = admin.App.Services.CreateAsyncScope();
        var occupation = scope.ServiceProvider.GetRequiredService<OccupationService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var rows = await occupation.ForPitchesAsync(dbContext.Pitches.Where(pitch => pitch.ClubId == club.Id), new StatisticsPeriod(new DateOnly(2026, 5, 4), new DateOnly(2026, 5, 10)), Ct);
        var figures = OccupationFigures.Sum(rows.Select(row => row.Figures));

        var line = top.GetProperty("clubs").EnumerateArray().Single(candidate => candidate.GetProperty("clubId").GetGuid() == club.Id);
        // 3 h + 1 h 30 booked of 4 h + 7 h open: 270 / 660 minutes.
        figures.Occupation.ShouldBe(0.4091m);
        line.GetProperty("occupation").GetDecimal().ShouldBe(figures.Occupation);
        line.GetProperty("bookedHours").GetDecimal().ShouldBe(4.5m);
        line.GetProperty("openHours").GetDecimal().ShouldBe(11m);
    }

    [Fact]
    public async Task The_top_holds_no_player_data()
    {
        await using var admin = await AdminTestApp.CreateAsync(factory);
        var (_, pitch) = await ClubAsync(admin, "Clube");
        var playerEmail = $"jogadora-secreta-{Guid.NewGuid():N}@exemplo.test";
        var player = await admin.App.CreateAccountAsync(Profiles.Player, null, playerEmail, "Jogadora Secretíssima");
        await admin.App.AddBookingAsync(pitch.Id, new DateTime(2026, 5, 4, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 4, 9, 0, 0, DateTimeKind.Utc), player);

        using var response = await admin.GetAsync($"/api/admin/clubs/top-occupation?{Week}");
        var body = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotContain("Secret", Case.Insensitive);
        body.ShouldNotContain(playerEmail, Case.Insensitive);
        body.ShouldNotContain(player.Id.ToString(), Case.Insensitive);
    }

    [Theory]
    [InlineData("from=2026-05-10&to=2026-05-04", "to")]
    [InlineData("from=2025-01-01&to=2026-01-02", "to")]
    [InlineData("from=ontem", "from")]
    public async Task A_wrong_period_for_the_top_is_a_400(string query, string field)
    {
        await using var admin = await AdminTestApp.CreateAsync(factory);

        var problem = await admin.GetProblemAsync($"/api/admin/clubs/top-occupation?{query}");

        problem.GetProperty("errors").EnumerateObject().Select(property => property.Name).ShouldContain(field);
    }

    // ---- US28

    private static async Task RegisteredAtAsync(AdminTestApp admin, Guid accountId, string instant)
    {
        await using var scope = admin.App.Services.CreateAsyncScope();
        var when = TestVenues.Utc(instant);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlAsync($"UPDATE users SET registered_at = {when} WHERE id = {accountId}", Ct);
    }

    /// <summary>
    /// Owners: 18 Oct 23:30 UTC (00:30 on the 19th in Lisbon, summer time: the week of the 19th, not of the 12th), 19 Oct, 25 Oct
    /// 23:30 UTC (Sunday the 25th in Lisbon, winter time: still the week of the 19th) and 26 Oct 00:30. Players: 30 Sep 22:30 UTC
    /// (September), 30 Sep 23:30 UTC (1 October in Lisbon), 5 Oct, and two on 20 Oct, one of them deactivated. An admin registered in
    /// October never counts.
    /// </summary>
    private static async Task SeedRegistrationsAsync(AdminTestApp admin)
    {
        foreach (var instant in new[] { "2026-10-18T23:30:00Z", "2026-10-19T08:00:00Z", "2026-10-25T23:30:00Z", "2026-10-26T00:30:00Z" })
        {
            await admin.AddOwnerAsync("Dono", registeredAt: TestVenues.Utc(instant));
        }

        foreach (var instant in new[] { "2026-09-30T22:30:00Z", "2026-09-30T23:30:00Z", "2026-10-05T10:00:00Z", "2026-10-20T10:00:00Z", "2026-10-20T11:00:00Z" })
        {
            var player = await admin.AddPlayerAsync();
            await RegisteredAtAsync(admin, player, instant);
            if (instant == "2026-10-20T11:00:00Z")
            {
                await admin.App.DeactivateAccountAsync(player);
            }
        }

        var other = await admin.App.CreateAccountAsync(Profiles.Admin, null, name: "Outro Admin");
        await RegisteredAtAsync(admin, other.Id, "2026-10-20T12:00:00Z");
    }

    private static int[] Column(JsonElement response, string name) =>
        response.GetProperty("buckets").EnumerateArray().Select(bucket => bucket.GetProperty(name).GetInt32()).ToArray();

    [Fact]
    public async Task Registrations_by_week_start_on_monday_use_the_lisbon_date_and_fill_empty_weeks()
    {
        await using var admin = await AdminTestApp.CreateAsync(factory);
        await SeedRegistrationsAsync(admin);

        var response = await admin.GetJsonAsync("/api/admin/registrations?granularity=week&from=2026-10-14&to=2026-10-28");

        response.GetProperty("granularity").GetString().ShouldBe("week");
        response.GetProperty("from").GetString().ShouldBe("2026-10-12");
        response.GetProperty("to").GetString().ShouldBe("2026-11-01");
        response.GetProperty("buckets").EnumerateArray().Select(bucket => bucket.GetProperty("start").GetString()).ShouldBe(["2026-10-12", "2026-10-19", "2026-10-26"]);
        Column(response, "owners").ShouldBe([0, 3, 1]);
        Column(response, "players").ShouldBe([0, 2, 0]);
        response.GetProperty("owners").GetInt32().ShouldBe(4);
        response.GetProperty("players").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Registrations_by_month_use_the_lisbon_month()
    {
        await using var admin = await AdminTestApp.CreateAsync(factory);
        await SeedRegistrationsAsync(admin);

        var response = await admin.GetJsonAsync("/api/admin/registrations?granularity=month&from=2026-08-15&to=2026-10-31");

        response.GetProperty("buckets").EnumerateArray().Select(bucket => bucket.GetProperty("start").GetString()).ShouldBe(["2026-08-01", "2026-09-01", "2026-10-01"]);
        Column(response, "owners").ShouldBe([0, 0, 4]); // all four are October in Lisbon, the one at 23:30 UTC on 18 October included
        Column(response, "players").ShouldBe([0, 1, 4]);
    }

    [Fact]
    public async Task Registrations_default_to_weeks_over_the_last_30_days()
    {
        await using var admin = await AdminTestApp.CreateAsync(factory);

        var response = await admin.GetJsonAsync("/api/admin/registrations");

        // The clock is Wednesday 28 October 2026: 29 Sep .. 28 Oct, widened to Monday 28 Sep .. Sunday 1 Nov.
        response.GetProperty("granularity").GetString().ShouldBe("week");
        response.GetProperty("from").GetString().ShouldBe("2026-09-28");
        response.GetProperty("to").GetString().ShouldBe("2026-11-01");
        Column(response, "owners").ShouldAllBe(count => count == 0);
    }

    [Theory]
    [InlineData("granularity=day", "granularity")]
    [InlineData("granularity=year", "granularity")]
    [InlineData("granularity=week&from=2024-01-01&to=2025-12-29", "to")]
    [InlineData("granularity=month&from=2026-10-10&to=2026-10-01", "to")]
    [InlineData("from=hoje", "from")]
    public async Task A_wrong_request_for_registrations_is_a_400(string query, string field)
    {
        await using var admin = await AdminTestApp.CreateAsync(factory);

        var problem = await admin.GetProblemAsync($"/api/admin/registrations?{query}");

        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.GetProperty("errors").EnumerateObject().Select(property => property.Name).ShouldContain(field);
    }
}
