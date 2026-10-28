using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Bookings;
using SportsBooking.Domain.Pitches;

namespace SportsBooking.Application.Bookings;

/// <summary>
/// One of the signed-in player's own bookings (US19). <see cref="Price"/> is what was paid, not the pitch's price today.
/// <see cref="CanCancel"/> is true while the booking is confirmed and now is strictly before
/// <see cref="CancellationDeadline"/> (the start minus the club's current cancellation window); the deadline is only
/// given for a confirmed booking.
/// </summary>
public sealed record MyBookingResponse(
    Guid Id,
    string ClubName,
    string ClubSlug,
    string PitchName,
    string Sport,
    DateTime StartsAt,
    DateTime EndsAt,
    decimal Price,
    string Status,
    bool CanCancel,
    DateTime? CancellationDeadline,
    MyBookingSeries? Series);

/// <summary>
/// US20: the weekly series a booking belongs to. <see cref="Position"/> of <see cref="Count"/> counts the bookings the series made
/// (cancelled ones included, dates left out not), in start order (provisional).
/// </summary>
public sealed record MyBookingSeries(Guid Id, int Position, int Count);

/// <summary>The query string of <c>GET /api/me/bookings</c>: <c>scope</c> is <c>upcoming</c> or <c>past</c>.</summary>
public sealed record MyBookingsRequest(string? Scope, int Page = 1)
{
    public const string Upcoming = "upcoming";

    public const string Past = "past";

    /// <summary>Bookings per page; fixed by US19, not chosen by the client.</summary>
    public const int PageSize = 20;

    /// <summary>A defensive upper bound (provisional), like the club search.</summary>
    public const int MaximumPage = 10_000;

    public MyBookingsRequest Normalized() => this with { Scope = string.IsNullOrWhiteSpace(Scope) ? null : Scope.Trim() };
}

public sealed class MyBookingsRequestValidator : AbstractValidator<MyBookingsRequest>
{
    public MyBookingsRequestValidator()
    {
        RuleFor(request => request.Scope)
            .Must(scope => scope is MyBookingsRequest.Upcoming or MyBookingsRequest.Past)
            .WithMessage($"Indica o âmbito: «{MyBookingsRequest.Upcoming}» (próximas) ou «{MyBookingsRequest.Past}» (anteriores).");

        RuleFor(request => request.Page)
            .InclusiveBetween(1, MyBookingsRequest.MaximumPage)
            .WithMessage($"A página tem de estar entre 1 e {MyBookingsRequest.MaximumPage}.");
    }
}

