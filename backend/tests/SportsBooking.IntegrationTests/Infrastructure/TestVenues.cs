using System.Globalization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Bookings;
using SportsBooking.Domain.Clubs;
using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.IntegrationTests.Infrastructure;

public sealed record TestClub(Guid Id, Guid OwnerId, string Slug, string Name, string Locality);

public sealed record TestPitch(Guid Id, Guid ClubId);

/// <summary>
/// Builds owners, clubs, pitches, blocks and bookings directly in the database through the domain model, for tests
/// that read them back through the API. Names and slugs are unique per call unless a test passes its own, because
/// every test of the assembly shares one database.
/// </summary>
public static class TestVenues
{
    /// <summary>When test data was "created"; the tests' own clocks are separate.</summary>
    private static readonly DateTime CreatedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DayOfWeek[] Week =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    /// <summary>Monday 09:00-13:00, the opening hours most tests use (4 May 2026 is a Monday).</summary>
    public static readonly (DayOfWeek Day, string Opens, string Closes)[] MondayMorning = [(DayOfWeek.Monday, "09:00", "13:00")];

    public static async Task<Guid> CreateOwnerAsync(this WebApplicationFactory<Program> app) =>
        (await app.CreateAccountAsync(Profiles.Owner, password: null, name: "Dono de Teste")).Id;

    public static async Task<TestClub> CreateClubAsync(
        this WebApplicationFactory<Program> app,
        Guid? ownerId = null,
        string? name = null,
        string locality = "Porto")
    {
        var owner = ownerId ?? await app.CreateOwnerAsync();
        var clubName = name ?? $"Clube {Guid.NewGuid():N}";
        // The real slug may get a numeric suffix; here an 8-character random one keeps it unique and within the limit.
        var slug = $"{ClubSlug.FromName(clubName)}-{Guid.NewGuid().ToString("N")[..8]}";
        var club = Club.Create(
            owner,
            slug,
            new ClubDetails(clubName, "Rua Principal 1", locality, "912345678", "geral@clube.test"),
            ClubRules.DefaultCancellationWindowHours,
            CreatedAt);

        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Clubs.Add(club);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new TestClub(club.Id, owner, club.Slug, club.Name, club.Locality);
    }

    /// <summary>A pitch with the given slot length and opening hours (default: Monday 09:00-13:00); other days are closed.</summary>
    public static async Task<TestPitch> CreatePitchAsync(
        this WebApplicationFactory<Program> app,
        Guid clubId,
        string name = "Campo 1",
        Sport sport = Sport.Padel,
        decimal price = 20m,
        int slotMinutes = 60,
        params (DayOfWeek Day, string Opens, string Closes)[] open)
    {
        var pitch = Pitch.Create(clubId, new PitchDetails(name, sport, price), CreatedAt);
        pitch.ChangeSchedule(SlotDuration.FromMinutes(slotMinutes), Hours(open.Length == 0 ? MondayMorning : open), CreatedAt);

        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Pitches.Add(pitch);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new TestPitch(pitch.Id, clubId);
    }

    /// <summary>Replaces a pitch's slot length and week, as the owner does in US08 (existing bookings are untouched).</summary>
    public static async Task ChangeScheduleAsync(
        this WebApplicationFactory<Program> app,
        Guid pitchId,
        int slotMinutes,
        params (DayOfWeek Day, string Opens, string Closes)[] open)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pitch = await dbContext.Pitches.SingleAsync(candidate => candidate.Id == pitchId, TestContext.Current.CancellationToken);
        pitch.ChangeSchedule(SlotDuration.FromMinutes(slotMinutes), Hours(open), CreatedAt);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>US07: a new price per slot; bookings already made keep theirs.</summary>
    public static async Task ChangePriceAsync(this WebApplicationFactory<Program> app, Guid pitchId, decimal price)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pitch = await dbContext.Pitches.SingleAsync(candidate => candidate.Id == pitchId, TestContext.Current.CancellationToken);
        pitch.UpdateDetails(new PitchDetails(pitch.Name, pitch.Sport, price), CreatedAt);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public static async Task DeactivatePitchAsync(this WebApplicationFactory<Program> app, Guid pitchId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pitch = await dbContext.Pitches.SingleAsync(candidate => candidate.Id == pitchId, TestContext.Current.CancellationToken);
        pitch.Deactivate(CreatedAt);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>US29: a deactivated owner's clubs disappear from the public pages.</summary>
    public static async Task DeactivateAccountAsync(this WebApplicationFactory<Program> app, Guid userId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await dbContext.Users.SingleAsync(candidate => candidate.Id == userId, TestContext.Current.CancellationToken);
        user.Deactivate(CreatedAt);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A block with the reason "Manutenção"; returns its id.</summary>
    public static async Task<Guid> AddBlockAsync(this WebApplicationFactory<Program> app, Guid pitchId, DateTime startsAtUtc, DateTime endsAtUtc)
    {
        var block = Block.Create(pitchId, startsAtUtc, endsAtUtc, "Manutenção", CreatedAt);
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Blocks.Add(block);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return block.Id;
    }

    /// <summary>Cancelling comes with US19 and US12; until then a test sets the status the way the database stores it.</summary>
    public static async Task SetBookingStatusAsync(this WebApplicationFactory<Program> app, Guid bookingId, string status)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.ExecuteSqlAsync($"UPDATE bookings SET status = {status} WHERE id = {bookingId}", TestContext.Current.CancellationToken);
    }

    /// <summary>A confirmed booking by a new player; times need not match the pitch's grid (an older grid, US17).</summary>
    public static async Task<Booking> AddBookingAsync(
        this WebApplicationFactory<Program> app,
        Guid pitchId,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        AppUser? player = null)
    {
        var account = player ?? await app.CreateAccountAsync(Profiles.Player, password: null, name: "Jogador de Teste");
        var booking = Booking.ForPlayer(pitchId, account.Id, startsAtUtc, endsAtUtc, 20m, CreatedAt);

        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Bookings.Add(booking);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return booking;
    }

    public static DateTime Utc(string instant) =>
        DateTime.Parse(instant, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    private static Dictionary<DayOfWeek, OpeningHours?> Hours(IEnumerable<(DayOfWeek Day, string Opens, string Closes)> open)
    {
        var week = Week.ToDictionary(day => day, _ => (OpeningHours?)null);
        foreach (var (day, opens, closes) in open)
        {
            week[day] = OpeningHours.Create(TimeOnly.Parse(opens, CultureInfo.InvariantCulture), TimeOnly.Parse(closes, CultureInfo.InvariantCulture));
        }

        return week;
    }
}
