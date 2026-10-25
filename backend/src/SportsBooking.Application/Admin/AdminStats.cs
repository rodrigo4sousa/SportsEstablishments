using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Bookings;
using SportsBooking.Domain.Pitches;

namespace SportsBooking.Application.Admin;

/// <summary>How many accounts of a profile are active and how many are deactivated (US24).</summary>
public sealed record AccountCounts(int Active, int Deactivated);

/// <summary>
/// The clubs, split three ways that add up to <see cref="Total"/>: <see cref="Visible"/> (the owner is active and the club has an
/// active pitch, what the public pages show, US16), <see cref="OfDeactivatedOwners"/> and <see cref="WithoutActivePitches"/> (an
/// active owner, but nothing to book yet).
/// </summary>
public sealed record ClubCounts(int Total, int Visible, int OfDeactivatedOwners, int WithoutActivePitches);

/// <summary>The platform's totals (US24): numbers only.</summary>
public sealed record AdminStatsResponse(AccountCounts Owners, ClubCounts Clubs, int ActivePitches, AccountCounts Players, int ConfirmedBookings);

/// <summary>
/// US24: the totals of the platform, each worked out by the database (<c>COUNT</c>, <c>GROUP BY</c>): nothing is loaded into
/// memory but the numbers. Active pitches are all the active pitches, whoever owns them; confirmed bookings are all the
/// confirmed ones, past and future, a player's and a customer's (provisional).
/// </summary>
public sealed class AdminStatsService(IAppDbContext dbContext)
{
    public async Task<AdminStatsResponse> GetAsync(CancellationToken cancellationToken)
    {
        // One grouped query for every account: profile and state, with how many of each.
        var accounts = await dbContext.AccountProfiles.AsNoTracking()
            .GroupBy(account => new { account.Profile, account.IsDeactivated })
            .Select(group => new { group.Key.Profile, group.Key.IsDeactivated, Count = group.Count() })
            .ToListAsync(cancellationToken);
        AccountCounts Of(string profile) => new(
            accounts.Where(row => row.Profile == profile && !row.IsDeactivated).Sum(row => row.Count),
            accounts.Where(row => row.Profile == profile && row.IsDeactivated).Sum(row => row.Count));

        var activeAccounts = dbContext.ActiveAccountIds;
        var activePitches = dbContext.Pitches.Where(pitch => pitch.Status == PitchStatus.Active);
        var totalClubs = await dbContext.Clubs.CountAsync(cancellationToken);
        var ofDeactivatedOwners = await dbContext.Clubs.CountAsync(club => !activeAccounts.Contains(club.OwnerId), cancellationToken);
        var visible = await dbContext.Clubs.CountAsync(
            club => activeAccounts.Contains(club.OwnerId) && activePitches.Any(pitch => pitch.ClubId == club.Id), cancellationToken);

        return new AdminStatsResponse(
            Of(Profiles.Owner),
            new ClubCounts(totalClubs, visible, ofDeactivatedOwners, totalClubs - visible - ofDeactivatedOwners),
            await activePitches.CountAsync(cancellationToken),
            Of(Profiles.Player),
            await dbContext.Bookings.CountAsync(booking => booking.Status == BookingStatus.Confirmed, cancellationToken));
    }
}
