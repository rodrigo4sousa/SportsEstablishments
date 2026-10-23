using SportsBooking.Application.Clubs;
using SportsBooking.Application.Common;

namespace SportsBooking.UnitTests.Clubs;

/// <summary>US06: club field rules, with pt-PT messages keyed by JSON field name.</summary>
public sealed class ClubRequestValidatorTests
{
    private static readonly ClubRequest Valid = new("Clube de Ténis da Foz", "Rua do Mar, 1", "Porto", "912 345 678", "geral@tenisfoz.pt");

    private static IReadOnlyDictionary<string, string[]> CreateErrors(ClubRequest request) =>
        new CreateClubRequestValidator().Validate(request.Normalized()).ToFieldErrors();

    private static IReadOnlyDictionary<string, string[]> UpdateErrors(ClubRequest request) =>
        new UpdateClubRequestValidator().Validate(request.Normalized()).ToFieldErrors();

    [Fact]
    public void A_complete_request_is_valid_and_the_window_is_optional_when_creating()
    {
        CreateErrors(Valid).ShouldBeEmpty();
        CreateErrors(Valid with { CancellationWindowHours = 48 }).ShouldBeEmpty();
    }

    [Fact]
    public void Editing_requires_the_cancellation_window()
    {
        UpdateErrors(Valid)["cancellationWindowHours"].ShouldBe(["Indica a janela de cancelamento, em horas."]);
        UpdateErrors(Valid with { CancellationWindowHours = 12 }).ShouldBeEmpty();
    }

    [Fact]
    public void Every_required_field_has_its_own_message()
    {
        var errors = CreateErrors(new ClubRequest(" ", "", null!, "  ", ""));

        errors.Keys.ShouldBe(["name", "address", "locality", "phone", "contactEmail"], ignoreOrder: true);
        errors["name"].ShouldBe(["Indica o nome do clube."]);
        errors["address"].ShouldBe(["Indica a morada do clube."]);
        errors["locality"].ShouldBe(["Indica a localidade do clube."]);
        errors["phone"].ShouldBe(["Indica o telefone do clube."]);
        errors["contactEmail"].ShouldBe(["Indica o email de contacto do clube."]);
    }

    [Fact]
    public void Texts_have_maximum_lengths()
    {
        var errors = CreateErrors(Valid with { Name = new string('a', 101), Address = new string('a', 201), Locality = new string('a', 101) });

        errors["name"].ShouldBe(["O nome pode ter no máximo 100 caracteres."]);
        errors["address"].ShouldBe(["A morada pode ter no máximo 200 caracteres."]);
        errors["locality"].ShouldBe(["A localidade pode ter no máximo 100 caracteres."]);
        CreateErrors(Valid with { Name = new string('a', 100), Address = new string('a', 200), Locality = new string('a', 100) }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("123")]
    [InlineData("91234567a")]
    [InlineData("+3519123456789012")]
    public void Phone_follows_the_account_rules(string phone)
    {
        CreateErrors(Valid with { Phone = phone })["phone"].ShouldBe(["Indica um número de telefone válido, com 9 a 15 algarismos."]);
    }

    [Theory]
    [InlineData("geral")]
    [InlineData("geral@clube")]
    [InlineData("Clube <geral@clube.pt>")]
    public void Contact_email_follows_the_account_rules(string email)
    {
        CreateErrors(Valid with { ContactEmail = email })["contactEmail"].ShouldBe(["Indica um email válido, por exemplo nome@exemplo.pt."]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(169)]
    public void The_cancellation_window_is_between_1_and_168_hours(int hours)
    {
        var message = "A janela de cancelamento tem de estar entre 1 e 168 horas.";

        CreateErrors(Valid with { CancellationWindowHours = hours })["cancellationWindowHours"].ShouldBe([message]);
        UpdateErrors(Valid with { CancellationWindowHours = hours })["cancellationWindowHours"].ShouldBe([message]);
    }

    [Fact]
    public void Normalizing_trims_texts_and_removes_spaces_from_the_phone()
    {
        var normalized = new ClubRequest("  Padel & Co. ", " Rua 1 ", " Lisboa ", "+351 912 345 678", " geral@padel.pt ").Normalized();

        normalized.ShouldBe(new ClubRequest("Padel & Co.", "Rua 1", "Lisboa", "+351912345678", "geral@padel.pt"));
    }

    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public void Pages_start_at_1_and_hold_1_to_100_items(int page, int pageSize, string field)
    {
        new PageRequestValidator().Validate(new PageRequest(page, pageSize)).ToFieldErrors().Keys.ShouldBe([field]);
        new PageRequestValidator().Validate(new PageRequest(1, 100)).IsValid.ShouldBeTrue();
    }
}
