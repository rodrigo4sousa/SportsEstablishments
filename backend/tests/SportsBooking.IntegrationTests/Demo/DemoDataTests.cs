using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Bookings;
using SportsBooking.Domain.Scheduling;
using SportsBooking.Infrastructure;
using SportsBooking.Infrastructure.Demo;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Demo;

/// <summary>T05: the demo data. Each seed runs on its own empty database, with a fixed clock.</summary>
public sealed class DemoDataTests(ApiFactory factory)
{
    // Tuesday 10 November 2026, 11:00 UTC. The data's "now" is the start of that day in Lisbon: 2026-11-10T00:00Z.
    private static readonly DateTimeOffset ClockStart = new(2026, 11, 10, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Anchor = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> NewApp(DateTimeOffset? now = null) =>
        factory.CreateIsolatedApp($"demo_{Guid.NewGuid():N}")
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(new FakeTimeProvider(now ?? ClockStart))));

    private static async Task<DemoSeedSummary> SeedAsync(WebApplicationFactory<Program> app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync(Ct);
    }

    /// <summary>The seed run once on its own database, shared by the tests that only read it.</summary>
    private static readonly SemaphoreSlim SharedGate = new(1, 1);

    private static (WebApplicationFactory<Program> App, DemoSeedSummary Summary, Snapshot Data)? _shared;

    private async Task<(WebApplicationFactory<Program> App, DemoSeedSummary Summary, Snapshot Data)> SharedAsync()
    {
        await SharedGate.WaitAsync(Ct);
        try
        {
            if (_shared is null)
            {
                var app = NewApp();
                var summary = await SeedAsync(app);
                _shared = (app, summary, await SnapshotAsync(app));
            }

            return _shared.Value;
        }
        finally
        {
            SharedGate.Release();
        }
    }

    /// <summary>
    /// The same seed on a clock that starts at the real time, for the tests that sign in: an access token is checked
    /// against the system clock, so a session made on a fake clock years away would be refused.
    /// </summary>
    private static (WebApplicationFactory<Program> App, Snapshot Data, DateTime Anchor)? _live;

    private async Task<(WebApplicationFactory<Program> App, Snapshot Data, DateTime Anchor)> LiveAsync()
    {
        await SharedGate.WaitAsync(Ct);
        try
        {
            if (_live is null)
            {
                var now = DateTimeOffset.UtcNow;
                var app = NewApp(now);
                await SeedAsync(app);
                var anchor = ClubTime.ToUtc(ClubTime.Today(app.Services.GetRequiredService<TimeProvider>(), Lisbon), TimeOnly.MinValue, Lisbon);
                _live = (app, await SnapshotAsync(app), anchor);
            }

            return _live.Value;
        }
        finally
        {
            SharedGate.Release();
        }
    }

    // ---- What is in the database

    private sealed record UserRow(Guid Id, string Email, string Name, DateTime RegisteredAt, string? Phone, string Profile);

    private sealed record ClubRow(Guid Id, string Name, string Slug, string Locality, string Address, string Phone, string ContactEmail, int WindowHours, Guid OwnerId);

    private sealed record PitchRow(Guid Id, Guid ClubId, string Name, string Sport, decimal Price, int SlotMinutes);

    private sealed record BlockRow(Guid PitchId, DateTime StartsAt, DateTime EndsAt, string? Reason);

    private sealed record BookingRow(
        Guid Id, Guid PitchId, Guid? PlayerId, string? CustomerName, string? CustomerPhone, DateTime StartsAt, DateTime EndsAt, decimal Price, string Status,
        DateTime CreatedAt, DateTime? CancelledAt, string? Reason);

    private sealed record Snapshot(List<UserRow> Users, List<ClubRow> Clubs, List<PitchRow> Pitches, List<BlockRow> Blocks, List<BookingRow> Bookings, int OutboxMessages)
    {
        public Dictionary<Guid, PitchRow> PitchById => Pitches.ToDictionary(pitch => pitch.Id);

        public Dictionary<Guid, ClubRow> ClubById => Clubs.ToDictionary(club => club.Id);

        public Dictionary<Guid, UserRow> UserById => Users.ToDictionary(user => user.Id);
    }

    private static async Task<Snapshot> SnapshotAsync(WebApplicationFactory<Program> app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = await (
                from user in db.Users.AsNoTracking()
                join userRole in db.UserRoles on user.Id equals userRole.UserId
                join role in db.Roles on userRole.RoleId equals role.Id
                select new UserRow(user.Id, user.Email!, user.Name, user.RegisteredAt, user.PhoneNumber, role.Name!))
            .ToListAsync(Ct);
        var clubs = (await db.Clubs.AsNoTracking().ToListAsync(Ct))
            .Select(club => new ClubRow(club.Id, club.Name, club.Slug, club.Locality, club.Address, club.Phone, club.ContactEmail, club.CancellationWindowHours, club.OwnerId)).ToList();
        var pitches = (await db.Pitches.AsNoTracking().ToListAsync(Ct))
            .Select(pitch => new PitchRow(pitch.Id, pitch.ClubId, pitch.Name, SportsBooking.Domain.Pitches.Sports.ToCode(pitch.Sport), pitch.PricePerSlot, pitch.SlotDurationMinutes)).ToList();
        var blocks = (await db.Blocks.AsNoTracking().ToListAsync(Ct)).Select(block => new BlockRow(block.PitchId, block.StartsAt, block.EndsAt, block.Reason)).ToList();
        var bookings = (await db.Bookings.AsNoTracking().ToListAsync(Ct))
            .Select(booking => new BookingRow(
                booking.Id, booking.PitchId, booking.PlayerId, booking.CustomerName, booking.CustomerPhone, booking.StartsAt, booking.EndsAt, booking.Price,
                booking.Status.ToString(), booking.CreatedAt, booking.CancelledAt, booking.CancellationReason)).ToList();
        return new Snapshot(users, clubs, pitches, blocks, bookings, await db.OutboxMessages.CountAsync(Ct));
    }

    /// <summary>Everything but the generated ids and password hashes, in a fixed order: what two runs must have in common.</summary>
    private static List<string> Canonical(Snapshot data)
    {
        var pitchName = data.PitchById.ToDictionary(pair => pair.Key, pair => $"{data.ClubById[pair.Value.ClubId].Slug}/{pair.Value.Name}");
        var userEmail = data.UserById.ToDictionary(pair => pair.Key, pair => pair.Value.Email);
        string Time(DateTime? value) => value?.ToString("O", CultureInfo.InvariantCulture) ?? "-";
        var lines = new List<string>();
        lines.AddRange(data.Users.Select(user => $"user|{user.Email}|{user.Name}|{user.Profile}|{Time(user.RegisteredAt)}|{user.Phone}"));
        lines.AddRange(data.Clubs.Select(club => $"club|{club.Slug}|{club.Name}|{club.Locality}|{club.Address}|{club.Phone}|{club.ContactEmail}|{club.WindowHours}|{userEmail[club.OwnerId]}"));
        lines.AddRange(data.Pitches.Select(pitch => $"pitch|{pitchName[pitch.Id]}|{pitch.Sport}|{pitch.Price}|{pitch.SlotMinutes}"));
        lines.AddRange(data.Blocks.Select(block => $"block|{pitchName[block.PitchId]}|{Time(block.StartsAt)}|{Time(block.EndsAt)}|{block.Reason}"));
        lines.AddRange(data.Bookings.Select(booking =>
            $"booking|{pitchName[booking.PitchId]}|{Time(booking.StartsAt)}|{Time(booking.EndsAt)}|{booking.Price}|{booking.Status}|" +
            $"{(booking.PlayerId is { } player ? userEmail[player] : $"{booking.CustomerName}/{booking.CustomerPhone}")}|{Time(booking.CreatedAt)}|{Time(booking.CancelledAt)}|{booking.Reason}"));
        return lines.Order(StringComparer.Ordinal).ToList();
    }

    // ---- Seeding an empty database

    [Fact]
    public async Task Seeding_an_empty_database_creates_the_expected_accounts_clubs_pitches_and_blocks()
    {
        var (_, summary, data) = await SharedAsync();

        summary.Owners.ShouldBe(5);
        summary.Players.ShouldBe(60);
        summary.Clubs.ShouldBe(10);
        summary.Pitches.ShouldBe(43);
        summary.Blocks.ShouldBe(5);
        data.Users.Count(user => user.Profile == Profiles.Owner).ShouldBe(5);
        data.Users.Count(user => user.Profile == Profiles.Player).ShouldBe(60);
        data.Users.Count.ShouldBe(65); // one profile each
        data.Clubs.Count.ShouldBe(10);
        data.Pitches.Count.ShouldBe(43);
        data.Blocks.Count.ShouldBe(5);
        data.Clubs.GroupBy(club => club.OwnerId).Select(group => group.Count()).ShouldBe([2, 2, 2, 2, 2]);
    }

    [Fact]
    public async Task The_clubs_are_around_porto_and_lisboa_with_all_four_sports_and_realistic_prices_and_slot_lengths()
    {
        var (_, _, data) = await SharedAsync();

        data.Clubs.Select(club => club.Locality).Distinct().Order().ShouldBe(["Cascais", "Lisboa", "Maia", "Matosinhos", "Oeiras", "Porto", "Vila Nova de Gaia"]);
        data.Pitches.Select(pitch => pitch.Sport).Distinct().Order().ShouldBe(["football7", "futsal", "padel", "tennis"]);
        data.Pitches.Select(pitch => pitch.SlotMinutes).Distinct().Order().ShouldBe([30, 60, 90]);
        data.Pitches.ShouldAllBe(pitch => pitch.Price >= 7m && pitch.Price <= 70m);
        data.Pitches.Where(pitch => pitch.Sport == "tennis").ShouldAllBe(pitch => pitch.Price <= 16m);
        data.Pitches.Where(pitch => pitch.Sport == "football7").ShouldAllBe(pitch => pitch.Price >= 55m);
        data.Clubs.Select(club => club.WindowHours).Distinct().Order().ShouldBe([12, 24, 48]);
        data.Clubs.Select(club => club.Slug).Distinct().Count().ShouldBe(10);
    }

    [Fact]
    public async Task There_are_a_few_blocks_two_over_and_three_to_come_with_one_future_maintenance_block()
    {
        var (_, _, data) = await SharedAsync();

        data.Blocks.Count(block => block.EndsAt <= Anchor).ShouldBe(2);
        data.Blocks.Count(block => block.StartsAt >= Anchor).ShouldBe(3);
        data.Blocks.ShouldContain(block => block.Reason == "Manutenção da superfície" && block.StartsAt > Anchor);
        data.Blocks.ShouldAllBe(block => block.EndsAt > block.StartsAt);
    }

    [Fact]
    public async Task The_players_registered_over_the_last_90_days_and_the_owners_before_that()
    {
        var (_, _, data) = await SharedAsync();

        var players = data.Users.Where(user => user.Profile == Profiles.Player).OrderBy(user => user.RegisteredAt).ToList();
        var owners = data.Users.Where(user => user.Profile == Profiles.Owner).ToList();
        players.First().RegisteredAt.ShouldBeLessThan(Anchor.AddDays(-88));
        players.First().RegisteredAt.ShouldBeGreaterThan(Anchor.AddDays(-92));
        players.Last().RegisteredAt.ShouldBeLessThan(Anchor);
        players.Last().RegisteredAt.ShouldBeGreaterThan(Anchor.AddDays(-4));
        players.Select(player => player.RegisteredAt.Date).Distinct().Count().ShouldBeGreaterThan(50); // spread, not clumped
        players.Count(player => player.RegisteredAt < Anchor.AddDays(-80)).ShouldBeGreaterThanOrEqualTo(6);
        // The owners signed up over the last 90 days too, deterministically and apart, so the registrations chart shows them.
        owners.Count.ShouldBe(5);
        owners.ShouldAllBe(owner => owner.RegisteredAt > Anchor.AddDays(-90) && owner.RegisteredAt < Anchor.AddDays(-30));
        owners.Select(owner => owner.RegisteredAt.Date).Distinct().Count().ShouldBe(5);
        owners.Min(owner => owner.RegisteredAt).ShouldBeLessThan(Anchor.AddDays(-85));
        owners.Max(owner => owner.RegisteredAt).ShouldBeGreaterThan(Anchor.AddDays(-45));
        data.Clubs.Count.ShouldBe(10);
    }

    [Fact]
    public async Task No_booking_is_made_before_its_clubs_owner_registered_and_each_club_opens_after_its_owner()
    {
        var (_, _, data) = await SharedAsync();

        var users = data.UserById;
        var checkedBookings = 0;
        foreach (var booking in data.Bookings)
        {
            var owner = users[data.ClubById[data.PitchById[booking.PitchId].ClubId].OwnerId];
            booking.CreatedAt.ShouldBeGreaterThanOrEqualTo(owner.RegisteredAt, $"a booking of {owner.Email} was made before the owner signed up");
            checkedBookings++;
        }

        checkedBookings.ShouldBeGreaterThan(5000);
        // The clubs of the owner who signed up last have a shorter history than the clubs of the first.
        var perOwner = data.Bookings.GroupBy(booking => data.ClubById[data.PitchById[booking.PitchId].ClubId].OwnerId).ToDictionary(group => group.Key, group => group.Min(booking => booking.StartsAt));
        var owners = data.Users.Where(user => user.Profile == Profiles.Owner).OrderBy(user => user.RegisteredAt).ToList();
        perOwner[owners[^1].Id].ShouldBeGreaterThan(owners[^1].RegisteredAt);
    }

    [Fact]
    public async Task Every_address_is_on_the_reserved_domain_and_the_one_demo_password_works()
    {
        var (app, _, data) = await SharedAsync();

        data.Users.ShouldAllBe(user => user.Email.EndsWith("@example.test", StringComparison.Ordinal));
        data.Clubs.ShouldAllBe(club => club.ContactEmail.EndsWith("@example.test", StringComparison.Ordinal));
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        foreach (var email in new[] { "dono01@example.test", "dono05@example.test", "jogador01@example.test", "jogador60@example.test" })
        {
            var user = await users.FindByEmailAsync(email);
            user.ShouldNotBeNull();
            (await users.CheckPasswordAsync(user, DemoDataSeeder.DemoPassword)).ShouldBeTrue(email);
            (await users.CheckPasswordAsync(user, "outra-palavra-passe")).ShouldBeFalse(email);
        }
    }

    // ---- The bookings

    [Fact]
    public async Task The_bookings_cover_the_last_60_days_and_the_next_14_in_every_state_with_customer_bookings_among_them()
    {
        var (_, summary, data) = await SharedAsync();

        summary.Bookings.ShouldBe(data.Bookings.Count);
        data.Bookings.Count.ShouldBeInRange(4000, 14000);
        data.Bookings.Min(booking => booking.StartsAt).ShouldBeGreaterThanOrEqualTo(Anchor.AddDays(-61));
        data.Bookings.Min(booking => booking.StartsAt).ShouldBeLessThan(Anchor.AddDays(-58));
        data.Bookings.Max(booking => booking.StartsAt).ShouldBeLessThan(Anchor.AddDays(15));
        data.Bookings.Max(booking => booking.StartsAt).ShouldBeGreaterThan(Anchor.AddDays(12));

        var past = data.Bookings.Where(booking => booking.EndsAt <= Anchor).ToList();
        var future = data.Bookings.Where(booking => booking.StartsAt >= Anchor).ToList();
        past.Count.ShouldBeGreaterThan(future.Count);
        past.Select(booking => booking.Status).Distinct().Order().ShouldBe(["CancelledByClub", "CancelledByPlayer", "Confirmed", "NoShow"]);
        future.Select(booking => booking.Status).Distinct().Order().ShouldBe(["CancelledByClub", "CancelledByPlayer", "Confirmed"]);
        data.Bookings.Count(booking => booking.Status == "NoShow").ShouldBeGreaterThan(30);
        data.Bookings.Count(booking => booking.Status == "CancelledByClub").ShouldBeGreaterThan(30);
        data.Bookings.Count(booking => booking.Status == "CancelledByPlayer").ShouldBeGreaterThan(100);
        var customers = data.Bookings.Count(booking => booking.PlayerId is null);
        customers.ShouldBe(summary.CustomerBookings);
        ((double)customers / data.Bookings.Count).ShouldBeInRange(0.03, 0.10);
        summary.Confirmed.ShouldBe(data.Bookings.Count(booking => booking.Status == "Confirmed"));
        summary.CancelledByPlayer.ShouldBe(data.Bookings.Count(booking => booking.Status == "CancelledByPlayer"));
        summary.CancelledByClub.ShouldBe(data.Bookings.Count(booking => booking.Status == "CancelledByClub"));
        summary.NoShows.ShouldBe(data.Bookings.Count(booking => booking.Status == "NoShow"));
        // Mostly attended, as real life is.
        ((double)past.Count(booking => booking.Status is "Confirmed") / past.Count).ShouldBeInRange(0.65, 0.92);
    }

    [Fact]
    public async Task The_bookings_are_busier_in_the_evening_and_at_weekends()
    {
        var (_, _, data) = await SharedAsync();
        var confirmedOrNoShow = data.Bookings.Where(booking => booking.Status is "Confirmed" or "NoShow").ToList();
        var local = confirmedOrNoShow.Select(booking => ClubTime.ToLocal(booking.StartsAt, Lisbon)).ToList();

        var evening = local.Count(time => time.Hour is >= 18 and < 22);
        var morning = local.Count(time => time.Hour is >= 9 and < 13);
        evening.ShouldBeGreaterThan(morning * 2);
        // Per day: a weekend day has more bookings than a weekday.
        var days = local.GroupBy(time => DateOnly.FromDateTime(time)).ToDictionary(group => group.Key, group => group.Count());
        var weekendAverage = days.Where(day => day.Key.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday).Average(day => day.Value);
        var weekdayAverage = days.Where(day => day.Key.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).Average(day => day.Value);
        weekendAverage.ShouldBeGreaterThan(weekdayAverage * 1.2);
        // And the platform grows: the last 30 days of history have more bookings than the 30 before them.
        var recent = confirmedOrNoShow.Count(booking => booking.StartsAt >= Anchor.AddDays(-30) && booking.StartsAt < Anchor);
        var earlier = confirmedOrNoShow.Count(booking => booking.StartsAt >= Anchor.AddDays(-60) && booking.StartsAt < Anchor.AddDays(-30));
        recent.ShouldBeGreaterThan(earlier);
    }

    [Fact]
    public async Task No_two_confirmed_bookings_of_a_pitch_overlap_and_the_database_accepted_them_all()
    {
        var (app, _, data) = await SharedAsync();

        foreach (var group in data.Bookings.Where(booking => booking.Status == "Confirmed").GroupBy(booking => booking.PitchId))
        {
            var ordered = group.OrderBy(booking => booking.StartsAt).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                ordered[i].StartsAt.ShouldBeGreaterThanOrEqualTo(ordered[i - 1].EndsAt, $"pitch {group.Key}");
            }
        }

        // The same, asked of the database itself.
        await using var scope = app.Services.CreateAsyncScope();
        var overlapping = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .SqlQuery<int>($"""
                            SELECT count(*)::int AS "Value" FROM bookings a JOIN bookings b
                              ON a.pitch_id = b.pitch_id AND a.id < b.id AND a.status = 'Confirmed' AND b.status = 'Confirmed'
                             AND a.starts_at < b.ends_at AND b.starts_at < a.ends_at
                            """)
            .SingleAsync(Ct);
        overlapping.ShouldBe(0);
    }

    [Fact]
    public async Task Every_booking_is_on_a_slot_its_pitch_generates_at_the_pitchs_price_and_clear_of_the_blocks()
    {
        var (app, _, data) = await SharedAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var pitches = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Pitches.AsNoTracking().ToDictionaryAsync(pitch => pitch.Id, Ct);

        foreach (var booking in data.Bookings)
        {
            var pitch = pitches[booking.PitchId];
            var localDate = DateOnly.FromDateTime(ClubTime.ToLocal(booking.StartsAt, Lisbon));
            var slot = pitch.SlotsOn(localDate, Lisbon).SingleOrDefault(candidate => candidate.StartsAtUtc == booking.StartsAt);
            slot.ShouldNotBeNull($"{pitch.Name} {booking.StartsAt:O}");
            slot.EndsAtUtc.ShouldBe(booking.EndsAt);
            booking.Price.ShouldBe(pitch.PricePerSlot);
            data.Blocks.Where(block => block.PitchId == booking.PitchId).ShouldAllBe(block => !(block.StartsAt < booking.EndsAt && booking.StartsAt < block.EndsAt));
        }
    }

    [Fact]
    public async Task Every_booking_was_created_before_it_starts_after_its_player_registered_and_inside_the_booking_window()
    {
        var (_, _, data) = await SharedAsync();
        var users = data.UserById;

        foreach (var booking in data.Bookings)
        {
            booking.CreatedAt.ShouldBeLessThan(booking.StartsAt);
            booking.CreatedAt.ShouldBeLessThanOrEqualTo(Anchor); // everything was made before the data's "now"
            if (booking.PlayerId is { } playerId)
            {
                users[playerId].Profile.ShouldBe(Profiles.Player);
                booking.CreatedAt.ShouldBeGreaterThanOrEqualTo(users[playerId].RegisteredAt);
            }

            // Not more than 30 days ahead of the day it was made (US18, in Lisbon dates).
            var madeOn = DateOnly.FromDateTime(ClubTime.ToLocal(booking.CreatedAt, Lisbon));
            var startsOn = DateOnly.FromDateTime(ClubTime.ToLocal(booking.StartsAt, Lisbon));
            startsOn.DayNumber.ShouldBeLessThanOrEqualTo(madeOn.DayNumber + 30);
        }

        data.Bookings.Where(booking => booking.PlayerId is null).ShouldAllBe(booking => booking.CustomerName != null && booking.CustomerPhone != null && booking.CustomerPhone.Length == 9);
    }

    [Fact]
    public async Task Cancellations_have_a_time_and_a_reason_when_the_club_cancelled_and_follow_the_rules_of_the_entities()
    {
        var (_, _, data) = await SharedAsync();
        var windows = data.PitchById.ToDictionary(pair => pair.Key, pair => TimeSpan.FromHours(data.ClubById[pair.Value.ClubId].WindowHours));

        foreach (var booking in data.Bookings.Where(booking => booking.Status is "CancelledByPlayer" or "CancelledByClub"))
        {
            booking.CancelledAt.ShouldNotBeNull();
            booking.CancelledAt!.Value.ShouldBeGreaterThan(booking.CreatedAt);
            booking.CancelledAt.Value.ShouldBeLessThanOrEqualTo(Anchor);
            if (booking.Status == "CancelledByClub")
            {
                booking.Reason.ShouldNotBeNullOrWhiteSpace();
                booking.CancelledAt.Value.ShouldBeLessThan(booking.StartsAt); // the club cancels before it starts
            }
            else
            {
                booking.Reason.ShouldBeNull();
                booking.CancelledAt.Value.ShouldBeLessThan(booking.StartsAt - windows[booking.PitchId]); // inside the window, as the app requires
                booking.PlayerId.ShouldNotBeNull(); // a customer cannot cancel
            }
        }

        data.Bookings.Where(booking => booking.Status is "Confirmed" or "NoShow").ShouldAllBe(booking => booking.CancelledAt == null && booking.Reason == null);
        data.Bookings.Where(booking => booking.Status == "NoShow").ShouldAllBe(booking => booking.EndsAt <= Anchor);
    }

    [Fact]
    public async Task Some_cancelled_slots_were_booked_again_by_somebody_else()
    {
        var (_, _, data) = await SharedAsync();

        var cancelled = data.Bookings.Where(booking => booking.Status is "CancelledByPlayer" or "CancelledByClub").ToList();
        var retaken = cancelled.Count(booking => data.Bookings.Any(other => other.Status is "Confirmed" or "NoShow" && other.PitchId == booking.PitchId && other.StartsAt == booking.StartsAt));
        retaken.ShouldBeGreaterThan(cancelled.Count / 5);
    }

    [Fact]
    public async Task Seeding_queues_no_email()
    {
        var (_, _, data) = await SharedAsync();

        data.OutboxMessages.ShouldBe(0);
    }

    // ---- Deterministic

    [Fact]
    public async Task Seeding_two_empty_databases_gives_the_same_data()
    {
        var first = NewApp();
        var second = NewApp();

        var firstSummary = await SeedAsync(first);
        var secondSummary = await SeedAsync(second);

        secondSummary.ShouldBe(firstSummary);
        var firstData = Canonical(await SnapshotAsync(first));
        var secondData = Canonical(await SnapshotAsync(second));
        secondData.Count.ShouldBe(firstData.Count);
        secondData.ShouldBe(firstData);
        firstData.Count.ShouldBeGreaterThan(4000);
    }

    [Fact]
    public async Task Another_date_gives_other_data_anchored_to_that_date()
    {
        var other = NewApp(ClockStart.AddDays(10));
        await SeedAsync(other);
        var (_, _, shared) = await SharedAsync();

        var data = await SnapshotAsync(other);

        data.Bookings.Min(booking => booking.StartsAt).ShouldBeGreaterThan(shared.Bookings.Min(booking => booking.StartsAt).AddDays(9));
        Canonical(data).ShouldNotBe(Canonical(shared));
    }

    // ---- Refusals

    private static async Task<Exception> SeedRefusedAsync(WebApplicationFactory<Program> app, string environmentName = "Development")
    {
        await using var scope = app.Services.CreateAsyncScope();
        var seeder = new DemoDataSeeder(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),
            scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>(),
            new StubEnvironment(environmentName),
            Options.Create(new DemoDataOptions()),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            NullLogger<DemoDataSeeder>.Instance);
        return await Should.ThrowAsync<DemoSeedRefusedException>(() => seeder.SeedAsync(Ct));
    }

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "SportsBooking.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private WebApplicationFactory<Program> ProductionApp(string? allowance) =>
        NewApp(DateTimeOffset.UtcNow)
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                if (allowance is not null)
                {
                    builder.UseSetting("DemoData:AllowInProduction", allowance);
                }
            });

    private static async Task<(int ExitCode, string Error)> RunCommandAsync(WebApplicationFactory<Program> app)
    {
        await app.Services.MigrateDatabaseAsync(Ct);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await DemoDataCommand.RunAsync(app.Services, output, error, Ct);
        return (exitCode, error.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task The_command_is_refused_in_production_without_the_explicit_allowance(string? allowance)
    {
        await using var app = ProductionApp(allowance);

        var (exitCode, error) = await RunCommandAsync(app);

        exitCode.ShouldBe(1);
        error.ShouldContain("DemoData:AllowInProduction");
        (await SnapshotAsync(app)).Users.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_command_runs_in_production_with_the_explicit_allowance()
    {
        await using var app = ProductionApp("true");

        var (exitCode, error) = await RunCommandAsync(app);

        error.ShouldBeEmpty();
        exitCode.ShouldBe(0);
        (await SnapshotAsync(app)).Users.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task It_refuses_in_the_production_environment_and_creates_nothing()
    {
        var app = NewApp();

        var refused = await SeedRefusedAsync(app, "Production");

        refused.Message.ShouldBe("Demo data is not created in the Production environment unless DemoData:AllowInProduction is true.");
        var data = await SnapshotAsync(app);
        data.Users.ShouldBeEmpty();
        data.Clubs.ShouldBeEmpty();
        data.Bookings.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Player")]
    public async Task It_refuses_when_an_owner_or_a_player_already_exists_and_adds_nothing(string profile)
    {
        var app = NewApp();
        await app.CreateAccountAsync(profile, password: null, email: "ja-existe@exemplo.test");

        var refused = await SeedRefusedAsync(app);

        refused.Message.ShouldBe("The database already has owners or players: demo data is only created in an empty one.");
        var data = await SnapshotAsync(app);
        data.Users.Count.ShouldBe(1);
        data.Clubs.ShouldBeEmpty();
        data.Pitches.ShouldBeEmpty();
        data.Bookings.ShouldBeEmpty();
    }

    [Fact]
    public async Task It_refuses_the_second_time_and_the_first_run_is_left_alone()
    {
        var app = NewApp();
        var summary = await SeedAsync(app);
        var before = Canonical(await SnapshotAsync(app));

        await SeedRefusedAsync(app);

        Canonical(await SnapshotAsync(app)).ShouldBe(before);
        summary.Bookings.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task An_admin_alone_does_not_stop_it()
    {
        var app = NewApp();
        await app.CreateAccountAsync(Profiles.Admin, password: null, email: "admin@exemplo.test");

        var summary = await SeedAsync(app);

        summary.Owners.ShouldBe(5);
        (await SnapshotAsync(app)).Users.Count.ShouldBe(66);
    }

    // ---- The command

    [Theory]
    [InlineData("seed-demo", true)]
    [InlineData("seed-demo", false)]
    public void Only_the_exact_argument_asks_for_it(string argument, bool alone)
    {
        DemoDataCommand.IsRequested(alone ? [argument] : [argument, "--urls=http://localhost:5080"]).ShouldBe(alone);
    }

    [Theory]
    [InlineData]
    [InlineData("seed")]
    [InlineData("SEED-DEMO")]
    [InlineData("seed-demo ")]
    [InlineData("--seed-demo")]
    public void Nothing_else_asks_for_it(params string[] args)
    {
        DemoDataCommand.IsRequested(args).ShouldBeFalse();
    }

    [Fact]
    public async Task The_command_seeds_exits_0_and_prints_counts_but_no_password_and_a_second_run_exits_1()
    {
        var app = NewApp();
        var output = new StringWriter();
        var error = new StringWriter();

        var first = await DemoDataCommand.RunAsync(app.Services, output, error, Ct);
        var second = await DemoDataCommand.RunAsync(app.Services, output, error, Ct);

        first.ShouldBe(0);
        second.ShouldBe(1);
        output.ToString().ShouldContain("5 owners, 60 players, 10 clubs, 43 pitches, 5 blocks");
        output.ToString().ShouldContain("dono01..dono05 and jogador01..jogador60 @example.test");
        output.ToString().ShouldNotContain(DemoDataSeeder.DemoPassword);
        error.ToString().ShouldBe("Demo data not created: The database already has owners or players: demo data is only created in an empty one." + Environment.NewLine);
    }

    // ---- Through the app

    [Fact]
    public async Task The_search_finds_clubs_in_several_localities_with_free_slots()
    {
        var (app, _, _) = await SharedAsync();
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/api/clubs?date=2026-11-11", Ct);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var clubs = page.GetProperty("items").EnumerateArray().ToList();
        clubs.Count.ShouldBe(10);
        clubs.Select(club => club.GetProperty("locality").GetString()).Distinct().Count().ShouldBe(7);
        clubs.Count(club => club.GetProperty("hasFreeSlots").GetBoolean()).ShouldBe(10);
        clubs.SelectMany(club => club.GetProperty("sports").EnumerateArray().Select(sport => sport.GetString())).Distinct().Count().ShouldBe(4);
        using var localities = await client.GetAsync("/api/localities", Ct);
        (await localities.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(7);
    }

    [Fact]
    public async Task A_demo_owner_signs_in_and_sees_a_busy_calendar_with_customer_bookings()
    {
        var (app, data, anchor) = await LiveAsync();
        var owner = data.Users.Single(user => user.Email == "dono01@example.test");
        var club = data.Clubs.First(candidate => candidate.OwnerId == owner.Id);
        using var client = app.CreateBrowserClient();
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email = owner.Email, password = DemoDataSeeder.DemoPassword }, Ct);
        login.StatusCode.ShouldBe(HttpStatusCode.OK, await login.Content.ReadAsStringAsync(Ct));

        // A week of the history, ending the day before yesterday: every pitch of the club has bookings in it.
        var to = DateOnly.FromDateTime(ClubTime.ToLocal(anchor, Lisbon)).AddDays(-2);
        var from = to.AddDays(-6);
        using var calendarResponse = await client.GetAsync($"/api/manage/clubs/{club.Id}/calendar?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&includeCancelled=true", Ct);
        var text = await calendarResponse.Content.ReadAsStringAsync(Ct);
        calendarResponse.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        var calendar = JsonDocument.Parse(text).RootElement;

        var pitches = data.Pitches.Count(pitch => pitch.ClubId == club.Id);
        calendar.GetProperty("pitches").GetArrayLength().ShouldBe(pitches);
        var bookings = calendar.GetProperty("bookings").EnumerateArray().ToList();
        bookings.Count.ShouldBeGreaterThan(pitches * 6);
        bookings.Select(booking => booking.GetProperty("bookerName").GetString()).Distinct().Count().ShouldBeGreaterThan(10);
        bookings.Select(booking => booking.GetProperty("status").GetString()).Distinct().Count().ShouldBeGreaterThanOrEqualTo(2);
        bookings.Select(booking => booking.GetProperty("pitchId").GetGuid()).Distinct().Count().ShouldBe(pitches);
    }

    [Fact]
    public async Task A_demo_player_signs_in_and_sees_past_and_upcoming_bookings()
    {
        var (app, data, anchor) = await LiveAsync();
        // A keen player: confirmed bookings both behind and ahead.
        var player = data.Bookings
            .Where(booking => booking.PlayerId != null && booking.Status == "Confirmed")
            .GroupBy(booking => booking.PlayerId!.Value)
            .Where(group => group.Any(booking => booking.StartsAt >= anchor) && group.Any(booking => booking.EndsAt <= anchor))
            .OrderByDescending(group => group.Count())
            .First();
        var email = data.UserById[player.Key].Email;
        using var client = app.CreateBrowserClient();
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DemoDataSeeder.DemoPassword }, Ct);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);

        var upcoming = await (await client.GetAsync("/api/me/bookings?scope=upcoming", Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var past = await (await client.GetAsync("/api/me/bookings?scope=past", Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);

        var now = app.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var mine = data.Bookings.Where(booking => booking.PlayerId == player.Key).ToList();
        upcoming.GetProperty("totalCount").GetInt32().ShouldBeGreaterThan(0);
        past.GetProperty("totalCount").GetInt32().ShouldBeGreaterThan(0);
        upcoming.GetProperty("totalCount").GetInt32().ShouldBe(mine.Count(booking => booking.Status == "Confirmed" && booking.EndsAt > now));
        past.GetProperty("totalCount").GetInt32().ShouldBe(mine.Count(booking => booking.Status != "Confirmed" || booking.EndsAt <= now));
    }

    [Fact]
    public async Task The_customer_bookings_are_in_no_players_list()
    {
        var (app, data, _) = await LiveAsync();
        var playerBookings = data.Bookings.Count(booking => booking.PlayerId != null);
        var listed = 0;

        foreach (var player in data.Users.Where(user => user.Profile == Profiles.Player).Take(5))
        {
            using var client = app.CreateBrowserClient();
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { email = player.Email, password = DemoDataSeeder.DemoPassword }, Ct);
            login.StatusCode.ShouldBe(HttpStatusCode.OK);
            foreach (var scope in new[] { "upcoming", "past" })
            {
                var page = await (await client.GetAsync($"/api/me/bookings?scope={scope}&page=1", Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
                listed += page.GetProperty("totalCount").GetInt32();
            }
        }

        listed.ShouldBe(data.Bookings.Count(booking => booking.PlayerId != null && data.Users.Where(user => user.Profile == Profiles.Player).Take(5).Any(user => user.Id == booking.PlayerId)));
        playerBookings.ShouldBeLessThan(data.Bookings.Count);
    }
}
