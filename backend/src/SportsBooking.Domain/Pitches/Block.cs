namespace SportsBooking.Domain.Pitches;

/// <summary>US09 limits for blocks (provisional, see US09 in docs/backlog.md). The reason length is also the column length.</summary>
public static class BlockRules
{
    public const int ReasonMaximumLength = 200;

    /// <summary>A block must end no later than this many days from now.</summary>
    public const int MaximumDaysAhead = 90;

    /// <summary>Start and end go in steps of 30 minutes in club local time, like opening hours (US08).</summary>
    public const int TimeStepMinutes = PitchScheduleRules.TimeStepMinutes;
}

/// <summary>
/// A period during which a pitch cannot be booked (US09), as UTC instants [StartsAt, EndsAt). Blocks of one pitch
/// never overlap (checked under the pitch row lock); touching blocks are fine. A block that has not started can be
/// removed; one that has started stays as history.
/// </summary>
public sealed class Block
{
    private Block()
    {
    }

    /// <summary>Public id (GUID v7).</summary>
    public Guid Id { get; private set; }

    public Guid PitchId { get; private set; }

    public DateTime StartsAt { get; private set; }

    public DateTime EndsAt { get; private set; }

    public string? Reason { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public static Block Create(Guid pitchId, DateTime startsAt, DateTime endsAt, string? reason, DateTime now)
    {
        if (EnsureUtc(endsAt) <= EnsureUtc(startsAt))
        {
            throw new ArgumentException("A block must end after it starts.", nameof(endsAt));
        }

        return new Block
        {
            Id = Guid.CreateVersion7(),
            PitchId = pitchId,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            CreatedAt = EnsureUtc(now),
        };
    }

    public bool HasStarted(DateTime now) => StartsAt <= EnsureUtc(now);

    /// <summary>Half-open overlap: a block that ends exactly when another starts does not overlap it.</summary>
    public bool Overlaps(DateTime startsAt, DateTime endsAt) => StartsAt < endsAt && startsAt < EndsAt;

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : throw new ArgumentException("Times must be UTC.", nameof(value));
}
