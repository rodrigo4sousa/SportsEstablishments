using SportsBooking.Domain.Accounts;

namespace SportsBooking.UnitTests.Accounts;

public sealed class AccountRulesTests
{
    [Theory]
    [InlineData("12345678")]
    [InlineData("uma frase longa")]
    [InlineData("ÁÉÍÓÚçãõ")]
    public void Passwords_with_at_least_eight_characters_are_accepted(string password)
    {
        PasswordPolicy.IsSatisfiedBy(password).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567")]
    public void Shorter_or_missing_passwords_are_rejected(string? password)
    {
        PasswordPolicy.IsSatisfiedBy(password).ShouldBeFalse();
    }

    [Fact]
    public void A_visitor_can_sign_up_as_a_player_or_an_owner_but_never_as_an_admin()
    {
        Profiles.SelfRegistration.ShouldBe([Profiles.Player, Profiles.Owner]);
        Profiles.SelfRegistration.ShouldNotContain(Profiles.Admin);
    }

    [Fact]
    public void There_are_exactly_three_distinct_profiles()
    {
        Profiles.All.ShouldBe([Profiles.Player, Profiles.Owner, Profiles.Admin]);
        Profiles.All.Distinct().Count().ShouldBe(3);
    }
}
