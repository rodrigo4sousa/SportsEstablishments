using SportsBooking.Infrastructure.Identity;

namespace SportsBooking.UnitTests.Accounts;

public sealed class AdminSeedSettingsTests
{
    [Fact]
    public void Email_and_password_that_follow_the_rules_are_valid()
    {
        AdminSeedSettings.Evaluate("admin@reservas.pt", "palavra-passe").ShouldBe(AdminSeedConfiguration.Valid);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Missing_email_and_password_means_not_configured(string? email, string? password)
    {
        AdminSeedSettings.Evaluate(email, password).ShouldBe(AdminSeedConfiguration.NotConfigured);
    }

    [Theory]
    [InlineData("admin@reservas.pt", null)]
    [InlineData("admin@reservas.pt", "")]
    [InlineData(null, "palavra-passe")]
    [InlineData("  ", "palavra-passe")]
    public void Only_one_of_email_and_password_means_partially_configured(string? email, string? password)
    {
        AdminSeedSettings.Evaluate(email, password).ShouldBe(AdminSeedConfiguration.PartiallyConfigured);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("admin@")]
    [InlineData("@reservas.pt")]
    [InlineData("admin@localhost")]
    [InlineData(" admin@reservas.pt")]
    [InlineData("admin@reservas.pt ")]
    [InlineData("Admin <admin@reservas.pt>")]
    public void Malformed_email_is_invalid(string email)
    {
        AdminSeedSettings.Evaluate(email, "palavra-passe").ShouldBe(AdminSeedConfiguration.InvalidEmail);
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("curta")]
    public void Password_shorter_than_eight_characters_is_rejected(string password)
    {
        AdminSeedSettings.Evaluate("admin@reservas.pt", password).ShouldBe(AdminSeedConfiguration.PasswordTooShort);
    }
}
