using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Clubs;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Bookings;
using SportsBooking.Domain.Pitches;

namespace SportsBooking.Application.Accounts;

/// <summary>US30: the file a person downloads with their own data. Only what is theirs: no password hash, token, internal id or other person's data.</summary>
public sealed record AccountDataExport(
    DateTime ExportedAt,
    ExportedAccount Account,
    ExportedLegalAcceptance LegalAcceptance,
    IReadOnlyList<ExportedBooking> Bookings);

public sealed record ExportedAccount(string Name, string Email, string? Phone, string Profile, DateTime RegisteredAt);

/// <summary>Both empty for an account made before the acceptance was recorded (T07).</summary>
public sealed record ExportedLegalAcceptance(string? Version, DateTime? AcceptedAt);

public sealed record ExportedBooking(
    string ClubName,
    string PitchName,
    string Sport,
    DateTime StartsAt,
    DateTime EndsAt,
    decimal Price,
    string Status,
    DateTime BookedAt,
    DateTime? CancelledAt,
    string? CancellationReason);

/// <summary>What only the accounts table knows about a person, for the export.</summary>
public sealed record AccountPersonalData(string Name, string Email, string? Phone, string Profile, DateTime RegisteredAt, string? AcceptedTermsVersion, DateTime? TermsAcceptedAt);

public sealed record DeleteAccountRequest(string Password)
{
    /// <summary>A missing value counts as empty. The password is kept exactly as typed.</summary>
    public DeleteAccountRequest Normalized() => new(Password ?? string.Empty);
}

public sealed class DeleteAccountRequestValidator : AbstractValidator<DeleteAccountRequest>
{
    public DeleteAccountRequestValidator()
    {
        // Not NotEmpty(): that also rejects whitespace, and a password of spaces is a valid password (US01).
        RuleFor(request => request.Password).Must(password => password.Length > 0).WithMessage("Indica a palavra-passe.");
    }
}

public enum AccountDeletionOutcome
{
    Deleted,

    /// <summary>The owner has a club (it may have been created a moment ago): a club cannot stay without its owner.</summary>
    OwnerHasClubs,

    /// <summary>The account does not exist, or was deleted by another request at the same moment.</summary>
    NotFound,
}

/// <summary>US30, the part that depends on Identity and on the database, implemented in Infrastructure.</summary>
public interface IAccountPrivacyStore
{
    Task<AccountPersonalData?> ReadPersonalDataAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>The account's current password, after the same hashing work in every case. A wrong one counts towards the lockout, as at login.</summary>
    Task<bool> VerifyPasswordAsync(Guid accountId, string password, CancellationToken cancellationToken);

    /// <summary>In one transaction: cancels the future bookings, anonymizes the account, ends every session and removes its pending emails.</summary>
    Task<AccountDeletionOutcome> DeleteAsync(Guid accountId, DateTime now, CancellationToken cancellationToken);
}

