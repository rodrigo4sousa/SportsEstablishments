using SportsBooking.Domain.Accounts;

namespace SportsBooking.Infrastructure.Identity;

public enum AdminSeedConfiguration
{
    Valid,
    NotConfigured,
    PartiallyConfigured,
    InvalidEmail,
    PasswordTooShort,
}

public static class AdminSeedSettings
{
    public static AdminSeedConfiguration Evaluate(string? email, string? password)
    {
        var hasEmail = !string.IsNullOrWhiteSpace(email);
        var hasPassword = !string.IsNullOrEmpty(password);

        if (!hasEmail && !hasPassword)
        {
            return AdminSeedConfiguration.NotConfigured;
        }

        if (!hasEmail || !hasPassword)
        {
            return AdminSeedConfiguration.PartiallyConfigured;
        }

        if (!EmailAddressPolicy.IsValid(email))
        {
            return AdminSeedConfiguration.InvalidEmail;
        }

        return PasswordPolicy.IsSatisfiedBy(password)
            ? AdminSeedConfiguration.Valid
            : AdminSeedConfiguration.PasswordTooShort;
    }
}
