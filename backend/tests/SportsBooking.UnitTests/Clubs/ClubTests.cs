using SportsBooking.Domain.Clubs;

namespace SportsBooking.UnitTests.Clubs;

/// <summary>US06: the club entity keeps its own rules.</summary>
public sealed class ClubTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc);
    private static readonly ClubDetails Details = new("Clube de Ténis da Foz", "Rua do Mar, 1", "Porto", "+351912345678", "geral@tenisfoz.pt");

    [Fact]
    public void A_new_club_has_its_owner_slug_lisbon_time_zone_and_a_v7_id()
    {
        var ownerId = Guid.CreateVersion7();

        var club = Club.Create(ownerId, "clube-de-tenis-da-foz", Details, 24, Now);

        club.Id.Version.ShouldBe(7);
        club.OwnerId.ShouldBe(ownerId);
        club.Slug.ShouldBe("clube-de-tenis-da-foz");
        club.Name.ShouldBe("Clube de Ténis da Foz");
        club.TimeZoneId.ShouldBe("Europe/Lisbon");
        club.CancellationWindowHours.ShouldBe(24);
        club.CancellationWindow.ShouldBe(TimeSpan.FromHours(24));
        club.CreatedAt.ShouldBe(Now);
        club.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public void Renaming_keeps_the_slug()
    {
        var club = Club.Create(Guid.CreateVersion7(), "clube-de-tenis-da-foz", Details, 24, Now);

        club.UpdateDetails(Details with { Name = "Foz Ténis & Padel" }, Now.AddDays(1));

        club.Name.ShouldBe("Foz Ténis & Padel");
        club.Slug.ShouldBe("clube-de-tenis-da-foz");
        club.UpdatedAt.ShouldBe(Now.AddDays(1));
        club.CreatedAt.ShouldBe(Now);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(48)]
    [InlineData(168)]
    public void The_cancellation_window_can_change_within_its_range(int hours)
    {
        var club = Club.Create(Guid.CreateVersion7(), "clube", Details, 24, Now);

        club.ChangeCancellationWindow(hours, Now.AddHours(1));

        club.CancellationWindowHours.ShouldBe(hours);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(169)]
    public void A_cancellation_window_outside_the_range_is_refused(int hours)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Club.Create(Guid.CreateVersion7(), "clube", Details, hours, Now));
        var club = Club.Create(Guid.CreateVersion7(), "clube", Details, 24, Now);
        Should.Throw<ArgumentOutOfRangeException>(() => club.ChangeCancellationWindow(hours, Now));
        club.CancellationWindowHours.ShouldBe(24);
    }

    [Fact]
    public void Times_must_be_utc()
    {
        Should.Throw<ArgumentException>(() => Club.Create(Guid.CreateVersion7(), "clube", Details, 24, DateTime.SpecifyKind(Now, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void A_club_needs_a_slug()
    {
        Should.Throw<ArgumentException>(() => Club.Create(Guid.CreateVersion7(), " ", Details, 24, Now));
    }
}
