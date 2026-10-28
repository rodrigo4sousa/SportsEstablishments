using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Pitches;

namespace SportsBooking.Application.Pitches;

public static class PriceBandErrors
{
    public static ServiceError NotFound { get; } = new(
        ErrorKind.NotFound,
        ErrorCodes.PriceBandNotFound,
        "Faixa de preço não encontrada",
        "Não encontrámos esta faixa de preço.");

    /// <summary>Overlap means a shared weekday and an intersecting time range; bands that only touch are fine.</summary>
    public static ServiceError Overlap { get; } = new(
        ErrorKind.Conflict,
        ErrorCodes.PriceBandOverlap,
        "Faixa sobreposta",
        "Esta faixa sobrepõe-se a outra faixa de preço deste campo num dos dias escolhidos.");

    public static ServiceError LimitReached { get; } = new(
        ErrorKind.Unprocessable,
        ErrorCodes.PriceBandLimitReached,
        "Demasiadas faixas",
        $"Um campo pode ter no máximo {PriceBandRules.MaximumPerPitch} faixas de preço.");

    public static ServiceError From(PriceBandOutcome outcome) => outcome switch
    {
        PriceBandOutcome.Overlaps => Overlap,
        PriceBandOutcome.LimitReached => LimitReached,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not an error."),
    };
}

/// <summary>
/// US10: the price bands of one pitch. The pitch is reached through <see cref="OwnedPitches"/> (T02), before any
/// validation. Changes run under the pitch lock (<see cref="IPitchLock"/>), the same one bookings take, and read the
/// pitch inside it: a booking is priced from the bands it sees under the lock, and two simultaneous changes cannot both
/// pass the overlap check. Existing bookings keep their stored price.
/// </summary>
public sealed class PriceBandService(
    OwnedPitches ownedPitches,
    IPitchLock pitchLock,
    IAppDbContext dbContext,
    PriceBandRequestValidator validator,
    TimeProvider timeProvider)
{
    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>The pitch's bands by start time. A pitch has few (at most 20), so all of them come at once.</summary>
    public async Task<Result<List<PriceBandResponse>>> ListAsync(Guid clubId, Guid pitchId, CancellationToken cancellationToken)
    {
        var found = await ownedPitches.FindAsync(clubId, pitchId, tracked: false, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        List<PriceBandResponse> bands = found.Value.PriceBands
            .OrderBy(band => band.Starts).ThenBy(band => band.DaysMask).ThenBy(band => band.Id)
            .Select(PriceBandResponse.From)
            .ToList();
        return bands;
    }

    public async Task<Result<PriceBandResponse>> CreateAsync(Guid clubId, Guid pitchId, PriceBandRequest request, CancellationToken cancellationToken)
    {
        var found = await ownedPitches.FindAsync(clubId, pitchId, tracked: false, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        var normalized = request.Normalized();
        var validation = await validator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        await using var scope = await pitchLock.LockAsync(pitchId, cancellationToken);
        var pitch = await ownedPitches.Of(clubId).SingleAsync(candidate => candidate.Id == pitchId, cancellationToken);
        var outcome = pitch.TryAddPriceBand(normalized.ToDetails(), Now(), out var band);
        if (outcome != PriceBandOutcome.Done)
        {
            return PriceBandErrors.From(outcome);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return PriceBandResponse.From(band!);
    }

    public async Task<Result<PriceBandResponse>> UpdateAsync(Guid clubId, Guid pitchId, Guid bandId, PriceBandRequest request, CancellationToken cancellationToken)
    {
        var found = await ownedPitches.FindAsync(clubId, pitchId, tracked: false, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        if (found.Value.PriceBands.All(candidate => candidate.Id != bandId))
        {
            return PriceBandErrors.NotFound;
        }

        var normalized = request.Normalized();
        var validation = await validator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        await using var scope = await pitchLock.LockAsync(pitchId, cancellationToken);
        var pitch = await ownedPitches.Of(clubId).SingleAsync(candidate => candidate.Id == pitchId, cancellationToken);
        var band = pitch.PriceBands.SingleOrDefault(candidate => candidate.Id == bandId);
        if (band is null)
        {
            return PriceBandErrors.NotFound;
        }

        var outcome = pitch.TryChangePriceBand(band, normalized.ToDetails(), Now());
        if (outcome != PriceBandOutcome.Done)
        {
            return PriceBandErrors.From(outcome);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return PriceBandResponse.From(band);
    }

    /// <summary>A real delete (provisional): a band is configuration, and bookings keep the price they were made at.</summary>
    public async Task<Result<bool>> DeleteAsync(Guid clubId, Guid pitchId, Guid bandId, CancellationToken cancellationToken)
    {
        var found = await ownedPitches.FindAsync(clubId, pitchId, tracked: false, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        await using var scope = await pitchLock.LockAsync(pitchId, cancellationToken);
        var pitch = await ownedPitches.Of(clubId).SingleAsync(candidate => candidate.Id == pitchId, cancellationToken);
        var band = pitch.PriceBands.SingleOrDefault(candidate => candidate.Id == bandId);
        if (band is null)
        {
            return PriceBandErrors.NotFound;
        }

        pitch.RemovePriceBand(band);
        dbContext.PriceBands.Remove(band);
        await dbContext.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return true;
    }
}
