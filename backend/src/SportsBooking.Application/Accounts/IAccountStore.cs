namespace SportsBooking.Application.Accounts;

/// <summary>Account persistence, implemented on ASP.NET Core Identity in Infrastructure.</summary>
public interface IAccountStore
{
    Task<AccountResponse?> FindAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>The signed-in account, as returned by <c>GET /api/auth/me</c>. <see cref="Profile"/> is Player, Owner or Admin.</summary>
public sealed record AccountResponse(Guid Id, string Name, string Email, string Profile);
