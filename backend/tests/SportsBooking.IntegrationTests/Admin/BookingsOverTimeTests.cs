using System.Text.Json;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Admin;

/// <summary>
/// US25: bookings per period, by the Lisbon date they were created. The data is built around the clock change of Sunday 25
/// October 2026 (01:00 UTC): Lisbon midnight of the 25th is 23:00 UTC on the 24th, the day lasts 25 hours, and the 26th starts
/// at 00:00 UTC.
/// </summary>
public sealed class BookingsOverTimeTests(ApiFactory factory)
{
    private static string[] Starts(JsonElement response) =>
        response.GetProperty("buckets").EnumerateArray().Select(bucket => bucket.GetProperty("start").GetString()!).ToArray();

    private static int[] Counts(JsonElement response) =>
        response.GetProperty("buckets").EnumerateArray().Select(bucket => bucket.GetProperty("count").GetInt32()).ToArray();

    /// <summary>
    /// Created (UTC → Lisbon date): 22:30 on 30 Sep → 30 Sep; 23:30 on 30 Sep → 1 Oct (summer time); 22:59 on 24 Oct → 24 Oct;
    /// 23:00 on 24 Oct, 00:30, 23:30 and 23:59:59 on the 25th → 25 Oct; 00:00 on 26 Oct → 26 Oct. Then, on the 26th a booking
    /// cancelled by the player, on the 27th one cancelled by the club and a no-show. The 24 Oct one is a customer's.
    /// </summary>
    private async Task<AdminTestApp> SeedAsync()
    {
        var admin = await AdminTestApp.CreateAsync(factory);
        var club = await admin.App.CreateClubAsync();
        var pitch = await admin.App.CreatePitchAsync(club.Id);
        var player = await admin.AddPlayerAsync();

        await admin.AddBookingAsync(pitch.Id, "2026-09-30T22:30:00Z", player);
        await admin.AddBookingAsync(pitch.Id, "2026-09-30T23:30:00Z", player);
        await admin.AddBookingAsync(pitch.Id, "2026-10-24T22:59:00Z");
        await admin.AddBookingAsync(pitch.Id, "2026-10-24T23:00:00Z", player);
        await admin.AddBookingAsync(pitch.Id, "2026-10-25T00:30:00Z", player);
        await admin.AddBookingAsync(pitch.Id, "2026-10-25T23:30:00Z", player);
        await admin.AddBookingAsync(pitch.Id, "2026-10-25T23:59:59Z", player);
        await admin.AddBookingAsync(pitch.Id, "2026-10-26T00:00:00Z", player);
        await admin.AddBookingAsync(pitch.Id, "2026-10-26T09:00:00Z", player, "CancelledByPlayer");
        await admin.AddBookingAsync(pitch.Id, "2026-10-27T09:00:00Z", player, "CancelledByClub");
        await admin.AddBookingAsync(pitch.Id, "2026-10-27T10:00:00Z", player, "NoShow");
        return admin;
    }

