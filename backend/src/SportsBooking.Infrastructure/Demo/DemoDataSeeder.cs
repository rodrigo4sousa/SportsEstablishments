using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Bookings;
using SportsBooking.Domain.Clubs;
using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.Infrastructure.Demo;

/// <summary>The demo data cannot be created: it says why, without any data.</summary>
public sealed class DemoSeedRefusedException(string message) : Exception(message);

/// <summary>What the seed created, as counts only.</summary>
public sealed record DemoSeedSummary(
    int Owners,
    int Players,
    int Clubs,
    int Pitches,
    int Blocks,
    int Bookings,
    int CustomerBookings,
    int Confirmed,
    int CancelledByPlayer,
    int CancelledByClub,
    int NoShows);

/// <summary>
/// T05: demo data for showing the app and for the admin's numbers: owners, clubs, pitches, blocks, players and bookings
/// from the last 60 days and the next 14. It runs only from the explicit <c>seed-demo</c> command (never at startup),
/// refuses in the Production environment unless <see cref="DemoDataOptions.AllowInProduction"/> is set, and refuses if any owner or player exists (provisional), so it can never be
/// mixed with real data. Everything goes through the same entities and rules as the app (<see cref="Club.Create"/>,
/// <see cref="Pitch.ChangeSchedule"/>, <see cref="Booking.ForPlayer"/>, <see cref="Booking.CancelByPlayer"/>, …); the one
/// thing it bypasses is the service rule "no bookings in the past", by building the bookings itself. Every booking is on a
/// slot the pitch's schedule generates (<see cref="Pitch.SlotsOn"/>), at the pitch's price, clear of the blocks, so the
/// exclusion constraint accepts all of it, and is created before its start, after its player registered and after its club's owner registered.
/// The data is deterministic (a fixed random seed) and anchored to the Lisbon date: the same date gives the same names,
/// dates, prices and statuses (only the generated ids and password hashes differ). It queues no email.
/// All addresses are on the reserved domain <c>example.test</c>; every account has the same development-only password.
/// </summary>
public sealed partial class DemoDataSeeder(
    AppDbContext dbContext,
    UserManager<AppUser> users,
    IPasswordHasher<AppUser> passwordHasher,
    IHostEnvironment environment,
    IOptions<DemoDataOptions> options,
    TimeProvider timeProvider,
    ILogger<DemoDataSeeder> logger)
{
    /// <summary>The password of every demo account. For development only: it is in the README, so it must never be a real one.</summary>
    public const string DemoPassword = "demo-password-1";

    /// <summary>The domain of every demo address: reserved (RFC 2606), so no mail can ever reach anyone.</summary>
    public const string EmailDomain = "example.test";

    public const int OwnerCount = 5;

    public const int PlayerCount = 60;

    /// <summary>The fixed seed of the random numbers: the same data every time.</summary>
    public const int RandomSeed = 20260101;

    private const int PastDays = 60;
    private const int FutureDays = 14;
    private const int RegistrationDays = 90;

    private static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById(ClubTimeZones.LisbonId);

    public async Task<DemoSeedSummary> SeedAsync(CancellationToken cancellationToken)
    {
        if (environment.IsProduction())
        {
            if (!options.Value.AllowInProduction)
            {
                throw new DemoSeedRefusedException($"Demo data is not created in the Production environment unless {DemoDataOptions.AllowInProductionSetting} is true.");
            }

            LogAllowedInProduction(logger);
        }

        var hasOwnersOrPlayers = await (
                from userRole in dbContext.UserRoles
                join role in dbContext.Roles on userRole.RoleId equals role.Id
                where role.Name == Profiles.Owner || role.Name == Profiles.Player
                select userRole.UserId)
            .AnyAsync(cancellationToken);
        if (hasOwnersOrPlayers)
        {
            throw new DemoSeedRefusedException("The database already has owners or players: demo data is only created in an empty one.");
        }

        var random = new Random(RandomSeed);
        var today = ClubTime.Today(timeProvider, Lisbon);
        // The data's "now": the start of today in Lisbon. What is before it has happened; what starts from it is still to come.
        var anchor = ClubTime.ToUtc(today, TimeOnly.MinValue, Lisbon);
        var passwordHash = passwordHasher.HashPassword(new AppUser(), DemoPassword);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var owners = await CreateOwnersAsync(anchor, passwordHash, cancellationToken);
        var players = await CreatePlayersAsync(anchor, passwordHash, random, cancellationToken);
        var venues = CreateVenues(owners);
        dbContext.Clubs.AddRange(venues.Select(venue => venue.Club));
        dbContext.Pitches.AddRange(venues.SelectMany(venue => venue.Pitches).Select(pitch => pitch.Pitch));
        var blocks = CreateBlocks(venues, today);
        dbContext.Blocks.AddRange(blocks);
        await dbContext.SaveChangesAsync(cancellationToken);

        var bookings = new BookingGenerator(random, anchor, today, venues, players, blocks).Generate();
        foreach (var chunk in bookings.Chunk(500))
        {
            dbContext.Bookings.AddRange(chunk);
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
        }

        await transaction.CommitAsync(cancellationToken);

        var summary = new DemoSeedSummary(
            owners.Count,
            players.Count,
            venues.Count,
            venues.Sum(venue => venue.Pitches.Count),
            blocks.Count,
            bookings.Count,
            bookings.Count(booking => booking.IsForCustomer),
            bookings.Count(booking => booking.Status == BookingStatus.Confirmed),
            bookings.Count(booking => booking.Status == BookingStatus.CancelledByPlayer),
            bookings.Count(booking => booking.Status == BookingStatus.CancelledByClub),
            bookings.Count(booking => booking.Status == BookingStatus.NoShow));
        LogSeeded(logger, summary.Owners, summary.Players, summary.Clubs, summary.Pitches, summary.Blocks, summary.Bookings);
        return summary;
    }

    // ---- Accounts

    private sealed record DemoOwner(Guid Id, DateTime RegisteredAt);

    private async Task<List<DemoOwner>> CreateOwnersAsync(DateTime anchor, string passwordHash, CancellationToken cancellationToken)
    {
        var owners = new List<DemoOwner>();
        for (var index = 0; index < OwnerCount; index++)
        {
            // Owners signed up over the first half of the last 90 days (88, 76, 64, 52 and 40 days ago), so the admin's
            // registrations chart shows them; each club opens the day after its owner registered, and takes no booking before.
            var registeredAt = anchor.AddDays(-(RegistrationDays - 2) + (index * 12)).AddHours(10 + index);
            var user = new AppUser(DemoNames.OwnerEmail(index), DemoNames.Owners[index], registeredAt)
            {
                PhoneNumber = $"9{1 + index}{3000000 + (index * 111111):0000000}"[..9],
                PasswordHash = passwordHash,
            };
            await CreateAccountAsync(user, Profiles.Owner, cancellationToken);
            owners.Add(new DemoOwner(user.Id, registeredAt));
        }

        return owners;
    }

    private async Task<List<DemoPlayer>> CreatePlayersAsync(DateTime anchor, string passwordHash, Random random, CancellationToken cancellationToken)
    {
        var players = new List<DemoPlayer>();
        for (var index = 0; index < PlayerCount; index++)
        {
            // Spread over the last 90 days, oldest first; the first dozen are there from the start of the booking history.
            var daysAgo = RegistrationDays - (index * (RegistrationDays - 1.0) / (PlayerCount - 1));
            var registeredAt = anchor.AddDays(-daysAgo).AddMinutes(random.Next(8 * 60, 22 * 60) - (24 * 60));
            var user = new AppUser(DemoNames.PlayerEmail(index), DemoNames.PlayerName(index), registeredAt)
            {
                PhoneNumber = index % 5 < 2 ? $"96{random.Next(1_000_000, 10_000_000)}" : null,
                PasswordHash = passwordHash,
            };
            await CreateAccountAsync(user, Profiles.Player, cancellationToken);
            // Most players book near where they live, and a few book a lot more than the others.
            players.Add(new DemoPlayer(user.Id, registeredAt, Region: index % 5 < 3 ? Region.Porto : Region.Lisboa, Weight: 0.3 + (random.NextDouble() * random.NextDouble() * 4)));
        }

        return players;
    }

    private async Task CreateAccountAsync(AppUser user, string profile, CancellationToken cancellationToken)
    {
        var created = await users.CreateAsync(user);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException($"A demo account could not be created: {string.Join(", ", created.Errors.Select(error => error.Code))}.");
        }

        var assigned = await users.AddToRoleAsync(user, profile);
        if (!assigned.Succeeded)
        {
            throw new InvalidOperationException($"A demo account could not get its profile: {string.Join(", ", assigned.Errors.Select(error => error.Code))}.");
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    // ---- Clubs, pitches, blocks

    private static List<DemoVenue> CreateVenues(List<DemoOwner> owners)
    {
        var taken = new List<string>();
        var venues = new List<DemoVenue>();
        foreach (var (spec, number) in DemoCatalog.Clubs.Select((spec, index) => (spec, index + 1)))
        {
            var slug = ClubSlug.NextAvailable(ClubSlug.FromName(spec.Name), taken);
            taken.Add(slug);
            var details = new ClubDetails(spec.Name, spec.Address, spec.Locality, spec.Phone, $"geral.clube{number:00}@{EmailDomain}");
            // The club opens the day after its owner signed up (and its clubs an hour apart).
            var createdAt = owners[spec.OwnerIndex].RegisteredAt.AddDays(1).AddHours(number);
            var club = Club.Create(owners[spec.OwnerIndex].Id, slug, details, spec.CancellationWindowHours, createdAt);
            var pitches = spec.Pitches.Select(pitchSpec =>
            {
                var pitch = Pitch.Create(club.Id, new PitchDetails(pitchSpec.Name, pitchSpec.Sport, pitchSpec.Price), createdAt.AddHours(1));
                pitch.ChangeSchedule(SlotDuration.FromMinutes(pitchSpec.SlotMinutes), pitchSpec.Week(), createdAt.AddHours(1));
                return new DemoPitch(pitch, pitchSpec.Popularity);
            }).ToList();
            venues.Add(new DemoVenue(club, spec.Region, pitches));
        }

        return venues;
    }

    private static List<Block> CreateBlocks(List<DemoVenue> venues, DateOnly today)
    {
        var blocks = new List<Block>();
        foreach (var spec in DemoCatalog.Blocks)
        {
            var venue = venues[spec.ClubIndex];
            var pitch = venue.Pitches[spec.PitchIndex].Pitch;
            var date = today.AddDays(spec.DaysFromToday);
            var startsAt = ClubTime.ToUtc(date, spec.Starts, Lisbon);
            var endsAt = ClubTime.ToUtc(date, spec.Ends, Lisbon);
            // Made some days before it starts (or, for one that is over, before it started), never in the future of the data.
            var createdAt = ClubTime.ToUtc(today, TimeOnly.MinValue, Lisbon).AddDays(Math.Min(spec.DaysFromToday - 7, -1));
            blocks.Add(Block.Create(pitch.Id, startsAt, endsAt, spec.Reason, createdAt));
        }

        return blocks;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Creating demo data in the Production environment, because DemoData:AllowInProduction is true.")]
    private static partial void LogAllowedInProduction(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo data created: {Owners} owners, {Players} players, {Clubs} clubs, {Pitches} pitches, {Blocks} blocks, {Bookings} bookings.")]
    private static partial void LogSeeded(ILogger logger, int owners, int players, int clubs, int pitches, int blocks, int bookings);

    // ---- Bookings

    private sealed record DemoPlayer(Guid Id, DateTime RegisteredAt, Region Region, double Weight);

    private sealed record DemoPitch(Pitch Pitch, double Popularity);

    private sealed record DemoVenue(Club Club, Region Region, List<DemoPitch> Pitches);

    /// <summary>Walks every pitch day by day and slot by slot, and books some of the slots.</summary>
    private sealed class BookingGenerator(Random random, DateTime anchor, DateOnly today, List<DemoVenue> venues, List<DemoPlayer> players, List<Block> blocks)
    {
        private readonly List<Booking> _bookings = [];

        public List<Booking> Generate()
        {
            foreach (var venue in venues)
            {
                foreach (var demoPitch in venue.Pitches)
                {
                    var pitchBlocks = blocks.Where(block => block.PitchId == demoPitch.Pitch.Id).ToList();
                    for (var offset = -PastDays; offset < FutureDays; offset++)
                    {
                        var date = today.AddDays(offset);
                        foreach (var slot in demoPitch.Pitch.SlotsOn(date, Lisbon))
                        {
                            if (pitchBlocks.Any(block => block.Overlaps(slot.StartsAtUtc, slot.EndsAtUtc)))
                            {
                                continue;
                            }

                            if (random.NextDouble() < Demand(venue, demoPitch, date, slot, offset))
                            {
                                Book(venue, demoPitch.Pitch, slot, offset);
                            }
                        }
                    }
                }
            }

            return _bookings;
        }

        /// <summary>The chance that a slot is booked: busier in the evening and at weekends, growing over time, thinner far ahead.</summary>
        private static double Demand(DemoVenue venue, DemoPitch demoPitch, DateOnly date, TimeSlot slot, int offset)
        {
            var local = ClubTime.ToLocal(slot.StartsAtUtc, Lisbon);
            var hour = local.Hour + (local.Minute / 60.0);
            var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var chance = weekend
                ? hour < 12 ? 0.35 : hour < 17 ? 0.50 : hour < 22 ? 0.72 : 0.35
                : hour < 12 ? 0.10 : hour < 17 ? 0.18 : hour < 22 ? 0.58 : 0.30;
            chance *= demoPitch.Popularity;
            chance *= demoPitch.Pitch.Sport switch { Sport.Padel => 1.1, Sport.Futsal => 1.0, Sport.Football7 => 0.9, _ => weekend ? 0.85 : 0.7 };
            chance *= venue.Region == Region.Lisboa ? 1.05 : 1.0;
            // Growth: the first weeks of the history are quieter than the last.
            chance *= 0.7 + (0.3 * Math.Clamp((offset + PastDays) / (double)PastDays, 0, 1));
            if (offset > 0)
            {
                chance *= Math.Max(0.35, 1 - (offset * 0.05));
            }

            return Math.Min(chance, 0.9);
        }

        private void Book(DemoVenue venue, Pitch pitch, TimeSlot slot, int offset)
        {
            var club = venue.Club;
            var isPast = slot.EndsAtUtc <= anchor;
            var forCustomer = random.NextDouble() < 0.06;
            var createdAt = CreationTime(slot.StartsAtUtc, forCustomer);
            if (createdAt < pitch.CreatedAt)
            {
                // The club (and so its owner's account) did not exist yet when this booking would have been made.
                return;
            }

            var booking = forCustomer
                ? Booking.ForCustomer(pitch.Id, DemoNames.CustomerName(random), DemoNames.CustomerPhone(random), slot.StartsAtUtc, slot.EndsAtUtc, pitch.PricePerSlot, createdAt)
                : PlayerBooking(venue, pitch, slot, createdAt);
            if (booking is null)
            {
                return;
            }

            // What became of it. Past bookings: mostly attended; some cancelled by the player or by the club, some no-shows.
            var roll = random.NextDouble();
            var cancelledByClub = isPast ? roll >= 0.91 && roll < 0.94 : roll >= 0.96;
            var cancelledByPlayer = !forCustomer && (isPast ? roll >= 0.80 && roll < 0.91 : roll >= 0.87 && roll < 0.96);
            var noShow = isPast && roll >= 0.94;

            if (cancelledByPlayer)
            {
                var latest = Min(slot.StartsAtUtc - club.CancellationWindow, anchor) - TimeSpan.FromMinutes(1);
                var earliest = createdAt + TimeSpan.FromMinutes(30);
                if (earliest < latest)
                {
                    var outcome = booking.CancelByPlayer(club.CancellationWindow, Between(earliest, latest));
                    if (outcome != CancellationOutcome.Cancelled)
                    {
                        throw new InvalidOperationException("A demo cancellation by the player was refused by the entity.");
                    }
                }
            }
            else if (cancelledByClub)
            {
                var latest = Min(slot.StartsAtUtc - TimeSpan.FromHours(1), anchor);
                var earliest = createdAt + TimeSpan.FromHours(1);
                if (earliest < latest)
                {
                    var outcome = booking.CancelByClub(DemoCatalog.ClubReasons[random.Next(DemoCatalog.ClubReasons.Length)], Between(earliest, latest));
                    if (outcome != ClubCancellationOutcome.Cancelled)
                    {
                        throw new InvalidOperationException("A demo cancellation by the club was refused by the entity.");
                    }
                }
            }
            else if (noShow)
            {
                booking.MarkNoShow(anchor);
            }

            _bookings.Add(booking);

            // A slot that was freed in time is often taken by somebody else.
            var freed = booking.Status is BookingStatus.CancelledByPlayer or BookingStatus.CancelledByClub && booking.CancelledAt is { } cancelledAt && cancelledAt < anchor;
            if (freed && random.NextDouble() < 0.45)
            {
                var replacementAt = Between(booking.CancelledAt!.Value, Min(slot.StartsAtUtc - TimeSpan.FromMinutes(30), anchor));
                if (replacementAt > booking.CancelledAt && replacementAt < slot.StartsAtUtc)
                {
                    var replacement = PlayerBooking(venue, pitch, slot, replacementAt);
                    if (replacement is not null)
                    {
                        if (isPast && random.NextDouble() < 0.10)
                        {
                            replacement.MarkNoShow(anchor);
                        }

                        _bookings.Add(replacement);
                    }
                }
            }
        }

        /// <summary>
        /// When it was made: usually a day or two ahead, sometimes a week or three, but never after the start, never later
        /// than the data's "now", and never more than 30 days before the start (the booking window).
        /// </summary>
        private DateTime CreationTime(DateTime startsAt, bool forCustomer)
        {
            var roll = random.NextDouble();
            var lead = forCustomer
                ? TimeSpan.FromHours(1 + random.Next(0, 72))
                : roll < 0.60 ? TimeSpan.FromHours(1 + random.Next(0, 48))
                : roll < 0.90 ? TimeSpan.FromHours(48 + random.Next(0, 120))
                : TimeSpan.FromHours(168 + random.Next(0, 360));
            var createdAt = startsAt - lead;
            if (createdAt > anchor)
            {
                // Still to come: it was made before the data's "now".
                createdAt = anchor - TimeSpan.FromMinutes(random.Next(60, 60 * 60));
            }

            return createdAt;
        }

        private Booking? PlayerBooking(DemoVenue venue, Pitch pitch, TimeSlot slot, DateTime createdAt)
        {
            var eligible = players.Where(player => player.RegisteredAt <= createdAt).ToList();
            if (eligible.Count == 0)
            {
                return null;
            }

            // Near home most of the time, and the keen players more often.
            var weights = eligible.Select(player => player.Weight * (player.Region == venue.Region ? 3.0 : 0.5)).ToList();
            var pick = random.NextDouble() * weights.Sum();
            var chosen = eligible[^1];
            for (var i = 0; i < eligible.Count; i++)
            {
                pick -= weights[i];
                if (pick <= 0)
                {
                    chosen = eligible[i];
                    break;
                }
            }

            return Booking.ForPlayer(pitch.Id, chosen.Id, slot.StartsAtUtc, slot.EndsAtUtc, pitch.PricePerSlot, createdAt);
        }

        private DateTime Between(DateTime earliest, DateTime latest) =>
            latest <= earliest ? earliest : earliest + TimeSpan.FromSeconds(random.NextDouble() * (latest - earliest).TotalSeconds);

        private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    }
}
