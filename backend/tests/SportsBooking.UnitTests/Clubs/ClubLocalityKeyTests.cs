using SportsBooking.Domain.Clubs;

namespace SportsBooking.UnitTests.Clubs;

/// <summary>US16 (provisional): the locality search ignores case, accents and extra white space.</summary>
public sealed class ClubLocalityKeyTests
{
    private static readonly DateTime Now = new(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);
    private static readonly string[] SameLocalityDifferentSpelling = ["São João", "SAO JOAO", "sao  joao", " são joão "];
    private static readonly ClubDetails Details = new("Clube", "Rua 1", "Porto", "912345678", "geral@clube.pt");

    [Theory]
    [InlineData("Porto", "porto")]
    [InlineData("PORTO", "porto")]
    [InlineData("Pórtó", "porto")]
    [InlineData("São João da Madeira", "sao joao da madeira")]
    [InlineData("Évora", "evora")]
    [InlineData("ÉVORA", "evora")]
    [InlineData("Covilhã", "covilha")]
    [InlineData("Ílhavo", "ilhavo")]
    [InlineData("Açores", "acores")]
    [InlineData("Câmara de Lobos", "camara de lobos")]
    [InlineData("  Vila   Nova\tde  Gaia ", "vila nova de gaia")]
    [InlineData("Peniche", "peniche")]
    public void The_key_has_no_accents_no_capitals_and_single_spaces(string locality, string expected)
    {
        ClubRules.LocalityKey(locality).ShouldBe(expected);
    }

    [Fact]
    public void Spellings_that_differ_only_in_case_accents_and_spacing_share_one_key()
    {
        var keys = SameLocalityDifferentSpelling.Select(ClubRules.LocalityKey).Distinct().ToList();

        keys.ShouldBe(["sao joao"]);
    }

    [Theory]
    [InlineData("Lisboa", "Porto")]
    [InlineData("Vila Real", "Vila Real de Santo António")]
    public void Different_localities_have_different_keys(string first, string second)
    {
        ClubRules.LocalityKey(first).ShouldNotBe(ClubRules.LocalityKey(second));
    }

    [Fact]
    public void The_key_is_never_longer_than_the_locality_it_comes_from()
    {
        var longest = new string('Ã', ClubRules.LocalityMaximumLength);

        ClubRules.LocalityKey(longest).Length.ShouldBeLessThanOrEqualTo(ClubRules.LocalityMaximumLength);
    }

    [Fact]
    public void A_club_keeps_its_locality_key_in_step_with_the_locality()
    {
        var club = Club.Create(Guid.CreateVersion7(), "clube", Details with { Locality = "São João da Madeira" }, 24, Now);
        club.LocalityKey.ShouldBe("sao joao da madeira");

        club.UpdateDetails(Details with { Locality = "ÉVORA" }, Now.AddDays(1));

        club.Locality.ShouldBe("ÉVORA");
        club.LocalityKey.ShouldBe("evora");
    }
}
