namespace SportsBooking.Application.Pitches;

/// <summary>Pitch persistence that needs the database's own rules; implemented in Infrastructure.</summary>
public interface IPitchStore
{
    /// <summary>
    /// Saves the pending changes. Returns <c>false</c> when another request took the same name in the club between
    /// the check and the save (the unique index on the club and the name decides); nothing is saved then.
    /// </summary>
    Task<bool> TrySaveAsync(CancellationToken cancellationToken);
}
