using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Clubs;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Bookings;
using SportsBooking.Domain.Pitches;

namespace SportsBooking.Application.Pitches;

/// <summary>A pitch as its owner sees it. <see cref="Sport"/> is a stable code; <see cref="Status"/> is "active" or "inactive".</summary>
public sealed record PitchResponse(Guid Id, Guid ClubId, string Name, string Sport, decimal PricePerSlot, string Status)
{
    public static PitchResponse From(Pitch pitch) => new(
        pitch.Id,
        pitch.ClubId,
        pitch.Name,
        Sports.ToCode(pitch.Sport),
        pitch.PricePerSlot,
        pitch.IsActive ? "active" : "inactive");
}

public static class PitchErrors
{
    /// <summary>A pitch that does not exist in this club (also: a pitch that belongs to another club).</summary>
    public static ServiceError NotFound { get; } = new(
        ErrorKind.NotFound,
        ErrorCodes.PitchNotFound,
        "Campo não encontrado",
        "Não encontrámos este campo.");

    public static ServiceError NameTaken { get; } = ServiceError.Validation(
        new Dictionary<string, string[]> { ["name"] = [NameTakenMessage] });

    public const string NameTakenMessage = "Já existe um campo com este nome neste clube.";

    /// <summary>US07: the pitch has confirmed bookings that have not ended; <c>count</c> says how many.</summary>
    public static ServiceError HasFutureBookings(int count) => new(
        ErrorKind.Unprocessable,
        ErrorCodes.PitchHasFutureBookings,
        "Campo com reservas",
        count == 1
            ? "Este campo tem 1 reserva futura confirmada. Cancela-a antes de o desativar."
            : $"Este campo tem {count} reservas futuras confirmadas. Cancela-as antes de o desativar.",
        Extensions: new Dictionary<string, object?> { ["count"] = count });
}

/// <summary>
/// US07: an owner adds, lists, reads, edits, deactivates and reactivates the pitches of their clubs. The club is
/// reached through <see cref="OwnedPitches"/> (T02), so another owner's club is a 404 <c>club_not_found</c>
/// whatever the request holds; a pitch asked for through a club that is not its own is a 404 <c>pitch_not_found</c>.
/// </summary>
public sealed class PitchService(
    OwnedPitches ownedPitches,
    IPitchStore pitchStore,
    IPitchLock pitchLock,
    IAppDbContext dbContext,
    PitchRequestValidator requestValidator,
    IValidator<PageRequest> pageValidator,
    TimeProvider timeProvider)
{
    public async Task<Result<PitchResponse>> CreateAsync(Guid clubId, PitchRequest request, CancellationToken cancellationToken)
    {
        if (!await ownedPitches.ClubIsOwnedAsync(clubId, cancellationToken))
        {
            return ClubErrors.NotFound;
        }

        var normalized = request.Normalized();
        var invalid = await ValidateAsync(clubId, pitchId: null, normalized, cancellationToken);
        if (invalid is not null)
        {
            return invalid;
        }

        var pitch = Pitch.Create(clubId, normalized.Details(), Now());
        dbContext.Pitches.Add(pitch);
        return await pitchStore.TrySaveAsync(cancellationToken) ? PitchResponse.From(pitch) : PitchErrors.NameTaken;
    }

    /// <summary>The club's pitches, active and inactive, by name ignoring case (then name and id), one page at a time.</summary>
    public async Task<Result<PagedResponse<PitchResponse>>> ListAsync(Guid clubId, PageRequest page, CancellationToken cancellationToken)
    {
        if (!await ownedPitches.ClubIsOwnedAsync(clubId, cancellationToken))
        {
            return ClubErrors.NotFound;
        }

        var validation = await pageValidator.ValidateAsync(page, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var pitches = ownedPitches.Of(clubId).AsNoTracking();
        var total = await pitches.CountAsync(cancellationToken);
        var items = await pitches
            .OrderBy(pitch => pitch.NameKey)
            .ThenBy(pitch => pitch.Name)
            .ThenBy(pitch => pitch.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResponse<PitchResponse>(items.Select(PitchResponse.From).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<Result<PitchResponse>> GetAsync(Guid clubId, Guid pitchId, CancellationToken cancellationToken)
    {
        var found = await ownedPitches.FindAsync(clubId, pitchId, tracked: false, cancellationToken);
        return found.IsSuccess ? PitchResponse.From(found.Value) : found.Error!;
    }

    /// <summary>Replaces name, sport and price. A new price applies to bookings made from now on, never to existing ones.</summary>
    public async Task<Result<PitchResponse>> UpdateAsync(Guid clubId, Guid pitchId, PitchRequest request, CancellationToken cancellationToken)
    {
        var found = await ownedPitches.FindAsync(clubId, pitchId, tracked: true, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        var normalized = request.Normalized();
        var invalid = await ValidateAsync(clubId, pitchId, normalized, cancellationToken);
        if (invalid is not null)
        {
            return invalid;
        }

        var pitch = found.Value;
        pitch.UpdateDetails(normalized.Details(), Now());
        return await pitchStore.TrySaveAsync(cancellationToken) ? PitchResponse.From(pitch) : PitchErrors.NameTaken;
    }

    /// <summary>
    /// Takes the pitch out of search and bookings; it stays in the history and can be reactivated. Doing it again is
    /// fine. Refusing when there are future confirmed bookings comes with the bookings (US18, docs/backlog.md).
    /// </summary>
    public async Task<Result<PitchResponse>> DeactivateAsync(Guid clubId, Guid pitchId, CancellationToken cancellationToken)
    {
        var found = await ownedPitches.FindAsync(clubId, pitchId, tracked: true, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        // Under the pitch lock, so a booking cannot slip in between the count and the deactivation.
        await using var scope = await pitchLock.LockAsync(pitchId, cancellationToken);
        var now = Now();
        var future = await dbContext.Bookings.CountAsync(
            booking => booking.PitchId == pitchId && booking.Status == BookingStatus.Confirmed && booking.EndsAt > now,
            cancellationToken);
        if (future > 0)
        {
            return PitchErrors.HasFutureBookings(future);
        }

        found.Value.Deactivate(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return PitchResponse.From(found.Value);
    }

    public async Task<Result<PitchResponse>> ReactivateAsync(Guid clubId, Guid pitchId, CancellationToken cancellationToken)
    {
        var found = await ownedPitches.FindAsync(clubId, pitchId, tracked: true, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        found.Value.Reactivate(Now());
        await dbContext.SaveChangesAsync(cancellationToken);
        return PitchResponse.From(found.Value);
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>Field rules plus "no other pitch of this club has this name, ignoring case".</summary>
    private async Task<ServiceError?> ValidateAsync(Guid clubId, Guid? pitchId, PitchRequest normalized, CancellationToken cancellationToken)
    {
        var validation = await requestValidator.ValidateAsync(normalized, cancellationToken);
        var errors = validation.IsValid
            ? new Dictionary<string, string[]>()
            : validation.ToFieldErrors().ToDictionary(pair => pair.Key, pair => pair.Value);

        if (!errors.ContainsKey("name"))
        {
            var key = PitchRules.NameKey(normalized.Name);
            var taken = await dbContext.Pitches.AsNoTracking()
                .AnyAsync(pitch => pitch.ClubId == clubId && pitch.NameKey == key && pitch.Id != pitchId, cancellationToken);
            if (taken)
            {
                errors["name"] = [PitchErrors.NameTakenMessage];
            }
        }

        return errors.Count == 0 ? null : ServiceError.Validation(errors);
    }
}