/// <summary>
/// US19: the signed-in player's bookings. Every query starts from the caller's own bookings (the player comes from the
/// session via <see cref="ICurrentUser"/>, never from the request), so another player's booking is simply not there and
/// gets the same 404 as one that does not exist. The cancellation window is read from the club at the moment of asking,
/// never from the booking (US06).
/// </summary>
public sealed class MyBookingsService(
    IAppDbContext dbContext,
    ICurrentUser currentUser,
    MyBookingsRequestValidator validator,
    TimeProvider timeProvider)
{
    // Filtered and ordered in SQL after it is projected, so it is built with an object initializer: EF cannot see
    // through a constructor call to translate `row.Status` or `row.Id`.
    private sealed class Row
    {
        public required Guid Id { get; init; }

        public required string ClubName { get; init; }

        public required string ClubSlug { get; init; }

        public required string PitchName { get; init; }

        public required Sport Sport { get; init; }

        public required DateTime StartsAt { get; init; }

        public required DateTime EndsAt { get; init; }

        public required decimal Price { get; init; }

        public required BookingStatus Status { get; init; }

        public required int WindowHours { get; init; }

        public required Guid? SeriesId { get; init; }

        public required int SeriesPosition { get; init; }

        public required int SeriesCount { get; init; }
    }

    /// <summary>
    /// <c>upcoming</c>: confirmed bookings that have not ended, oldest first. <c>past</c>: bookings that have ended and
    /// the ones that were cancelled (or marked as no-show), newest first (provisional). Twenty to a page.
    /// </summary>
    public async Task<Result<PagedResponse<MyBookingResponse>>> ListAsync(MyBookingsRequest request, CancellationToken cancellationToken)
    {
        var normalized = request.Normalized();
        var validation = await validator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var mine = Mine();
        var skip = (normalized.Page - 1) * MyBookingsRequest.PageSize;

        IQueryable<Row> ordered;
        if (normalized.Scope == MyBookingsRequest.Upcoming)
        {
            ordered = mine.Where(row => row.Status == BookingStatus.Confirmed && row.EndsAt > now)
                .OrderBy(row => row.StartsAt).ThenBy(row => row.Id);
        }
        else
        {
            ordered = mine.Where(row => row.Status != BookingStatus.Confirmed || row.EndsAt <= now)
                .OrderByDescending(row => row.StartsAt).ThenByDescending(row => row.Id);
        }

        var total = await ordered.CountAsync(cancellationToken);
        var rows = await ordered.Skip(skip).Take(MyBookingsRequest.PageSize).ToListAsync(cancellationToken);
        return new PagedResponse<MyBookingResponse>(rows.Select(row => ToResponse(row, now)).ToList(), normalized.Page, MyBookingsRequest.PageSize, total);
    }

    /// <summary>One of the caller's own bookings; any other id is 404 <c>booking_not_found</c>.</summary>
    public async Task<Result<MyBookingResponse>> GetAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var row = await Mine().Where(candidate => candidate.Id == bookingId).SingleOrDefaultAsync(cancellationToken);
        return row is null ? BookingErrors.NotFound : ToResponse(row, timeProvider.GetUtcNow().UtcDateTime);
    }

    private IQueryable<Row> Mine()
    {
        var playerId = currentUser.UserId
            ?? throw new InvalidOperationException("My bookings needs a signed-in player: protect the endpoint with the Player policy.");

        return dbContext.Bookings.AsNoTracking()
            .Where(booking => booking.PlayerId == playerId)
            .Join(dbContext.Pitches, booking => booking.PitchId, pitch => pitch.Id, (booking, pitch) => new { Booking = booking, Pitch = pitch })
            .Join(
                dbContext.Clubs,
                joined => joined.Pitch.ClubId,
                club => club.Id,
                (joined, club) => new Row
                {
                    Id = joined.Booking.Id,
                    ClubName = club.Name,
                    ClubSlug = club.Slug,
                    PitchName = joined.Pitch.Name,
                    Sport = joined.Pitch.Sport,
                    StartsAt = joined.Booking.StartsAt,
                    EndsAt = joined.Booking.EndsAt,
                    Price = joined.Booking.Price,
                    Status = joined.Booking.Status,
                    WindowHours = club.CancellationWindowHours,
                    SeriesId = joined.Booking.SeriesId,
                    SeriesPosition = joined.Booking.SeriesId == null
                        ? 0
                        : dbContext.Bookings.Count(other => other.SeriesId == joined.Booking.SeriesId && other.StartsAt <= joined.Booking.StartsAt),
                    SeriesCount = joined.Booking.SeriesId == null ? 0 : dbContext.Bookings.Count(other => other.SeriesId == joined.Booking.SeriesId),
                });
    }

    private static MyBookingResponse ToResponse(Row row, DateTime now)
    {
        var window = TimeSpan.FromHours(row.WindowHours);
        return new MyBookingResponse(
            row.Id,
            row.ClubName,
            row.ClubSlug,
            row.PitchName,
            Sports.ToCode(row.Sport),
            row.StartsAt,
            row.EndsAt,
            row.Price,
            BookingStatuses.ToCode(row.Status),
            Booking.CanBeCancelledByPlayer(row.Status, row.StartsAt, window, now),
            row.Status == BookingStatus.Confirmed ? Booking.CancellationDeadline(row.StartsAt, window) : null,
            row.SeriesId is { } seriesId ? new MyBookingSeries(seriesId, row.SeriesPosition, row.SeriesCount) : null);
    }
}
