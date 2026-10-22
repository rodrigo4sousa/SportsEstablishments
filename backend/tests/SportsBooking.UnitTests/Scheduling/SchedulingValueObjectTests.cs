using SportsBooking.Domain.Scheduling;

namespace SportsBooking.UnitTests.Scheduling;

public sealed class SchedulingValueObjectTests
{
    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(90)]
    public void Supported_slot_durations_are_accepted(int minutes)
    {
        SlotDuration.IsSupported(minutes).ShouldBeTrue();
        SlotDuration.FromMinutes(minutes).Length.ShouldBe(TimeSpan.FromMinutes(minutes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(45)]
    [InlineData(120)]
    [InlineData(-60)]
    public void Other_slot_durations_are_rejected(int minutes)
    {
        SlotDuration.IsSupported(minutes).ShouldBeFalse();
        Should.Throw<ArgumentOutOfRangeException>(() => SlotDuration.FromMinutes(minutes));
    }

    [Theory]
    [InlineData("09:00", "21:00")]
    [InlineData("18:00", "00:00")] // closes at midnight
    [InlineData("00:00", "00:00")] // open all day
    [InlineData("23:30", "00:00")]
    public void Valid_opening_hours_are_accepted(string opens, string closes)
    {
        var hours = OpeningHours.Create(TimeOnly.Parse(opens, System.Globalization.CultureInfo.InvariantCulture), TimeOnly.Parse(closes, System.Globalization.CultureInfo.InvariantCulture));

        hours.ClosesAtMidnight.ShouldBe(closes == "00:00");
    }

    [Theory]
    [InlineData("10:00", "10:00")]
    [InlineData("21:00", "09:00")]
    [InlineData("22:00", "02:00")] // past midnight is not supported
    public void Invalid_opening_hours_are_rejected(string opens, string closes)
    {
        var open = TimeOnly.Parse(opens, System.Globalization.CultureInfo.InvariantCulture);
        var close = TimeOnly.Parse(closes, System.Globalization.CultureInfo.InvariantCulture);

        OpeningHours.AreValid(open, close).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => OpeningHours.Create(open, close));
    }
}
