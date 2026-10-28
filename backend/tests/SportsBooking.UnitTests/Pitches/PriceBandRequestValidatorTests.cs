using SportsBooking.Application.Pitches;

namespace SportsBooking.UnitTests.Pitches;

/// <summary>US10: field rules of a price band request, one message per field in pt-PT.</summary>
public sealed class PriceBandRequestValidatorTests
{
    private static readonly PriceBandRequestValidator Validator = new();

    private static PriceBandRequest Valid => new(["monday", "friday"], "18:00", "22:00", 30m);

    private static Dictionary<string, string[]> Errors(PriceBandRequest request)
    {
        var result = Validator.Validate(request.Normalized());
        return result.Errors.GroupBy(error => char.ToLowerInvariant(error.PropertyName[0]) + error.PropertyName[1..])
            .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray());
    }

    [Fact]
    public void A_good_request_has_no_errors()
    {
        Errors(Valid).ShouldBeEmpty();
        Errors(Valid with { End = "00:00" }).ShouldBeEmpty();
        Errors(Valid with { Start = "00:00", End = "00:00" }).ShouldBeEmpty();
    }

    [Fact]
    public void An_empty_set_of_weekdays_is_refused_on_days()
    {
        Errors(Valid with { Days = [] })["days"].ShouldBe(["Escolhe pelo menos um dia da semana."]);
    }

    [Fact]
    public void A_weekday_name_that_does_not_exist_is_refused_on_days()
    {
        Errors(Valid with { Days = ["monday", "someday"] }).ShouldContainKey("days");
    }

    [Fact]
    public void Day_names_are_trimmed_lower_cased_and_without_repeats()
    {
        var normalized = (Valid with { Days = [" Friday ", "MONDAY", "monday"] }).Normalized();

        normalized.Days.ShouldBe(["monday", "friday"]);
    }

    [Theory]
    [InlineData("", "Indica a hora de início da faixa.")]
    [InlineData("6pm", "Indica a hora no formato HH:mm, por exemplo 18:00.")]
    [InlineData("18:15", "As horas vão de meia em meia hora, por exemplo 18:00 ou 18:30.")]
    public void A_bad_start_is_refused_on_start(string start, string message)
    {
        Errors(Valid with { Start = start })["start"].ShouldBe([message]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ten")]
    [InlineData("22:10")]
    public void A_bad_end_is_refused_on_end(string end)
    {
        Errors(Valid with { End = end }).ShouldContainKey("end");
    }

    [Theory]
    [InlineData("22:00", "18:00")]
    [InlineData("18:00", "18:00")]
    public void An_end_that_is_not_after_the_start_is_refused_on_end(string start, string end)
    {
        Errors(Valid with { Start = start, End = end })["end"].Single().ShouldStartWith("O fim tem de ser depois do início.");
    }

    [Theory]
    [InlineData(0, "O preço tem de ser maior que 0 €.")]
    [InlineData(-5, "O preço tem de ser maior que 0 €.")]
    [InlineData(500.01, "O preço não pode passar de 500 €.")]
    [InlineData(12.505, "O preço pode ter no máximo duas casas decimais.")]
    public void A_price_out_of_range_is_refused_on_price(double price, string message)
    {
        Errors(Valid with { Price = (decimal)price })["price"].ShouldBe([message]);
    }

    [Fact]
    public void The_limits_of_the_price_are_accepted()
    {
        Errors(Valid with { Price = 0.01m }).ShouldBeEmpty();
        Errors(Valid with { Price = 500m }).ShouldBeEmpty();
    }
}
