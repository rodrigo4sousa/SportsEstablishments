using FluentValidation;
using SportsBooking.Application.Auth;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Application.Accounts;

public sealed record RegisteredAccount(AccountResponse Account, SessionTokens Session);

/// <summary>US01: creates a player account and starts its session, so the player is signed in right away.</summary>
public sealed class PlayerRegistrationService(
    IValidator<RegisterPlayerRequest> validator,
    IAccountStore accounts,
    ISessionService sessions)
{
    public async Task<Result<RegisteredAccount>> RegisterAsync(RegisterPlayerRequest request, CancellationToken cancellationToken)
    {
        var normalized = request.Normalized();
        var validation = await validator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var account = await accounts.CreatePlayerAsync(
            new NewAccount(normalized.Name, normalized.Email, normalized.Password, normalized.Phone),
            cancellationToken);
        if (account is null)
        {
            return AccountErrors.EmailTaken;
        }

        var session = await sessions.StartAsync(account.Id, Profiles.Player, cancellationToken);
        return new RegisteredAccount(account, session);
    }
}

public static class AccountErrors
{
    /// <summary>
    /// US01 requires saying that the email is taken. The message is the same whatever the existing account is
    /// (player, owner or admin) and says nothing else about it.
    /// </summary>
    public static ServiceError EmailTaken { get; } = new(
        ErrorKind.Conflict,
        ErrorCodes.EmailTaken,
        "Email já registado",
        "Já existe uma conta com este email.");
}
