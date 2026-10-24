using SportsBooking.Domain.Pitches;

namespace SportsBooking.UnitTests.Pitches;

/// <summary>US09: the block entity keeps its own rules.</summary>
public sealed class BlockTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Start = new(2026, 5, 4, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 5, 4, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_new_block_has_a_v7_id_its_pitch_the_instants_and_a_trimmed_reason()
    {
        var pitchId = Guid.CreateVersion7();

        var block = Block.Create(pitchId, Start, End, "  Piso novo ", Now);

        block.Id.Version.ShouldBe(7);
        block.PitchId.ShouldBe(pitchId);
        block.StartsAt.ShouldBe(Start);
        block.EndsAt.ShouldBe(End);
        block.Reason.ShouldBe("Piso novo");
        block.CreatedAt.ShouldBe(Now);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_reason_is_no_reason(string? reason)
    {
        Block.Create(Guid.CreateVersion7(), Start, End, reason, Now).Reason.ShouldBeNull();
    }

    [Fact]
    public void A_block_must_end_after_it_starts_and_use_utc()
    {
        Should.Throw<ArgumentException>(() => Block.Create(Guid.CreateVersion7(), End, Start, null, Now));
        Should.Throw<ArgumentException>(() => Block.Create(Guid.CreateVersion7(), Start, Start, null, Now));
        Should.Throw<ArgumentException>(() => Block.Create(Guid.CreateVersion7(), Start.AddHours(1).ToLocalTime(), End, null, Now));
    }

    [Fact]
    public void A_block_has_started_from_its_first_instant()
    {
        var block = Block.Create(Guid.CreateVersion7(), Start, End, null, Now);

        block.HasStarted(Start.AddTicks(-1)).ShouldBeFalse();
        block.HasStarted(Start).ShouldBeTrue();
        block.HasStarted(End.AddDays(1)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(7, 9, true)] // overlaps the start
    [InlineData(9, 11, true)] // overlaps the end
    [InlineData(8, 10, true)] // the same
    [InlineData(6, 12, true)] // around it
    [InlineData(8, 9, true)] // inside
    [InlineData(6, 8, false)] // ends when it starts: touching
    [InlineData(10, 12, false)] // starts when it ends: touching
    [InlineData(11, 12, false)]
    public void Overlap_is_half_open_so_touching_periods_do_not_overlap(int fromHour, int toHour, bool overlaps)
    {
        var block = Block.Create(Guid.CreateVersion7(), Start, End, null, Now);

        block.Overlaps(new DateTime(2026, 5, 4, fromHour, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 4, toHour, 0, 0, DateTimeKind.Utc)).ShouldBe(overlaps);
    }
}
