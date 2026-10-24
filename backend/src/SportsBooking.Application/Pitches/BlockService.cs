using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Bookings;
using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Pitches;

/// <summary>A block as its owner sees it: UTC instants (the owner area shows them in Lisbon time).</summary>
public sealed record BlockResponse(Guid Id, Guid PitchId, DateTime StartsAt, DateTime EndsAt, string? Reason)
{
    public static readonly System.Linq.Expressions.Expression<Func<Block, BlockResponse>> Projection =
        block => new BlockResponse(block.Id, block.PitchId, block.StartsAt, block.EndsAt, block.Reason);

    private static readonly Func<Block, BlockResponse> FromBlock = Projection.Compile();

    public static BlockResponse From(Block block) => FromBlock(block);
}

/// <summary>A confirmed booking that stands in the way of a block (US09).</summary>
public sealed record OverlappingBooking(Guid Id, DateTime StartsAt, DateTime EndsAt);

public static class BlockErrors
{
    public static ServiceError NotFound { get; } = new(
        ErrorKind.NotFound,
        ErrorCodes.BlockNotFound,
        "Bloqueio não encontrado",
        "Não encontrámos este bloqueio.");

    /// <summary>Provisional (US09): any overlap with another block of the pitch is refused.</summary>
    public static ServiceError Overlap { get; } = new(
        ErrorKind.Conflict,
        ErrorCodes.BlockOverlap,
        "Bloqueio sobreposto",
        "Este período sobrepõe-se a outro bloqueio deste campo.");

    /// <summary>
    /// US09: confirmed bookings overlap the block. The detail lists their times in the club's local time; the
    /// <c>bookings</c> extension has their ids and UTC instants, so the owner can cancel them (US12) and try again.
    /// </summary>
    public static ServiceError OverlapsBookings(IReadOnlyList<OverlappingBooking> bookings, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(bookings);
        var times = string.Join(", ", bookings.Select(booking => $"{Local(booking.StartsAt, timeZone)}–{Local(booking.EndsAt, timeZone)[11..]}"));
        return new ServiceError(
            ErrorKind.Conflict,
            ErrorCodes.BlockOverlapsBookings,
            "Bloqueio sobre reservas",
            $"Há reservas confirmadas neste período: {times}. Cancela-as primeiro ou escolhe outro período.",
            Extensions: new Dictionary<string, object?> { ["bookings"] = bookings });
    }

    private static string Local(DateTime utc, TimeZoneInfo timeZone) =>
        ClubTime.ToLocal(utc, timeZone).ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    public static ServiceError AlreadyStarted { get; } = new(
        ErrorKind.Unprocessable,
        ErrorCodes.BlockAlreadyStarted,
        "Bloqueio já começou",
        "Um bloqueio que já começou não pode ser removido.");
}

/// <summary>
/// US09: an owner blocks periods of a pitch, lists the upcoming ones and removes those that have not started. The
/// pitch is reached through <see cref="OwnedPitches"/> (T02): another owner's club is a 404 <c>club_not_found</c>, a
/// pitch outside its club a 404 <c>pitch_not_found</c>, before any validation.
/// </summary>
public sealed class BlockService(
    OwnedPitches ownedPitches,
    IPitchLock pitchLock,
    IAppDbContext dbContext,
    BlockCheckValidator validator,
    IValidator<PageRequest> pageValidator,
    TimeProvider timeProvider)
{
    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Creates a block. Everything that decides whether the period is free happens in one transaction that starts by
    /// locking the pitch row (<see cref="IPitchLock"/>): the overlap check with other blocks, and, when bookings exist
    /// (T03, US18), the check that no confirmed booking overlaps — it goes right next to the block check below, in the
    /// same place, so a booking and a block cannot both pass.
    /// </summary>
    public async Task<Result<BlockResponse>> CreateAsync(Guid clubId, Guid pitchId, BlockRequest request, CancellationToken cancellationToken)
    {
        var found = await ownedPitches.FindAsync(clubId, pitchId, tracked: false, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        var now = Now();
        var timeZone = await ownedPitches.TimeZoneOfAsync(clubId, cancellationToken);
        var check = new BlockCheck(request.Normalized(), now, timeZone);
        var validation = await validator.ValidateAsync(check, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var startsAt = check.StartUtc!.Value;
        var endsAt = check.EndUtc!.Value;

        await using var scope = await pitchLock.LockAsync(pitchId, cancellationToken);

        if (await dbContext.Blocks.AnyAsync(block => block.PitchId == pitchId && block.StartsAt < endsAt && startsAt < block.EndsAt, cancellationToken))
        {
            return BlockErrors.Overlap;
        }

        // US09 and T03: under the same lock, confirmed bookings of the pitch that overlap the period refuse it.
        var bookings = await dbContext.Bookings.AsNoTracking()
            .Where(booking => booking.PitchId == pitchId
                && booking.Status == BookingStatus.Confirmed
                && booking.StartsAt < endsAt
                && startsAt < booking.EndsAt)
            .OrderBy(booking => booking.StartsAt)
            .Select(booking => new OverlappingBooking(booking.Id, booking.StartsAt, booking.EndsAt))
            .ToListAsync(cancellationToken);
        if (bookings.Count > 0)
        {
            return BlockErrors.OverlapsBookings(bookings, timeZone);
        }

        var block = Block.Create(pitchId, startsAt, endsAt, check.Request.Reason, now);
        dbContext.Blocks.Add(block);
        await dbContext.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return BlockResponse.From(block);
    }

    /// <summary>The pitch's blocks that have not ended (ongoing and future), by start, one page at a time.</summary>
    public async Task<Result<PagedResponse<BlockResponse>>> ListAsync(Guid clubId, Guid pitchId, PageRequest page, CancellationToken cancellationToken)
    {
        var found = await ownedPitches.FindAsync(clubId, pitchId, tracked: false, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        var validation = await pageValidator.ValidateAsync(page, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var now = Now();
        var blocks = dbContext.Blocks.AsNoTracking().Where(block => block.PitchId == pitchId && block.EndsAt > now);
        var total = await blocks.CountAsync(cancellationToken);
        var items = await blocks
            .OrderBy(block => block.StartsAt)
            .ThenBy(block => block.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(BlockResponse.Projection)
            .ToListAsync(cancellationToken);
        return new PagedResponse<BlockResponse>(items, page.Page, page.PageSize, total);
    }

    /// <summary>Removes a block that has not started; the slots it covered are free again.</summary>
    public async Task<Result<bool>> DeleteAsync(Guid clubId, Guid pitchId, Guid blockId, CancellationToken cancellationToken)
    {
        var found = await ownedPitches.FindAsync(clubId, pitchId, tracked: false, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        var block = await dbContext.Blocks.SingleOrDefaultAsync(candidate => candidate.PitchId == pitchId && candidate.Id == blockId, cancellationToken);
        if (block is null)
        {
            return BlockErrors.NotFound;
        }

        if (block.HasStarted(Now()))
        {
            return BlockErrors.AlreadyStarted;
        }

        dbContext.Blocks.Remove(block);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
