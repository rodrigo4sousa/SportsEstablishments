using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Common;
using SportsBooking.Application.Pitches;
using SportsBooking.Application.Clubs;
using SportsBooking.Domain.Bookings;
using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Bookings;

/// <summary>
/// The query string of <c>GET /api/manage/clubs/{clubId}/calendar</c> (US11), read as text so that a wrong value gets a
/// Portuguese message on its field and not MVC's English binding error. <see cref="From"/> and <see cref="To"/> are
/// local dates of the club ("yyyy-MM-dd"), both included; <see cref="IncludeCancelled"/> is <c>true</c> or <c>false</c>.
/// </summary>
public sealed record CalendarRequest(string? From, string? To, string? IncludeCancelled)
{
    /// <summary>The longest range a calendar answers, both dates counted (a week).</summary>
    public const int MaximumDays = 7;

    /// <summary>The first and last date accepted (provisional): a sanity bound, so no date arithmetic can overflow.</summary>
    public static readonly DateOnly Earliest = new(2000, 1, 1);

    public static readonly DateOnly Latest = new(2100, 12, 31);

    public CalendarRequest Normalized() => this with
    {
        From = From?.Trim() ?? string.Empty,
        To = To?.Trim() ?? string.Empty,
        IncludeCancelled = string.IsNullOrWhiteSpace(IncludeCancelled) ? null : IncludeCancelled.Trim().ToLowerInvariant(),
    };

    public static bool TryParseDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text, BookingDateInput.Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}

public sealed class CalendarRequestValidator : AbstractValidator<CalendarRequest>
{
    private const string BadDate = "Indica a data no formato AAAA-MM-DD, por exemplo 2026-10-25.";

    public CalendarRequestValidator()
    {
        RuleFor(request => request.From)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica a data inicial.")
            .Must(IsValidDate).WithMessage(BadDate)
            .Must(IsInBounds).WithMessage($"A data tem de estar entre {CalendarRequest.Earliest.Year} e {CalendarRequest.Latest.Year}.");

        RuleFor(request => request.To)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica a data final.")
            .Must(IsValidDate).WithMessage(BadDate)
            .Must(IsInBounds).WithMessage($"A data tem de estar entre {CalendarRequest.Earliest.Year} e {CalendarRequest.Latest.Year}.")
            .Must((request, _) => !BothDates(request, out var from, out var to) || from <= to)
            .WithMessage("A data final não pode ser anterior à inicial.")
            .Must((request, _) => !BothDates(request, out var from, out var to) || to.DayNumber - from.DayNumber < CalendarRequest.MaximumDays)
            .WithMessage($"O calendário mostra no máximo {CalendarRequest.MaximumDays} dias de cada vez.");

        RuleFor(request => request.IncludeCancelled)
            .Must(value => value is null or "true" or "false")
            .WithMessage("Indica «true» ou «false».");
    }

    private static bool IsValidDate(string? text) => CalendarRequest.TryParseDate(text, out _);

    private static bool IsInBounds(string? text) =>
        CalendarRequest.TryParseDate(text, out var date) && date >= CalendarRequest.Earliest && date <= CalendarRequest.Latest;

    private static bool BothDates(CalendarRequest request, out DateOnly from, out DateOnly to)
    {
        from = default;
        to = default;
        return CalendarRequest.TryParseDate(request.From, out from) && CalendarRequest.TryParseDate(request.To, out to);
    }
}

/// <summary>One day's opening hours of a pitch, in the club's local time ("09:00"); both null when the pitch is closed that day. A closing of "00:00" is midnight.</summary>
public sealed record CalendarDay(string Date, string? Opens, string? Closes);

/// <summary>A pitch of the calendar: the club's active pitches, and an inactive one that has bookings in the range.</summary>
public sealed record CalendarPitch(Guid Id, string Name, string Sport, string Status, int SlotDurationMinutes, IReadOnlyList<CalendarDay> Days);

/// <summary>A booking in the range, with the player's name. Instants are UTC; show them in the club's time zone.</summary>
public sealed record CalendarBooking(Guid Id, Guid PitchId, DateTime StartsAt, DateTime EndsAt, string Status, decimal Price, string PlayerName);

public sealed record CalendarBlock(Guid Id, Guid PitchId, DateTime StartsAt, DateTime EndsAt, string? Reason);

public sealed record CalendarResponse(
    string From,
    string To,
    IReadOnlyList<CalendarPitch> Pitches,
    IReadOnlyList<CalendarBooking> Bookings,
    IReadOnlyList<CalendarBlock> Blocks);

