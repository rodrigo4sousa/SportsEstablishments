using Microsoft.Extensions.Options;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Infrastructure.Identity;

/// <summary>
/// Partial or invalid admin seed settings stop the API at startup (US04). Both settings absent is valid:
/// the API starts without an admin. Failure messages name the settings, never their values.
/// </summary>
internal sealed class AdminSeedOptionsValidator : IValidateOptions<AdminSeedOptions>
{
    public ValidateOptionsResult Validate(string? name, AdminSeedOptions options) =>
        AdminSeedSettings.Evaluate(options.Email, options.Password) switch
        {
            AdminSeedConfiguration.PartiallyConfigured => ValidateOptionsResult.Fail(
                "AdminSeed:Email and AdminSeed:Password must be set together: set both to seed the admin account, or neither to start without one."),
            AdminSeedConfiguration.InvalidEmail => ValidateOptionsResult.Fail(
                "AdminSeed:Email is not a valid email address."),
            AdminSeedConfiguration.PasswordTooShort => ValidateOptionsResult.Fail(
                $"AdminSeed:Password must have at least {PasswordPolicy.MinimumLength} characters."),
            _ => ValidateOptionsResult.Success,
        };
}
