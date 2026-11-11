using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SportsBooking.Application.Accounts;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.Infrastructure.Identity;

internal sealed class AccountStore(
    UserManager<AppUser> userManager,
    AppDbContext dbContext,
    FallbackPasswordHash fallbackHash,
    TimeProvider timeProvider) : IAccountStore
{
    // The email is also the user name, so a taken email can show up as either error.
    private static readonly HashSet<string> EmailTakenErrors =
    [
        nameof(IdentityErrorDescriber.DuplicateEmail),
        nameof(IdentityErrorDescriber.DuplicateUserName),
    ];

    public async Task<AccountResponse?> CreateAsync(NewAccount account, string profile, CancellationToken cancellationToken)
    {
        if (!Profiles.SelfRegistration.Contains(profile))
        {
            throw new ArgumentOutOfRangeException(nameof(profile), "Only the profiles in Profiles.SelfRegistration can be created by a registration.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var user = new AppUser(account.Email, account.Name, now) { PhoneNumber = account.Phone };
        user.RecordTermsAcceptance(account.AcceptedTermsVersion, now);
        user.ChangePreferredLanguage(account.PreferredLanguage);

        // Account and profile are saved together, so a failure never leaves an account without its profile.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Identity hashes the password before it checks the email, so both outcomes cost the same work.
            var created = await userManager.CreateAsync(user, account.Password);
            if (!created.Succeeded)
            {
                return created.Errors.Any(error => EmailTakenErrors.Contains(error.Code))
                    ? null
                    : throw UnexpectedFailure(created);
            }

            var profileAssigned = await userManager.AddToRoleAsync(user, profile);
            if (!profileAssigned.Succeeded)
            {
                throw UnexpectedFailure(profileAssigned);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: IdentityModelConfiguration.NormalizedEmailIndex,
        })
        {
            // Another registration with the same email won the race; the unique index decided.
            return null;
        }

        return new AccountResponse(user.Id, user.Name, user.Email!, profile, user.PreferredLanguage);
    }

    public async Task<AccountResponse?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user?.PasswordHash is null || !user.IsActive || await userManager.IsLockedOutAsync(user))
        {
            // No real check to make, but the same hashing work, so timing does not tell these cases apart.
            fallbackHash.Verify(password);
            return null;
        }

        // Also upgrades a hash made with fewer iterations (PasswordHasherOptions).
        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            return null;
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return await FindAsync(user.Id, cancellationToken);
    }

    public async Task<AccountResponse?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new AccountResponse(
                user.Id,
                user.Name,
                user.Email!,
                dbContext.UserRoles
                    .Where(userRole => userRole.UserId == user.Id)
                    .Join(dbContext.Roles, userRole => userRole.RoleId, role => role.Id, (_, role) => role.Name!)
                    .First(),
                user.PreferredLanguage))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.DeactivatedAt == null, cancellationToken);

    public Task<bool> IsDeletedAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.DeletedAt != null, cancellationToken);

    public async Task<bool> ChangePreferredLanguageAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        if (!PreferredLanguage.IsSupported(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code), "Not a supported language code.");
        }

        // A deleted account keeps no preference (US30): the conditional update leaves it as the deletion left it.
        return await dbContext.Users
            .Where(user => user.Id == userId && user.DeletedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.PreferredLanguage, code), cancellationToken) == 1;
    }

    // Validation already passed, so any other Identity error is a bug, not an expected failure.
    private static InvalidOperationException UnexpectedFailure(IdentityResult result) =>
        new($"Account creation failed: {string.Join(", ", result.Errors.Select(error => error.Code))}");
}
