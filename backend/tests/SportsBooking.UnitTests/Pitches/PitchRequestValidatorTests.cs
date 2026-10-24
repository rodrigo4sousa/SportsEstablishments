using SportsBooking.Application.Common;
using SportsBooking.Application.Pitches;

namespace SportsBooking.UnitTests.Pitches;

/// <summary>US07: pitch field rules, with pt-PT messages keyed by JSON field name.</summary>
public sealed class PitchRequestValidatorTests
{
    private static readonly PitchRequest Valid = new("Campo 1", "futsal", 12.5m);

    private static IReadOnlyDictionary<string, string[]> Errors(PitchRequest request) =>
        new PitchRequestValidator().Validate(request.Normalized()).ToFieldErrors();

    [Fact]
    public void A_complete_request_is_valid()
    {
        Errors(Valid).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("futsal")]
    [InlineData("football7")]
    [InlineData("padel")]
    [InlineData("tennis")]
    public void Every_sport_of_the_list_is_accepted(string sport)
    {
        Errors(Valid with { Sport = sport }).ShouldBeEmpty();
    }

    [Fact]
    public void Every_required_field_has_its_own_message()
    {
        var errors = Errors(new PitchRequest(" ", null!, 0m));

        errors.Keys.ShouldBe(["name", "sport", "pricePerSlot"], ignoreOrder: true);
        errors["name"].ShouldBe(["Indica o nome do campo."]);
        errors["sport"].ShouldBe(["Indica o desporto do campo."]);
        errors["pricePerSlot"].ShouldBe(["O preço por slot tem de ser maior que 0 €."]);
    }

    [Theory]
    [InlineData("rugby")]
    [InlineData("Futsal")]
    [InlineData("futebol 7")]
    [InlineData("tenis")]
    public void A_sport_outside_the_list_is_rejected(string sport)
    {
        Errors(Valid with { Sport = sport })["sport"].ShouldBe(["Escolhe um desporto da lista: futsal, futebol 7, padel ou ténis."]);
    }

    [Fact]
    public void The_name_is_limited_to_100_characters_after_trimming()
    {
        Errors(Valid with { Name = new string('a', 100) }).ShouldBeEmpty();
        Errors(Valid with { Name = $"  {new string('a', 100)}  " }).ShouldBeEmpty();
        Errors(Valid with { Name = new string('a', 101) })["name"].ShouldBe(["O nome pode ter no máximo 100 caracteres."]);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("12.5")]
    [InlineData("12.50")]
    [InlineData("500")]
    [InlineData("500.00")]
    public void Prices_above_zero_up_to_500_with_at_most_two_decimals_are_valid(string price)
    {
        Errors(Valid with { PricePerSlot = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture) }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("0", "O preço por slot tem de ser maior que 0 €.")]
    [InlineData("-0.01", "O preço por slot tem de ser maior que 0 €.")]
    [InlineData("500.01", "O preço por slot não pode passar de 500 €.")]
    [InlineData("12.345", "O preço por slot pode ter no máximo duas casas decimais.")]
    [InlineData("0.001", "O preço por slot pode ter no máximo duas casas decimais.")]
    public void Other_prices_are_rejected_with_one_message(string price, string message)
    {
        Errors(Valid with { PricePerSlot = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture) })["pricePerSlot"].ShouldBe([message]);
    }

    [Fact]
    public void Trailing_zeros_after_two_decimals_do_not_count_as_a_third_decimal()
    {
        Errors(Valid with { PricePerSlot = 1.500m }).ShouldBeEmpty();
    }
}
