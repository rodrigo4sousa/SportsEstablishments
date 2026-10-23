using FluentValidation;
using SportsBooking.Application.Auth;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Application.Accounts;

public sealed record RegisteredAccount(AccountResponse Account, SessionTokens Session);

/// <summary>
/// US01 and US02: creates a player or an owner account and starts its session, so the user is signed in right away.
/// The profile is chosen by the caller (one method per endpoint), never by the request.
/// </summary>
public sealed class AccountRegistrationService(
    IValidator<RegisterAccountRequest> validator,
    IAccountStore accounts,
    ISessionService sessions)
{
    public Task<Result<RegisteredAccount>> RegisterPlayerAsync(RegisterAccountRequest request, CancellationToken cancellationToken) =>
        RegisterAsync(request, Profiles.Player, cancellationToken);

    public Task<Result<RegisteredAccount>> RegisterOwnerAsync(RegisterAccountRequest request, CancellationToken cancellationToken) =>
        RegisterAsync(request, Profiles.Owner, cancellationToken);

    private async Task<Result<RegisteredAccount>> RegisterAsync(RegisterAccountRequest request, string profile, CancellationToken cancellationToken)
    {
        var normalized = request.Normalized();
        var validation = await validator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var account = await accounts.CreateAsync(
            new NewAccount(normalized.Name, normalized.Email, normalized.Password, normalized.Phone),
            profile,
            cancellationToken);
        if (account is null)
        {
            return AccountErrors.EmailTaken;
        }

        var session = await sessions.StartAsync(account.Id, profile, cancellationToken);
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
