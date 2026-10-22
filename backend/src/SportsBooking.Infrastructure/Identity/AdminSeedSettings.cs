using System.Net.Mail;
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

        if (!IsPlainEmailAddress(email!))
        {
            return AdminSeedConfiguration.InvalidEmail;
        }

        return PasswordPolicy.IsSatisfiedBy(password)
            ? AdminSeedConfiguration.Valid
            : AdminSeedConfiguration.PasswordTooShort;
    }

    // A bare address only: no display name, no surrounding whitespace.
    private static bool IsPlainEmailAddress(string email) =>
        MailAddress.TryCreate(email, out var address)
        && string.Equals(address.Address, email, StringComparison.Ordinal)
        && address.Host.Contains('.', StringComparison.Ordinal);
}