/// <summary>
/// US11: what the owner's calendar shows for up to seven days of one club: its pitches with their opening hours, the
/// bookings (with the player's name, which only this club's owner sees) and the blocks. The range is in local dates of
/// the club, so a day is what the clock says (25 hours on 25 October 2026): a booking belongs to the range if it overlaps
/// it. Cancelled bookings (by the player or by the club) are left out unless asked for; a no-show is not a cancellation.
/// The club is checked first (T02, 404 before validating); pitches and bookings are reached only through it.
/// </summary>
public sealed class ClubCalendarService(
    OwnedPitches ownedPitches,
    OwnedBookings ownedBookings,
    IAppDbContext dbContext,
    CalendarRequestValidator validator)
{
    public async Task<Result<CalendarResponse>> GetAsync(Guid clubId, CalendarRequest request, CancellationToken cancellationToken)
    {
        if (!await ownedPitches.ClubIsOwnedAsync(clubId, cancellationToken))
        {
            return ClubErrors.NotFound;
        }

        var normalized = request.Normalized();
        var validation = await validator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        // Validated above, so both parse.
        var from = DateOnly.ParseExact(normalized.From!, BookingDateInput.Format, CultureInfo.InvariantCulture);
        var to = DateOnly.ParseExact(normalized.To!, BookingDateInput.Format, CultureInfo.InvariantCulture);
        var includeCancelled = normalized.IncludeCancelled == "true";

        var timeZone = await ownedPitches.TimeZoneOfAsync(clubId, cancellationToken);
        var rangeStart = ClubTime.ToUtc(from, TimeOnly.MinValue, timeZone);
        var rangeEnd = ClubTime.ToUtc(to.AddDays(1), TimeOnly.MinValue, timeZone);

        var visible = ownedBookings.Of(clubId).AsNoTracking()
            .Where(booking => booking.StartsAt < rangeEnd && rangeStart < booking.EndsAt);
        if (!includeCancelled)
        {
            visible = visible.Where(booking => booking.Status == BookingStatus.Confirmed || booking.Status == BookingStatus.NoShow);
        }

        var bookings = await visible
            .Join(dbContext.AccountNames, booking => booking.PlayerId, account => (Guid?)account.Id, (booking, account) => new BookingRow { Booking = booking, PlayerName = account.Name })
            .OrderBy(row => row.Booking.StartsAt)
            .ThenBy(row => row.Booking.Id)
            .ToListAsync(cancellationToken);

        var withBookings = bookings.Select(row => row.Booking.PitchId).ToHashSet();
        var pitches = (await ownedPitches.Of(clubId).AsNoTracking()
                .OrderBy(pitch => pitch.NameKey)
                .ThenBy(pitch => pitch.Id)
                .ToListAsync(cancellationToken))
            .Where(pitch => pitch.IsActive || withBookings.Contains(pitch.Id))
            .ToList();

        var pitchIds = pitches.Select(pitch => pitch.Id).ToList();
        var blocks = await dbContext.Blocks.AsNoTracking()
            .Where(block => pitchIds.Contains(block.PitchId) && block.StartsAt < rangeEnd && rangeStart < block.EndsAt)
            .OrderBy(block => block.StartsAt)
            .ThenBy(block => block.Id)
            .ToListAsync(cancellationToken);

        return new CalendarResponse(
            normalized.From!,
            normalized.To!,
            pitches.Select(pitch => ToCalendarPitch(pitch, from, to)).ToList(),
            bookings.Select(row => new CalendarBooking(
                row.Booking.Id,
                row.Booking.PitchId,
                row.Booking.StartsAt,
                row.Booking.EndsAt,
                BookingStatuses.ToCode(row.Booking.Status),
                row.Booking.Price,
                row.PlayerName)).ToList(),
            blocks.Select(block => new CalendarBlock(block.Id, block.PitchId, block.StartsAt, block.EndsAt, block.Reason)).ToList());
    }

    private sealed class BookingRow
    {
        public required Booking Booking { get; init; }

        public required string PlayerName { get; init; }
    }

    private static CalendarPitch ToCalendarPitch(Pitch pitch, DateOnly from, DateOnly to)
    {
        var days = new List<CalendarDay>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var hours = pitch.HoursOn(date.DayOfWeek);
            days.Add(new CalendarDay(
                date.ToString(BookingDateInput.Format, CultureInfo.InvariantCulture),
                hours?.Opens.ToString("HH:mm", CultureInfo.InvariantCulture),
                hours?.Closes.ToString("HH:mm", CultureInfo.InvariantCulture)));
        }

        return new CalendarPitch(pitch.Id, pitch.Name, Sports.ToCode(pitch.Sport), pitch.IsActive ? "active" : "inactive", pitch.SlotDurationMinutes, days);
    }
}