    [Fact]
    public async Task Days_are_cut_on_the_lisbon_date_around_the_clock_change()
    {
        await using var admin = await SeedAsync();

        var response = await admin.GetJsonAsync("/api/admin/bookings-over-time?granularity=day&from=2026-10-23&to=2026-10-27");

        response.GetProperty("granularity").GetString().ShouldBe("day");
        Starts(response).ShouldBe(["2026-10-23", "2026-10-24", "2026-10-25", "2026-10-26", "2026-10-27"]);
        // 24th: the customer's at 22:59 UTC. 25th: four, from 23:00 UTC on the 24th to 23:59:59 UTC on the 25th (25 hours).
        // 26th: the one at 00:00 UTC. 27th: the no-show (a cancellation is not counted).
        Counts(response).ShouldBe([0, 1, 4, 1, 1]);
        response.GetProperty("total").GetInt32().ShouldBe(7);
        response.GetProperty("includeCancelled").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_bucket_says_its_first_and_last_day()
    {
        await using var admin = await SeedAsync();

        var response = await admin.GetJsonAsync("/api/admin/bookings-over-time?granularity=day&from=2026-10-25&to=2026-10-25");

        var bucket = response.GetProperty("buckets").EnumerateArray().Single();
        bucket.GetProperty("start").GetString().ShouldBe("2026-10-25");
        bucket.GetProperty("end").GetString().ShouldBe("2026-10-25");
    }

    [Fact]
    public async Task Cancelled_bookings_are_counted_only_when_asked_for()
    {
        await using var admin = await SeedAsync();

        var response = await admin.GetJsonAsync("/api/admin/bookings-over-time?granularity=day&from=2026-10-25&to=2026-10-27&includeCancelled=true");

        Counts(response).ShouldBe([4, 2, 2]);
        response.GetProperty("total").GetInt32().ShouldBe(8);
        response.GetProperty("includeCancelled").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Weeks_start_on_monday_and_the_range_is_widened_to_whole_weeks()
    {
        await using var admin = await SeedAsync();

        // Wednesday the 14th to Wednesday the 28th: the weeks of Monday 12th, 19th and 26th.
        var response = await admin.GetJsonAsync("/api/admin/bookings-over-time?granularity=week&from=2026-10-14&to=2026-10-28");

        Starts(response).ShouldBe(["2026-10-12", "2026-10-19", "2026-10-26"]);
        Counts(response).ShouldBe([0, 5, 2]);
        response.GetProperty("from").GetString().ShouldBe("2026-10-12");
        response.GetProperty("to").GetString().ShouldBe("2026-11-01");
        var lastWeek = response.GetProperty("buckets").EnumerateArray().Last();
        lastWeek.GetProperty("end").GetString().ShouldBe("2026-11-01");
    }

    [Fact]
    public async Task Months_use_the_lisbon_month_and_empty_ones_show_a_zero()
    {
        await using var admin = await SeedAsync();

        var response = await admin.GetJsonAsync("/api/admin/bookings-over-time?granularity=month&from=2026-08-15&to=2026-10-28");

        Starts(response).ShouldBe(["2026-08-01", "2026-09-01", "2026-10-01"]);
        // 23:30 UTC on 30 September is already 1 October in Lisbon (summer time): 1 + 7 more.
        Counts(response).ShouldBe([0, 1, 8]);
        response.GetProperty("buckets").EnumerateArray().Last().GetProperty("end").GetString().ShouldBe("2026-10-31");
    }

    [Fact]
    public async Task Without_parameters_it_is_the_last_30_days_in_lisbon_by_day()
    {
        await using var admin = await SeedAsync();

        var response = await admin.GetJsonAsync("/api/admin/bookings-over-time");

        response.GetProperty("granularity").GetString().ShouldBe("day");
        var starts = Starts(response);
        starts.Length.ShouldBe(30);
        starts[0].ShouldBe("2026-09-29");
        starts[^1].ShouldBe("2026-10-28");
        var counts = Counts(response);
        // 30 Sep, 1 Oct, 24th, the four of the 25th, 26th, 27th (the no-show).
        counts.Sum().ShouldBe(9);
        counts.Count(count => count == 0).ShouldBe(24);
        counts[1].ShouldBe(1);
        counts[2].ShouldBe(1);
        response.GetProperty("total").GetInt32().ShouldBe(9);
    }

    [Fact]
    public async Task Bookings_made_by_the_club_for_a_customer_are_counted()
    {
        await using var admin = await AdminTestApp.CreateAsync(factory);
        var club = await admin.App.CreateClubAsync();
        var pitch = await admin.App.CreatePitchAsync(club.Id);
        await admin.AddBookingAsync(pitch.Id, "2026-10-20T10:00:00Z");

        var response = await admin.GetJsonAsync("/api/admin/bookings-over-time?from=2026-10-20&to=2026-10-20");

        Counts(response).ShouldBe([1]);
    }

    [Theory]
    [InlineData("day", "2024-01-01", "2024-12-31", 366)]
    [InlineData("week", "2024-01-01", "2025-12-28", 104)]
    [InlineData("month", "2022-01-01", "2026-12-31", 60)]
    public async Task The_longest_range_allowed_is_answered(string granularity, string from, string to, int periods)
    {
        await using var admin = await AdminTestApp.CreateAsync(factory);

        var response = await admin.GetJsonAsync($"/api/admin/bookings-over-time?granularity={granularity}&from={from}&to={to}");

        response.GetProperty("buckets").GetArrayLength().ShouldBe(periods);
        Counts(response).ShouldAllBe(count => count == 0);
    }

    [Theory]
    [InlineData("day", "2024-01-01", "2025-01-01")]
    [InlineData("week", "2024-01-01", "2025-12-29")]
    [InlineData("month", "2022-01-01", "2027-01-01")]
    public async Task One_period_more_than_the_longest_range_is_refused(string granularity, string from, string to)
    {
        await using var admin = await AdminTestApp.CreateAsync(factory);

        var problem = await admin.GetProblemAsync($"/api/admin/bookings-over-time?granularity={granularity}&from={from}&to={to}");

        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.GetProperty("errors").GetProperty("to")[0].GetString()!.ShouldContain("demasiados períodos");
    }

    [Theory]
    [InlineData("granularity=year", "granularity")]
    [InlineData("from=hoje", "from")]
    [InlineData("to=2026-13-01", "to")]
    [InlineData("from=1999-12-31&to=2000-01-02", "from")]
    [InlineData("to=2101-01-01", "to")]
    [InlineData("from=2026-10-10&to=2026-10-09", "to")]
    [InlineData("from=2026-11-05", "from")]
    [InlineData("includeCancelled=talvez", "includeCancelled")]
    public async Task A_wrong_value_is_a_400_on_that_field(string query, string field)
    {
        await using var admin = await AdminTestApp.CreateAsync(factory);

        var problem = await admin.GetProblemAsync($"/api/admin/bookings-over-time?{query}");

        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.GetProperty("errors").EnumerateObject().Select(property => property.Name).ShouldContain(field);
    }
}
