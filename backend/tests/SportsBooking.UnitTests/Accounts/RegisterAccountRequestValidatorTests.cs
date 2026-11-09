using SportsBooking.Application.Accounts;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.UnitTests.Accounts;

/// <summary>US01: field rules for player registration, with pt-PT messages keyed by JSON field name.</summary>
public sealed class RegisterAccountRequestValidatorTests
{
    private static readonly RegisterAccountRequestValidator Validator = new();

    private static readonly RegisterAccountRequest ValidRequest = new("Ana Silva", "ana@exemplo.pt", "palavra-passe", "1.0", "912 345 678");

    private static IReadOnlyDictionary<string, string[]> Errors(RegisterAccountRequest request) =>
        Validator.Validate(request.Normalized()).ToFieldErrors();

    [Fact]
    public void A_complete_request_is_valid()
    {
        Errors(ValidRequest).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("+351 912 345 678")]
    [InlineData("912345678")]
    [InlineData("+447700900123")]
    public void Phone_is_optional_and_accepts_portuguese_and_international_numbers(string? phone)
    {
        Errors(ValidRequest with { Phone = phone }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_is_required(string? name)
    {
        Errors(ValidRequest with { Name = name! })["name"].ShouldBe(["Indica o teu nome."]);
    }

    [Fact]
    public void Name_has_at_most_100_characters_after_trimming()
    {
        Errors(ValidRequest with { Name = $"  {new string('a', 100)}  " }).ShouldBeEmpty();
        Errors(ValidRequest with { Name = new string('a', 101) })["name"]
            .ShouldBe(["O nome pode ter no máximo 100 caracteres."]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Email_is_required(string? email)
    {
        Errors(ValidRequest with { Email = email! })["email"].ShouldBe(["Indica o teu email."]);
    }

    [Theory]
    [InlineData("ana")]
    [InlineData("ana@")]
    [InlineData("@exemplo.pt")]
    [InlineData("ana@localhost")]
    [InlineData("ana silva@exemplo.pt")]
    [InlineData("Ana <ana@exemplo.pt>")]
    [InlineData("ana@exemplo.pt, rui@exemplo.pt")]
    public void Email_must_be_a_plain_address(string email)
    {
        Errors(ValidRequest with { Email = email })["email"].ShouldBe(["Indica um email válido, por exemplo nome@exemplo.pt."]);
    }

    [Fact]
    public void Email_has_at_most_256_characters_the_column_length()
    {
        var local = new string('a', 64);
        var labels = string.Concat(Enumerable.Repeat("abcdefghi.", 18));
        var atLimit = $"{local}@{labels}exemplos.pt";
        var overLimit = $"{local}@{labels}exemplosx.pt";
        atLimit.Length.ShouldBe(EmailAddressPolicy.MaximumLength);
        overLimit.Length.ShouldBe(EmailAddressPolicy.MaximumLength + 1);

        Errors(ValidRequest with { Email = atLimit }).ShouldBeEmpty();
        Errors(ValidRequest with { Email = overLimit }).ShouldContainKey("email");
    }

    [Fact]
    public void Surrounding_whitespace_around_the_email_is_ignored()
    {
        Errors(ValidRequest with { Email = "  ana@exemplo.pt\t" }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567")]
    public void Password_needs_at_least_8_characters(string? password)
    {
        Errors(ValidRequest with { Password = password! })["password"]
            .ShouldBe(["A palavra-passe tem de ter pelo menos 8 caracteres."]);
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("        ")]
    [InlineData("só letras")]
    public void Any_password_with_8_characters_is_accepted(string password)
    {
        Errors(ValidRequest with { Password = password }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("1234567890123456")]
    [InlineData("91234567a")]
    [InlineData("912-345-678")]
    [InlineData("++351912345678")]
    public void Phone_must_have_9_to_15_digits(string phone)
    {
        Errors(ValidRequest with { Phone = phone })["phone"]
            .ShouldBe(["Indica um número de telemóvel válido, com 9 a 15 algarismos."]);
    }

    [Fact]
    public void Every_invalid_field_gets_its_own_message()
    {
        var errors = Errors(new RegisterAccountRequest("", "não-é-email", "curta", "1.0", "123"));

        errors.Keys.ShouldBe(["name", "email", "password", "phone"], ignoreOrder: true);
    }

    [Fact]
    public void Normalization_trims_name_and_email_strips_phone_spaces_and_keeps_the_password()
    {
        var normalized = new RegisterAccountRequest("  Ana Silva ", " Ana@Exemplo.pt ", " palavra ", " 1.0 ", " +351 912 345 678 ").Normalized();

        normalized.ShouldBe(new RegisterAccountRequest("Ana Silva", "Ana@Exemplo.pt", " palavra ", "1.0", "+351912345678"));
        new RegisterAccountRequest("Ana", "ana@exemplo.pt", "palavra-passe", "1.0", "   ").Normalized().Phone.ShouldBeNull();
    }
}
