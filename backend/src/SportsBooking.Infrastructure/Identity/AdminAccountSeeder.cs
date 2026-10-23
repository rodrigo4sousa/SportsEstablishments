using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.Infrastructure.Identity;

/// <summary>
/// Creates the platform admin account at startup (US04). This is the only code path that gives the Admin profile.
/// It never promotes an existing account, never creates a second admin and never changes an existing admin.
/// Logs never contain the configured email or password.
/// </summary>
public sealed partial class AdminAccountSeeder(
    UserManager<AppUser> userManager,
    AppDbContext dbContext,
    IOptions<AdminSeedOptions> options,
    ILogger<AdminAccountSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // Partial or invalid settings never get here: AdminSeedOptionsValidator stops the API at startup.
        var email = options.Value.Email;
        var password = options.Value.Password;

        if (AdminSeedSettings.Evaluate(email, password) == AdminSeedConfiguration.NotConfigured)
        {
            LogNotConfigured(logger);
            return;
        }

        var existingAdmins = await userManager.GetUsersInRoleAsync(Profiles.Admin);
        if (existingAdmins.Count > 0)
        {
            var normalizedEmail = userManager.NormalizeEmail(email);
            if (existingAdmins.Any(admin => admin.NormalizedEmail == normalizedEmail))
            {
                LogAlreadyExists(logger);
            }
            else
            {
                LogOtherAdminExists(logger);
            }

            return;
        }

        if (await userManager.FindByEmailAsync(email!) is not null)
        {
            LogEmailUsedByAnotherAccount(logger);
            return;
        }

        await CreateAdminAsync(email!, password!, cancellationToken);
    }

    private async Task CreateAdminAsync(string email, string password, CancellationToken cancellationToken)
    {
        var admin = new AppUser(email) { EmailConfirmed = true };

        // Account and profile are saved together, so a failure never leaves an account without its profile.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var created = await userManager.CreateAsync(admin, password);
            if (!created.Succeeded)
            {
                LogCreateFailed(logger, string.Join(", ", created.Errors.Select(error => error.Code)));
                return;
            }

            var profileAssigned = await userManager.AddToRoleAsync(admin, Profiles.Admin);
            if (!profileAssigned.Succeeded)
            {
                LogCreateFailed(logger, string.Join(", ", profileAssigned.Errors.Select(error => error.Code)));
                return;
            }

            await transaction.CommitAsync(cancellationToken);
            LogCreated(logger, admin.Id);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another instance created the same account at the same time.
            LogCreatedConcurrently(logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Admin seed skipped: AdminSeed:Email and AdminSeed:Password are not set. A new installation has no admin until they are.")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin account already exists; nothing to seed.")]
    private static partial void LogAlreadyExists(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Admin account not seeded: an admin with a different email already exists.")]
    private static partial void LogOtherAdminExists(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Admin account not seeded: the configured email belongs to an existing non-admin account, which is never promoted.")]
    private static partial void LogEmailUsedByAnotherAccount(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Admin account not seeded: account creation failed ({ErrorCodes}).")]
    private static partial void LogCreateFailed(ILogger logger, string errorCodes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin account was created concurrently by another instance; nothing to seed.")]
    private static partial void LogCreatedConcurrently(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin account {AdminId} created from AdminSeed settings.")]
    private static partial void LogCreated(ILogger logger, Guid adminId);
}
