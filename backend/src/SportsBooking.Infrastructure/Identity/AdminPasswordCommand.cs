using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.Infrastructure.Identity;

/// <summary>
/// The <c>set-admin-password</c> command of the API (US04): <c>dotnet run --project backend/src/SportsBooking.Api -- set-admin-password</c>.
/// It changes the admin's password, ends the admin's sessions and exits, without starting the server. The new password is read
/// from the environment variable <see cref="PasswordVariable"/>, never from the command line (an argument is visible in the
/// process list and the shell history), checked with the rules of US01, and never written anywhere: not to the output and not to
/// the log. The admin's email is not written either.
/// </summary>
public static class AdminPasswordCommand
{
    public const string Name = "set-admin-password";

    public const string PasswordVariable = "ADMIN_NEW_PASSWORD";

    /// <summary>True when the first argument is the command. Any other argument makes the command refuse, instead of running with it.</summary>
    public static bool IsRequested(IReadOnlyList<string> args) => args.Count > 0 && args[0] == Name;

    /// <summary>Returns the exit code: 0 done, 1 refused. <paramref name="readVariable"/> exists for tests; the program passes the environment.</summary>
    public static async Task<int> RunAsync(
        IServiceProvider services,
        IReadOnlyList<string> args,
        Func<string, string?> readVariable,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (args.Count != 1)
        {
            await error.WriteLineAsync($"Admin password not changed: the command takes no arguments. Put the new password in the {PasswordVariable} environment variable, never on the command line.");
            return 1;
        }

        var password = readVariable(PasswordVariable);
        if (string.IsNullOrEmpty(password))
        {
            await error.WriteLineAsync($"Admin password not changed: the {PasswordVariable} environment variable is not set.");
            return 1;
        }

        if (!PasswordPolicy.IsSatisfiedBy(password))
        {
            await error.WriteLineAsync($"Admin password not changed: the password needs at least {PasswordPolicy.MinimumLength} characters.");
            return 1;
        }

        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var admins = await userManager.GetUsersInRoleAsync(Profiles.Admin);
        if (admins.Count != 1)
        {
            await error.WriteLineAsync("Admin password not changed: there is no admin account. Start the API with the AdminSeed settings to create it.");
            return 1;
        }

        var admin = admins[0];
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        admin.PasswordHash = userManager.PasswordHasher.HashPassword(admin, password);
        // A new security stamp, and a clean slate for a lockout the old password may have caused.
        var updated = await userManager.UpdateSecurityStampAsync(admin);
        await userManager.ResetAccessFailedCountAsync(admin);
        await userManager.SetLockoutEndDateAsync(admin, null);
        if (!updated.Succeeded)
        {
            await error.WriteLineAsync($"Admin password not changed: the account could not be updated ({string.Join(", ", updated.Errors.Select(failure => failure.Code))}).");
            return 1;
        }

        await dbContext.RefreshTokens
            .Where(token => token.UserId == admin.Id && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, (DateTime?)now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await output.WriteLineAsync("Admin password changed. The admin's sessions were ended: sign in again with the new password.");
        return 0;
    }
}
