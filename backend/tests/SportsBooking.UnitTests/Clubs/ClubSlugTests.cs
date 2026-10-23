using SportsBooking.Domain.Clubs;

namespace SportsBooking.UnitTests.Clubs;

/// <summary>US06: the public slug made from a club's name.</summary>
public sealed class ClubSlugTests
{
    [Theory]
    [InlineData("Clube de Ténis da Foz", "clube-de-tenis-da-foz")]
    [InlineData("Padel & Co.", "padel-co")]
    [InlineData("Clube Desportivo do Porto", "clube-desportivo-do-porto")]
    [InlineData("Associação Recreativa São João", "associacao-recreativa-sao-joao")]
    [InlineData("Ginásio Açores — Futsal", "ginasio-acores-futsal")]
    [InlineData("  CAMPOS   DA   LUZ  ", "campos-da-luz")]
    [InlineData("Futebol 7 Olivais", "futebol-7-olivais")]
    [InlineData("O'Neill's Padel", "o-neill-s-padel")]
    [InlineData("Ténis-Clube/Benfica", "tenis-clube-benfica")]
    [InlineData("Ñandú Pádel Ü", "nandu-padel-u")]
    public void Names_become_lowercase_ascii_words_joined_by_hyphens(string name, string slug)
    {
        ClubSlug.FromName(name).ShouldBe(slug);
    }

    [Theory]
    [InlineData("&&&")]
    [InlineData("—")]
    [InlineData("足球")]
    public void A_name_with_nothing_usable_gets_the_fallback(string name)
    {
        ClubSlug.FromName(name).ShouldBe(ClubSlug.Fallback);
    }

    [Fact]
    public void Long_names_are_cut_at_a_word_boundary_without_a_trailing_hyphen()
    {
        var name = string.Join(' ', Enumerable.Repeat("Associação", 12));

        var slug = ClubSlug.FromName(name);

        slug.Length.ShouldBeLessThanOrEqualTo(ClubSlug.BaseMaximumLength);
        slug.ShouldNotEndWith("-");
        slug.Split('-').ShouldAllBe(word => word == "associacao");
    }

    [Fact]
    public void Slugs_contain_only_lowercase_ascii_letters_digits_and_single_hyphens()
    {
        var slug = ClubSlug.FromName("Ç'À  --  É!? 9 Ü");

        slug.ShouldBe("c-a-e-9-u");
        slug.ShouldMatch("^[a-z0-9]+(-[a-z0-9]+)*$");
    }

    [Fact]
    public void A_free_base_is_used_as_it_is()
    {
        ClubSlug.NextAvailable("padel-co", []).ShouldBe("padel-co");
        ClubSlug.NextAvailable("padel-co", ["padel-co-lisboa", "padel"]).ShouldBe("padel-co");
    }

    [Fact]
    public void A_taken_base_gets_the_next_suffix_after_the_highest_in_use()
    {
        ClubSlug.NextAvailable("padel-co", ["padel-co"]).ShouldBe("padel-co-2");
        ClubSlug.NextAvailable("padel-co", ["padel-co", "padel-co-2"]).ShouldBe("padel-co-3");
        ClubSlug.NextAvailable("padel-co", ["padel-co", "padel-co-2", "padel-co-7"]).ShouldBe("padel-co-8");
    }

    [Fact]
    public void Other_names_that_start_like_the_base_are_not_collisions()
    {
        // "padel-co-lisboa" and "padel-co-2-2" are other clubs' slugs, not suffixes of "padel-co".
        ClubSlug.NextAvailable("padel-co", ["padel-co", "padel-co-lisboa", "padel-co-2-2", "padel-co-1", "padel-co-007"]).ShouldBe("padel-co-2");
    }

    [Fact]
    public void A_name_ending_in_a_number_does_not_steal_the_base_or_its_suffixes()
    {
        // "Futebol 7" gives "futebol-7": a slug of its own, while "Futebol" is still free.
        ClubSlug.NextAvailable("futebol", ["futebol-7"]).ShouldBe("futebol");
        ClubSlug.NextAvailable("futebol", ["futebol", "futebol-7"]).ShouldBe("futebol-8");
        ClubSlug.NextAvailable("futebol-7", ["futebol-7"]).ShouldBe("futebol-7-2");
    }

    [Theory]
    [InlineData("padel-co", "padel-co", 1)]
    [InlineData("padel-co-2", "padel-co", 2)]
    [InlineData("padel-co-15", "padel-co", 15)]
    [InlineData("padel-co-1", "padel-co", null)]
    [InlineData("padel-co-02", "padel-co", null)]
    [InlineData("padel-co-lisboa", "padel-co", null)]
    [InlineData("padel-cop", "padel-co", null)]
    [InlineData("padel", "padel-co", null)]
    public void Collision_suffixes_are_read_back_exactly(string slug, string baseSlug, int? suffix)
    {
        ClubSlug.SuffixOf(slug, baseSlug).ShouldBe(suffix);
    }

    [Fact]
    public void Suffixes_start_at_2()
    {
        ClubSlug.WithSuffix("padel", 2).ShouldBe("padel-2");
        Should.Throw<ArgumentOutOfRangeException>(() => ClubSlug.WithSuffix("padel", 1));
    }
}
