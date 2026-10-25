namespace SportsBooking.Application.Admin;

/// <summary>
/// An account as the admin's counts see it: its profile, when it registered and whether it is deactivated. It has no name,
/// no email, no phone: counting players never needs them, and what is not in the type cannot leak (US24, US26, US28).
/// </summary>
public sealed class AccountProfile
{
    public required Guid Id { get; init; }

    /// <summary><c>Player</c>, <c>Owner</c> or <c>Admin</c> (<see cref="Domain.Accounts.Profiles"/>).</summary>
    public required string Profile { get; init; }

    public required DateTime RegisteredAt { get; init; }

    public required bool IsDeactivated { get; init; }
}

/// <summary>An owner's account for the admin's list (US26). Owners' names and emails are shown; players' never are.</summary>
public sealed class OwnerAccount
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Email { get; init; }

    public required DateTime RegisteredAt { get; init; }

    public required bool IsDeactivated { get; init; }
}