/// <summary>US30: the signed-in person's own data, as a file (GDPR access and portability).</summary>
public sealed class AccountExportService(ICurrentUser currentUser, IAccountPrivacyStore store, IAppDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<AccountDataExport>> ExportAsync(CancellationToken cancellationToken)
    {
        var accountId = currentUser.UserId ?? throw new InvalidOperationException("The export requires a session.");
        var data = await store.ReadPersonalDataAsync(accountId, cancellationToken);
        if (data is null)
        {
            return AccountPrivacyErrors.AccountNotFound;
        }

        var rows = await (
                from booking in dbContext.Bookings.AsNoTracking()
                where booking.PlayerId == accountId
                join pitch in dbContext.Pitches.AsNoTracking() on booking.PitchId equals pitch.Id
                join club in dbContext.Clubs.AsNoTracking() on pitch.ClubId equals club.Id
                orderby booking.StartsAt, booking.Id
                select new { ClubName = club.Name, PitchName = pitch.Name, pitch.Sport, booking.StartsAt, booking.EndsAt, booking.Price, booking.Status, booking.CreatedAt, booking.CancelledAt, booking.CancellationReason })
            .ToListAsync(cancellationToken);

        return new AccountDataExport(
            timeProvider.GetUtcNow().UtcDateTime,
            new ExportedAccount(data.Name, data.Email, data.Phone, data.Profile, Utc(data.RegisteredAt)),
            new ExportedLegalAcceptance(data.AcceptedTermsVersion, data.TermsAcceptedAt is { } acceptedAt ? Utc(acceptedAt) : null),
            rows.Select(row => new ExportedBooking(
                    row.ClubName,
                    row.PitchName,
                    Sports.ToCode(row.Sport),
                    Utc(row.StartsAt),
                    Utc(row.EndsAt),
                    row.Price,
                    BookingStatuses.ToCode(row.Status),
                    Utc(row.CreatedAt),
                    row.CancelledAt is { } cancelledAt ? Utc(cancelledAt) : null,
                    row.CancellationReason))
                .ToList());
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

/// <summary>
/// US30: deletes the signed-in account by anonymizing it (provisional, docs/backlog.md). Admins cannot delete themselves, and neither
/// can an owner with clubs, because a club always has an owner. The refusals come before the password: they are about the account, which
/// the session already identifies, and the person should not have to type a password to learn them.
/// </summary>
public sealed class AccountDeletionService(
    ICurrentUser currentUser,
    IValidator<DeleteAccountRequest> validator,
    IAccountStore accounts,
    OwnedClubs ownedClubs,
    IAccountPrivacyStore store,
    TimeProvider timeProvider)
{
    public async Task<Result<bool>> DeleteAsync(DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        var accountId = currentUser.UserId ?? throw new InvalidOperationException("Deleting an account requires a session.");
        var normalized = request.Normalized();
        var validation = await validator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var account = await accounts.FindAsync(accountId, cancellationToken);
        if (account is null)
        {
            return AccountPrivacyErrors.AccountNotFound;
        }

        if (account.Profile == Profiles.Admin)
        {
            return AccountPrivacyErrors.AdminCannotDeleteAccount;
        }

        if (account.Profile == Profiles.Owner && await ownedClubs.All().AnyAsync(cancellationToken))
        {
            return AccountPrivacyErrors.OwnerHasClubs;
        }

        if (!await store.VerifyPasswordAsync(accountId, normalized.Password, cancellationToken))
        {
            return AccountPrivacyErrors.InvalidPassword;
        }

        return await store.DeleteAsync(accountId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken) switch
        {
            AccountDeletionOutcome.Deleted => true,
            AccountDeletionOutcome.OwnerHasClubs => AccountPrivacyErrors.OwnerHasClubs,
            _ => AccountPrivacyErrors.AccountNotFound,
        };
    }
}

public static class AccountPrivacyErrors
{
    /// <summary>A session whose account is gone: the same as having no session.</summary>
    public static readonly ServiceError AccountNotFound = new(
        ErrorKind.Unauthorized, ErrorCodes.Unauthorized, "Sessão inválida", "A sessão já não é válida.");

    /// <summary>Not a 401: to the browser client a 401 means an expired session, which it would try to renew.</summary>
    public static readonly ServiceError InvalidPassword = new(
        ErrorKind.Validation,
        ErrorCodes.InvalidPassword,
        "Palavra-passe incorreta",
        "A palavra-passe está incorreta.",
        new Dictionary<string, string[]> { ["password"] = ["A palavra-passe está incorreta."] });

    public static readonly ServiceError OwnerHasClubs = new(
        ErrorKind.Unprocessable,
        ErrorCodes.OwnerHasClubs,
        "Conta com clubes",
        "Uma conta com clubes não pode ser apagada aqui. Contacta a administração.");

    public static readonly ServiceError AdminCannotDeleteAccount = new(
        ErrorKind.Unprocessable,
        ErrorCodes.AdminCannotDeleteAccount,
        "Conta de administração",
        "A conta de administração não pode ser apagada.");
}
