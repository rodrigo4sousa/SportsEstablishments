using SportsBooking.Domain.Pitches;

namespace SportsBooking.UnitTests.Pitches;

/// <summary>US07: the pitch entity keeps its own rules.</summary>
public sealed class PitchTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc);
    private static readonly PitchDetails Details = new("Campo 1", Sport.Futsal, 12.5m);

    [Fact]
    public void A_new_pitch_is_active_and_belongs_to_its_club_with_a_v7_id()
    {
        var clubId = Guid.CreateVersion7();

        var pitch = Pitch.Create(clubId, Details, Now);

        pitch.Id.Version.ShouldBe(7);
        pitch.ClubId.ShouldBe(clubId);
        pitch.Name.ShouldBe("Campo 1");
        pitch.Sport.ShouldBe(Sport.Futsal);
        pitch.PricePerSlot.ShouldBe(12.5m);
        pitch.Status.ShouldBe(PitchStatus.Active);
        pitch.IsActive.ShouldBeTrue();
        pitch.CreatedAt.ShouldBe(Now);
        pitch.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public void Updating_replaces_name_sport_and_price_and_keeps_the_status()
    {
        var pitch = Pitch.Create(Guid.CreateVersion7(), Details, Now);
        pitch.Deactivate(Now);

        pitch.UpdateDetails(new PitchDetails("Campo Central", Sport.Padel, 20m), Now.AddHours(1));

        pitch.Name.ShouldBe("Campo Central");
        pitch.NameKey.ShouldBe("CAMPO CENTRAL");
        pitch.Sport.ShouldBe(Sport.Padel);
        pitch.PricePerSlot.ShouldBe(20m);
        pitch.Status.ShouldBe(PitchStatus.Inactive);
        pitch.UpdatedAt.ShouldBe(Now.AddHours(1));
        pitch.CreatedAt.ShouldBe(Now);
    }

    [Fact]
    public void A_pitch_can_be_deactivated_and_reactivated_any_number_of_times()
    {
        var pitch = Pitch.Create(Guid.CreateVersion7(), Details, Now);

        pitch.Deactivate(Now.AddDays(1));
        pitch.Deactivate(Now.AddDays(2));
        pitch.IsActive.ShouldBeFalse();
        pitch.Reactivate(Now.AddDays(3));
        pitch.Reactivate(Now.AddDays(4));

        pitch.IsActive.ShouldBeTrue();
        pitch.UpdatedAt.ShouldBe(Now.AddDays(4));
    }

    [Fact]
    public void Names_that_differ_only_in_case_or_surrounding_spaces_have_the_same_key()
    {
        PitchRules.NameKey("Campo Ténis").ShouldBe(PitchRules.NameKey("  CAMPO TÉNIS "));
        PitchRules.NameKey("Campo 1").ShouldNotBe(PitchRules.NameKey("Campo 2"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(500.01)]
    public void A_price_outside_the_rules_is_refused_by_the_entity_too(double price)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Pitch.Create(Guid.CreateVersion7(), Details with { PricePerSlot = (decimal)price }, Now));
    }

    [Fact]
    public void Times_must_be_utc()
    {
        var local = new DateTime(2026, 10, 23, 10, 0, 0, DateTimeKind.Local);

        Should.Throw<ArgumentException>(() => Pitch.Create(Guid.CreateVersion7(), Details, local));
    }

    [Fact]
    public void Sport_codes_are_stable_and_parse_back()
    {
        Sports.Codes.ShouldBe(["futsal", "football7", "padel", "tennis"]);
        foreach (var sport in Sports.All)
        {
            Sports.TryParse(Sports.ToCode(sport), out var parsed).ShouldBeTrue();
            parsed.ShouldBe(sport);
        }

        Sports.TryParse("Futsal", out _).ShouldBeFalse();
        Sports.TryParse(null, out _).ShouldBeFalse();
    }
}
