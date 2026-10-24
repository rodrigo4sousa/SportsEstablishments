using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Common;
using SportsBooking.Application.Pitches;
using SportsBooking.Domain.Clubs;
using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Catalog;

/// <summary>
/// The search of US16, from the query string. <see cref="Date"/> is a local date "yyyy-MM-dd" (today in Lisbon by default).
/// </summary>
public sealed record ClubSearchRequest(string? Sport, string? Locality, string? Date, int Page = 1)
{
    /// <summary>Clubs per page; fixed by US16, not chosen by the client.</summary>
    public const int PageSize = 20;

    /// <summary>A defensive upper bound (provisional): 10,000 pages of 20 is far more clubs than will exist.</summary>
    public const int MaximumPage = 10_000;

    /// <summary>Blank filters mean "no filter".</summary>
    public ClubSearchRequest Normalized() => this with { Sport = Clean(Sport), Locality = Clean(Locality), Date = Clean(Date) };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// One result: no address or contacts (those are on the club page). <see cref="Sports"/> are the stable sport codes of
/// the club's active pitches; <see cref="HasFreeSlots"/> says whether any active pitch has a free slot on the date.
/// </summary>
public sealed record ClubSearchItem(string Slug, string Name, string Locality, IReadOnlyList<string> Sports, bool HasFreeSlots);

public sealed class ClubSearchRequestValidator : AbstractValidator<ClubSearchRequest>
{
    public ClubSearchRequestValidator()
    {
        RuleFor(request => request.Sport)
            .Must(sport => sport is null || Sports.TryParse(sport, out _))
            .WithMessage("Escolhe um desporto da lista.");

        RuleFor(request => request.Locality)
            .MaximumLength(ClubRules.LocalityMaximumLength)
            .WithMessage($"A localidade tem de ter no máximo {ClubRules.LocalityMaximumLength} caracteres.");

        RuleFor(request => request.Page)
            .InclusiveBetween(1, ClubSearchRequest.MaximumPage)
            .WithMessage($"A página tem de estar entre 1 e {ClubSearchRequest.MaximumPage}.");
    }
}

/// <summary>
/// US16: a visitor or player searches the clubs that can take bookings. Anyone can use it, so it returns nothing about
/// players or owners. Free slots are worked out only for the clubs on the page, by <see cref="PitchDayCalendar"/>
/// (the same calculation as the slot list of US17).
/// </summary>
public sealed class ClubSearchService(
    VisibleClubs visibleClubs,
    IAppDbContext dbContext,
    PitchDayCalendar calendar,
    ClubSearchRequestValidator validator,
    TimeProvider timeProvider)
{
    private sealed record ClubRow(Guid Id, string Slug, string Name, string Locality, string TimeZoneId);

    public async Task<Result<PagedResponse<ClubSearchItem>>> SearchAsync(ClubSearchRequest request, CancellationToken cancellationToken)
    {
        var normalized = request.Normalized();
        var validation = await validator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        // The platform's zone: today (and so the default date and the window) is the same for every visitor.
        var today = ClubTime.Today(timeProvider, ClubTimeZones.Lisbon);
        var date = today;
        if (normalized.Date is not null)
        {
            var requested = BookingDateInput.Parse(normalized.Date, today);
            if (!requested.IsSuccess)
            {
                return requested.Error!;
            }

            date = requested.Value;
        }

        var clubs = visibleClubs.All();
        if (normalized.Locality is not null)
        {
            var localityKey = ClubRules.LocalityKey(normalized.Locality);
            clubs = clubs.Where(club => club.LocalityKey == localityKey);
        }

        if (normalized.Sport is not null && Sports.TryParse(normalized.Sport, out var sport))
        {
            // An inactive pitch does not count: its sport is not found by the filter (US16).
            clubs = clubs.Where(club => dbContext.Pitches.Any(pitch => pitch.ClubId == club.Id && pitch.Status == PitchStatus.Active && pitch.Sport == sport));
        }

        var total = await clubs.CountAsync(cancellationToken);
        var skip = (normalized.Page - 1) * ClubSearchRequest.PageSize;
#pragma warning disable CA1304, CA1311 // Translated to SQL lower(); no .NET culture is involved.
        var rows = await clubs
            .OrderBy(club => club.Name.ToLower())
            .ThenBy(club => club.Name)
            .ThenBy(club => club.Id)
            .Skip(skip)
            .Take(ClubSearchRequest.PageSize)
            .Select(club => new ClubRow(club.Id, club.Slug, club.Name, club.Locality, club.TimeZoneId))
            .ToListAsync(cancellationToken);
#pragma warning restore CA1304, CA1311
        if (rows.Count == 0)
        {
            return new PagedResponse<ClubSearchItem>([], normalized.Page, ClubSearchRequest.PageSize, total);
        }

        // Only now, for the clubs on this page: their active pitches and what is free on the date.
        var clubIds = rows.Select(row => row.Id).ToList();
        var pitches = await dbContext.Pitches.AsNoTracking()
            .Where(pitch => clubIds.Contains(pitch.ClubId) && pitch.Status == PitchStatus.Active)
            .ToListAsync(cancellationToken);
        var zones = rows.ToDictionary(row => row.Id, row => TimeZoneInfo.FindSystemTimeZoneById(row.TimeZoneId));
        var daySlots = await calendar.SlotsAsync(pitches.Select(pitch => new PitchInZone(pitch, zones[pitch.ClubId])).ToList(), date, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var items = rows.Select(row =>
        {
            var clubPitches = pitches.Where(pitch => pitch.ClubId == row.Id).ToList();
            return new ClubSearchItem(
                row.Slug,
                row.Name,
                row.Locality,
                Sports.All.Where(sport => clubPitches.Any(pitch => pitch.Sport == sport)).Select(Sports.ToCode).ToList(),
                clubPitches.Any(pitch => daySlots[pitch.Id].Any(slot => slot.IsFreeAt(now))));
        }).ToList();
        return new PagedResponse<ClubSearchItem>(items, normalized.Page, ClubSearchRequest.PageSize, total);
    }

    /// <summary>
    /// The localities of the visible clubs, for a select (provisional): one entry for each spelling-insensitive
    /// locality (see <see cref="ClubRules.LocalityKey"/>), shown as one of the spellings in use, ordered by key.
    /// </summary>
    public async Task<IReadOnlyList<string>> LocalitiesAsync(CancellationToken cancellationToken) =>
        await visibleClubs.All()
            .GroupBy(club => club.LocalityKey)
            .Select(group => new { Key = group.Key, Label = group.Min(club => club.Locality)! })
            .OrderBy(entry => entry.Key)
            .Select(entry => entry.Label)
            .ToListAsync(cancellationToken);
}
