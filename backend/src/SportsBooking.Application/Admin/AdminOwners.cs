using System.Linq.Expressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Bookings;

namespace SportsBooking.Application.Admin;

/// <summary>One owner of the admin's list (US26), with counts only about the rest of the platform: never a player.</summary>
public sealed record AdminOwnerResponse(
    Guid Id,
    string Name,
    string Email,
    DateTime RegisteredAt,
    bool Deactivated,
    int Clubs,
    int Pitches,
    int Bookings,
    int Players);

/// <summary>The query string of <c>GET /api/admin/owners</c>.</summary>
public sealed record AdminOwnersRequest(string? Search, string? Sort, string? Direction, int Page = 1)
{
    public const int PageSize = 20;

    public const int MaximumPage = 10_000;

    public const int SearchMaximumLength = 100;

    public const string DefaultSort = "name";

    /// <summary>The columns the list can be sorted by.</summary>
    public static readonly string[] SortColumns = ["name", "email", "registeredAt", "status", "clubs", "pitches", "bookings", "players"];

    public AdminOwnersRequest Normalized() => this with
    {
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
        Sort = string.IsNullOrWhiteSpace(Sort) ? DefaultSort : Sort.Trim(),
        Direction = string.IsNullOrWhiteSpace(Direction) ? "asc" : Direction.Trim().ToLowerInvariant(),
    };
}

public sealed class AdminOwnersRequestValidator : AbstractValidator<AdminOwnersRequest>
{
    public AdminOwnersRequestValidator()
    {
        RuleFor(request => request.Search).MaximumLength(AdminOwnersRequest.SearchMaximumLength)
            .WithMessage($"A pesquisa pode ter no máximo {AdminOwnersRequest.SearchMaximumLength} caracteres.");
        RuleFor(request => request.Sort).Must(sort => sort is not null && AdminOwnersRequest.SortColumns.Contains(sort, StringComparer.Ordinal))
            .WithMessage($"Ordena por uma destas colunas: {string.Join(", ", AdminOwnersRequest.SortColumns)}.");
        RuleFor(request => request.Direction).Must(direction => direction is "asc" or "desc")
            .WithMessage("Indica a direção: «asc» ou «desc».");
        RuleFor(request => request.Page).InclusiveBetween(1, AdminOwnersRequest.MaximumPage)
            .WithMessage($"A página tem de estar entre 1 e {AdminOwnersRequest.MaximumPage}.");
    }
}

/// <summary>
/// US26: the list of owners with their numbers, 20 to a page. Every number is worked out by the database for the owners of the
/// list: clubs, pitches (all of them, active or not), bookings (not cancelled, a no-show is not a cancellation, customer
/// bookings included) and the distinct players who booked at their clubs (<c>COUNT(DISTINCT player_id)</c> over those same
/// bookings, so a customer booking, which has no player, counts for nobody). All provisional. Sortable by every column, with
/// the owner's id as the last tie-break so the order is always the same; the search finds a part of the name or the email,
/// ignoring case.
/// </summary>
public sealed class AdminOwnersService(IAppDbContext dbContext, AdminOwnersRequestValidator validator)
{
    private sealed class Row
    {
        public required Guid Id { get; init; }

        public required string Name { get; init; }

        public required string Email { get; init; }

        public required DateTime RegisteredAt { get; init; }

        public required bool Deactivated { get; init; }

        public required int Clubs { get; init; }

        public required int Pitches { get; init; }

        public required int Bookings { get; init; }

        public required int Players { get; init; }
    }

    public async Task<Result<PagedResponse<AdminOwnerResponse>>> ListAsync(AdminOwnersRequest request, CancellationToken cancellationToken)
    {
        var normalized = request.Normalized();
        var validation = await validator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var owners = dbContext.OwnerAccounts.AsNoTracking();
        if (normalized.Search is { } search)
        {
            var term = search.ToLowerInvariant();
#pragma warning disable CA1304, CA1311, CA1862 // EF translates ToLower()/Contains() to lower()/strpos in SQL; the string is never lowered in .NET.
            owners = owners.Where(owner => owner.Name.ToLower().Contains(term) || owner.Email.ToLower().Contains(term));
#pragma warning restore CA1304, CA1311, CA1862
        }

        var rows = owners.Select(owner => new Row
        {
            Id = owner.Id,
            Name = owner.Name,
            Email = owner.Email,
            RegisteredAt = owner.RegisteredAt,
            Deactivated = owner.IsDeactivated,
            Clubs = dbContext.Clubs.Count(club => club.OwnerId == owner.Id),
            Pitches = dbContext.Pitches.Count(pitch => dbContext.Clubs.Any(club => club.Id == pitch.ClubId && club.OwnerId == owner.Id)),
            Bookings = dbContext.Bookings.Count(booking =>
                booking.Status != BookingStatus.CancelledByPlayer && booking.Status != BookingStatus.CancelledByClub
                && dbContext.Pitches.Any(pitch => pitch.Id == booking.PitchId && dbContext.Clubs.Any(club => club.Id == pitch.ClubId && club.OwnerId == owner.Id))),
            Players = dbContext.Bookings
                .Where(booking => booking.PlayerId != null && booking.Status != BookingStatus.CancelledByPlayer && booking.Status != BookingStatus.CancelledByClub
                    && dbContext.Pitches.Any(pitch => pitch.Id == booking.PitchId && dbContext.Clubs.Any(club => club.Id == pitch.ClubId && club.OwnerId == owner.Id)))
                .Select(booking => booking.PlayerId)
                .Distinct()
                .Count(),
        });

        var total = await rows.CountAsync(cancellationToken);
        var items = await Sorted(rows, normalized.Sort!, descending: normalized.Direction == "desc")
            .Skip((normalized.Page - 1) * AdminOwnersRequest.PageSize)
            .Take(AdminOwnersRequest.PageSize)
            .Select(row => new AdminOwnerResponse(row.Id, row.Name, row.Email, row.RegisteredAt, row.Deactivated, row.Clubs, row.Pitches, row.Bookings, row.Players))
            .ToListAsync(cancellationToken);
        return new PagedResponse<AdminOwnerResponse>(items, normalized.Page, AdminOwnersRequest.PageSize, total);
    }

    /// <summary>The chosen column, then the owner's id: two owners with the same value always come in the same order.</summary>
    private static IQueryable<Row> Sorted(IQueryable<Row> rows, string sort, bool descending)
    {
        IOrderedQueryable<Row> By<TKey>(Expression<Func<Row, TKey>> key) => descending ? rows.OrderByDescending(key) : rows.OrderBy(key);

#pragma warning disable CA1304, CA1311 // EF translates ToLower() to lower() in SQL, so the order ignores case.
        var ordered = sort switch
        {
            "email" => By(row => row.Email.ToLower()),
            "registeredAt" => By(row => row.RegisteredAt),
            "status" => By(row => row.Deactivated),
            "clubs" => By(row => row.Clubs),
            "pitches" => By(row => row.Pitches),
            "bookings" => By(row => row.Bookings),
            "players" => By(row => row.Players),
            _ => By(row => row.Name.ToLower()),
        };
#pragma warning restore CA1304, CA1311
        return ordered.ThenBy(row => row.Id);
    }
}
