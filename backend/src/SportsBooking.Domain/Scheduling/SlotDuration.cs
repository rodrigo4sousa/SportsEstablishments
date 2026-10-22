namespace SportsBooking.Domain.Scheduling;

/// <summary>Length of the bookable slots of a pitch: 30, 60 or 90 minutes.</summary>
public readonly record struct SlotDuration
{
    public static readonly SlotDuration ThirtyMinutes = new(30);
    public static readonly SlotDuration SixtyMinutes = new(60);
    public static readonly SlotDuration NinetyMinutes = new(90);

    private SlotDuration(int minutes) => Minutes = minutes;

    public int Minutes { get; }

    public TimeSpan Length => TimeSpan.FromMinutes(Minutes);

    public static bool IsSupported(int minutes) => minutes is 30 or 60 or 90;

    public static SlotDuration FromMinutes(int minutes) =>
        IsSupported(minutes)
            ? new SlotDuration(minutes)
            : throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "Slot duration must be 30, 60 or 90 minutes.");
}
