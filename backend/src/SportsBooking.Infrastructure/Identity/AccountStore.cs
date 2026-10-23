using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SportsBooking.Application.Accounts;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.Infrastructure.Identity;

internal sealed class AccountStore(UserManager<AppUser> userManager, AppDbContext dbContext) : IAccountStore
{
    // The email is also the user name, so a taken email can show up as either error.
    private static readonly HashSet<string> EmailTakenErrors =
    [
        nameof(IdentityErrorDescriber.DuplicateEmail),
        nameof(IdentityErrorDescriber.DuplicateUserName),
    ];

    public async Task<AccountResponse?> CreatePlayerAsync(NewAccount account, CancellationToken cancellationToken)
    {
        var user = new AppUser(account.Email, account.Name) { PhoneNumber = account.Phone };

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

            var profileAssigned = await userManager.AddToRoleAsync(user, Profiles.Player);
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

        return new AccountResponse(user.Id, user.Name, user.Email!, Profiles.Player);
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
                    .First()))
            .SingleOrDefaultAsync(cancellationToken);

    // Validation already passed, so any other Identity error is a bug, not an expected failure.
    private static InvalidOperationException UnexpectedFailure(IdentityResult result) =>
        new($"Account creation failed: {string.Join(", ", result.Errors.Select(error => error.Code))}");
}
