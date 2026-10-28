using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Clubs;
using SportsBooking.Application.Common;
using SportsBooking.Application.Pitches;
using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Catalog;

/// <summary>
/// An active pitch on the club page: what a player needs to pick one. <see cref="PricePerSlot"/> is the base price, the one
/// outside every price band; <see cref="HasPriceBands"/> says some slots cost something else (US10), and each slot says its own price.
/// </summary>
public sealed record PublicPitchResponse(Guid Id, string Name, string Sport, decimal PricePerSlot, bool HasPriceBands);

/// <summary>
/// The public page of a club (US16): its contacts and its active pitches. Nothing about the owner or the players.
/// The club's own id stays private (the owner area uses it); the public address is the slug.
/// </summary>
public sealed record PublicClubResponse(
    string Slug,
    string Name,
    string Address,
    string Locality,
    string Phone,
    string ContactEmail,
    IReadOnlyList<PublicPitchResponse> Pitches);

/// <summary>
/// A slot of a pitch on a date (US17). <see cref="Available"/> is the only thing said about its state: a slot that is
/// booked, blocked, outside the hours or already started is just not available, with no reason and no ids.
/// </summary>
public sealed record PublicSlotResponse(DateTime StartsAt, DateTime EndsAt, decimal Price, bool Available);

/// <summary>Every slot the pitch's schedule generates on the local date, in order (none when the pitch is closed that day).</summary>
public sealed record PublicSlotsResponse(DateOnly Date, IReadOnlyList<PublicSlotResponse> Slots);

/// <summary>
/// US16 and US17: what a visitor or player can read about one club and its pitches. Everything starts from
/// <see cref="VisibleClubs"/>, so an unknown club, a club without an active pitch and a club of a deactivated owner
/// all give the same 404 <c>club_not_found</c>.
/// </summary>
public sealed class PublicClubService(
    VisibleClubs visibleClubs,
    IAppDbContext dbContext,
    PitchDayCalendar calendar,
    TimeProvider timeProvider)
{
    public async Task<Result<PublicClubResponse>> GetAsync(string slug, CancellationToken cancellationToken)
    {
        var club = await visibleClubs.All()
            .Where(candidate => candidate.Slug == slug)
            .Select(candidate => new { candidate.Id, candidate.Slug, candidate.Name, candidate.Address, candidate.Locality, candidate.Phone, candidate.ContactEmail })
            .SingleOrDefaultAsync(cancellationToken);
        if (club is null)
        {
            return ClubErrors.NotFound;
        }

#pragma warning disable CA1304, CA1311 // Translated to SQL lower(); no .NET culture is involved.
        var pitches = await dbContext.Pitches.AsNoTracking()
            .Where(pitch => pitch.ClubId == club.Id && pitch.Status == PitchStatus.Active)
            .OrderBy(pitch => pitch.Name.ToLower())
            .ThenBy(pitch => pitch.Name)
            .ThenBy(pitch => pitch.Id)
            .ToListAsync(cancellationToken);
#pragma warning restore CA1304, CA1311

        return new PublicClubResponse(
            club.Slug,
            club.Name,
            club.Address,
            club.Locality,
            club.Phone,
            club.ContactEmail,
            pitches.Select(pitch => new PublicPitchResponse(pitch.Id, pitch.Name, Sports.ToCode(pitch.Sport), pitch.PricePerSlot, pitch.HasPriceBands)).ToList());
    }

    /// <summary>
    /// The slots of one active pitch of a visible club on a local date ("yyyy-MM-dd", today up to 30 days ahead in the
    /// club's time zone). The club and the pitch are checked first, so a hidden one is a 404 whatever the date is. A
    /// slot is available when <see cref="DaySlot.IsFreeAt"/> says so: the one rule the search uses too.
    /// </summary>
    public async Task<Result<PublicSlotsResponse>> SlotsAsync(string slug, Guid pitchId, string? date, CancellationToken cancellationToken)
    {
        var club = await visibleClubs.All()
            .Where(candidate => candidate.Slug == slug)
            .Select(candidate => new { candidate.Id, candidate.TimeZoneId })
            .SingleOrDefaultAsync(cancellationToken);
        if (club is null)
        {
            return ClubErrors.NotFound;
        }

        // An inactive pitch shows no slots (US17): to the public it is simply not there.
        var pitch = await dbContext.Pitches.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == pitchId && candidate.ClubId == club.Id && candidate.Status == PitchStatus.Active, cancellationToken);
        if (pitch is null)
        {
            return PitchErrors.NotFound;
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(club.TimeZoneId);
        var requested = BookingDateInput.Parse(date, ClubTime.Today(timeProvider, timeZone));
        if (!requested.IsSuccess)
        {
            return requested.Error!;
        }

        var daySlots = await calendar.SlotsAsync([new PitchInZone(pitch, timeZone)], requested.Value, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var slots = daySlots[pitch.Id]
            .Select(slot => new PublicSlotResponse(slot.Slot.StartsAtUtc, slot.Slot.EndsAtUtc, pitch.PriceForSlot(slot.Slot.StartsAtUtc, timeZone), slot.IsFreeAt(now)))
            .ToList();
        return new PublicSlotsResponse(requested.Value, slots);
    }
}
