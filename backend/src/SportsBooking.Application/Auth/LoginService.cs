using FluentValidation;
using SportsBooking.Application.Accounts;
using SportsBooking.Application.Common;

namespace SportsBooking.Application.Auth;

public sealed record SignedInAccount(AccountResponse Account, SessionTokens Session);

/// <summary>US03: checks the credentials and starts a session for any profile (player, owner or admin).</summary>
public sealed class LoginService(
    IValidator<LoginRequest> validator,
    IAccountStore accounts,
    ISessionService sessions)
{
    public async Task<Result<SignedInAccount>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalized = request.Normalized();
        var validation = await validator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var account = await accounts.AuthenticateAsync(normalized.Email, normalized.Password, cancellationToken);
        if (account is null)
        {
            return AuthErrors.InvalidCredentials;
        }

        var session = await sessions.StartAsync(account.Id, account.Profile, cancellationToken);
        return new SignedInAccount(account, session);
    }
}
