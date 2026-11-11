using SportsBooking.Application.Accounts;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.UnitTests.Accounts;

/// <summary>T09: the language codes of an account's emails.</summary>
public sealed class PreferredLanguageTests
{
    [Theory]
    [InlineData("pt-PT", true)]
    [InlineData("en", true)]
    [InlineData("EN", false)]
    [InlineData("pt", false)]
    [InlineData("en-GB", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_sites_codes_are_supported(string? code, bool supported) =>
        PreferredLanguage.IsSupported(code).ShouldBe(supported);

    [Theory]
    [InlineData("en", Language.English)]
    [InlineData("pt-PT", Language.Portuguese)]
    [InlineData(null, Language.Portuguese)]
    [InlineData("xx", Language.Portuguese)]
    public void No_preference_means_portuguese(string? code, Language language) =>
        PreferredLanguage.ForEmails(code).ShouldBe(language);

    [Fact]
    public void Registration_trims_the_language_and_reads_blank_as_none()
    {
        new RegisterAccountRequest("Ana", "ana@exemplo.pt", "palavra-passe", "1.0", PreferredLanguage: " en ").Normalized().PreferredLanguage.ShouldBe("en");
        new RegisterAccountRequest("Ana", "ana@exemplo.pt", "palavra-passe", "1.0", PreferredLanguage: "  ").Normalized().PreferredLanguage.ShouldBeNull();
    }

    [Fact]
    public void Registration_refuses_an_unsupported_language_on_its_field()
    {
        var validator = new RegisterAccountRequestValidator();

        var invalid = validator.Validate(new RegisterAccountRequest("Ana", "ana@exemplo.pt", "palavra-passe", "1.0", PreferredLanguage: "fr"));
        var valid = validator.Validate(new RegisterAccountRequest("Ana", "ana@exemplo.pt", "palavra-passe", "1.0", PreferredLanguage: "en"));
        var none = validator.Validate(new RegisterAccountRequest("Ana", "ana@exemplo.pt", "palavra-passe", "1.0"));

        invalid.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(RegisterAccountRequest.PreferredLanguage));
        valid.IsValid.ShouldBeTrue();
        none.IsValid.ShouldBeTrue();
    }
}
