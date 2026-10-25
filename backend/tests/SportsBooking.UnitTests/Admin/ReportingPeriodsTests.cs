using System.Globalization;
using SportsBooking.Application.Admin;

namespace SportsBooking.UnitTests.Admin;

public sealed class ReportingPeriodsTests
{
    private static DateOnly Day(string date) => DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("2026-10-26", "2026-10-26")] // Monday
    [InlineData("2026-10-28", "2026-10-26")] // Wednesday
    [InlineData("2026-11-01", "2026-10-26")] // Sunday belongs to the week that began on Monday
    [InlineData("2026-11-02", "2026-11-02")]
    [InlineData("2027-01-01", "2026-12-28")] // across the year
    public void A_week_starts_on_monday(string date, string expectedStart)
    {
        ReportingPeriods.Start(Day(date), Granularity.Week).ShouldBe(Day(expectedStart));
    }

    [Fact]
    public void A_month_starts_on_the_first_and_a_day_on_itself()
    {
        ReportingPeriods.Start(new DateOnly(2026, 10, 28), Granularity.Month).ShouldBe(new DateOnly(2026, 10, 1));
        ReportingPeriods.Start(new DateOnly(2026, 10, 28), Granularity.Day).ShouldBe(new DateOnly(2026, 10, 28));
    }

    [Theory]
    [InlineData(Granularity.Day, "2026-10-25", "2026-10-26", "2026-10-25")]
    [InlineData(Granularity.Week, "2026-10-26", "2026-11-02", "2026-11-01")]
    [InlineData(Granularity.Month, "2026-02-01", "2026-03-01", "2026-02-28")]
    [InlineData(Granularity.Month, "2028-02-01", "2028-03-01", "2028-02-29")]
    public void A_period_ends_the_day_before_the_next_starts(Granularity granularity, string start, string next, string end)
    {
        ReportingPeriods.Next(Day(start), granularity).ShouldBe(Day(next));
        ReportingPeriods.End(Day(start), granularity).ShouldBe(Day(end));
    }

    [Theory]
    [InlineData("day", true, Granularity.Day)]
    [InlineData("week", true, Granularity.Week)]
    [InlineData("month", true, Granularity.Month)]
    [InlineData("Day", false, Granularity.Day)]
    [InlineData("year", false, Granularity.Day)]
    [InlineData(null, false, Granularity.Day)]
    public void Only_the_three_codes_are_accepted(string? code, bool accepted, Granularity expected)
    {
        ReportingPeriods.TryParse(code, out var granularity).ShouldBe(accepted);
        granularity.ShouldBe(expected);
        if (accepted)
        {
            ReportingPeriods.ToCode(granularity).ShouldBe(code);
        }
    }

    [Fact]
    public void The_longest_ranges_are_a_year_of_days_two_of_weeks_and_five_of_months()
    {
        ReportingPeriods.MaximumPeriods(Granularity.Day).ShouldBe(366);
        ReportingPeriods.MaximumPeriods(Granularity.Week).ShouldBe(104);
        ReportingPeriods.MaximumPeriods(Granularity.Month).ShouldBe(60);
    }
}
