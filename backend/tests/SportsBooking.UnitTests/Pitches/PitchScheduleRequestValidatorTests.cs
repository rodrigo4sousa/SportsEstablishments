using SportsBooking.Application.Common;
using SportsBooking.Application.Pitches;

namespace SportsBooking.UnitTests.Pitches;

/// <summary>US08: schedule rules, with pt-PT messages keyed by JSON path (<c>monday.opens</c>).</summary>
public sealed class PitchScheduleRequestValidatorTests
{
    private const string NotAfterOpening =
        "A hora de fecho tem de ser depois da abertura. O fecho mais tarde é 00:00 (meia-noite); horários que passam da meia-noite não são aceites.";

    private static readonly DayHours Closed = DayHours.ClosedDay;

    private static DayHours Open(string? opens, string? closes) => new(false, opens, closes);

    private static PitchScheduleRequest Week(int minutes = 60, DayHours? monday = null) =>
        new(minutes, monday ?? Open("09:00", "22:00"), Closed, Closed, Closed, Closed, Closed, Closed);

    private static IReadOnlyDictionary<string, string[]> Errors(PitchScheduleRequest request) =>
        new PitchScheduleRequestValidator().Validate(request).ToFieldErrors();

    [Fact]
    public void A_week_with_open_and_closed_days_is_valid()
    {
        Errors(Week()).ShouldBeEmpty();
        Errors(new PitchScheduleRequest(30, Open("08:00", "23:30"), Open("08:30", "00:00"), Closed, Closed, Closed, Open("00:00", "00:00"), Closed)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_day_may_be_closed()
    {
        Errors(new PitchScheduleRequest(60, Closed, Closed, Closed, Closed, Closed, Closed, Closed)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(90)]
    public void The_slot_length_is_30_60_or_90(int minutes)
    {
        Errors(Week(minutes)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(45)]
    [InlineData(120)]
    [InlineData(-60)]
    public void Any_other_slot_length_is_rejected(int minutes)
    {
        Errors(Week(minutes))["slotDurationMinutes"].ShouldBe(["A duração do slot tem de ser 30, 60 ou 90 minutos."]);
    }

    [Fact]
    public void Every_weekday_is_required_because_the_whole_week_is_replaced()
    {
        var errors = Errors(new PitchScheduleRequest(60, null!, Closed, Closed, Closed, Closed, null!, null!));

        errors.Keys.ShouldBe(["monday", "saturday", "sunday"], ignoreOrder: true);
        errors["monday"].ShouldBe(["Indica o horário de segunda-feira ou marca o dia como fechado."]);
        errors["saturday"].ShouldBe(["Indica o horário de sábado ou marca o dia como fechado."]);
        errors["sunday"].ShouldBe(["Indica o horário de domingo ou marca o dia como fechado."]);
    }

    [Fact]
    public void A_closed_day_cannot_have_hours()
    {
        Errors(Week(monday: new DayHours(true, "09:00", null)))["monday"]
            .ShouldBe(["Um dia fechado não tem horas: tira as horas de segunda-feira ou abre o dia."]);
        Errors(Week(monday: new DayHours(true, "", ""))).ShouldBeEmpty();
    }

    [Fact]
    public void An_open_day_needs_both_times()
    {
        var errors = Errors(Week(monday: Open(null, " ")));

        errors["monday.opens"].ShouldBe(["Indica a hora de abertura de segunda-feira."]);
        errors["monday.closes"].ShouldBe(["Indica a hora de fecho de segunda-feira."]);
    }

    [Theory]
    [InlineData("9:00")]
    [InlineData("09:00:00")]
    [InlineData("24:00")]
    [InlineData("0900")]
    [InlineData("09h00")]
    [InlineData("nove")]
    public void Times_must_be_hh_mm(string opens)
    {
        var errors = Errors(Week(monday: Open(opens, "22:00")));

        errors["monday.opens"].ShouldBe(["Indica a hora no formato HH:mm, por exemplo 09:00."]);
        errors.ContainsKey("monday.closes").ShouldBeFalse();
    }

    [Theory]
    [InlineData("09:15", "22:00", "monday.opens")]
    [InlineData("09:10", "22:00", "monday.opens")]
    [InlineData("09:00", "21:45", "monday.closes")]
    public void Times_go_in_steps_of_30_minutes(string opens, string closes, string field)
    {
        var errors = Errors(Week(monday: Open(opens, closes)));

        errors.Keys.ShouldBe([field]);
        errors[field].ShouldBe(["As horas vão de meia em meia hora, por exemplo 09:00 ou 09:30."]);
    }

    [Theory]
    [InlineData("22:00", "02:00")] // past midnight: a known T04 limitation
    [InlineData("10:00", "09:00")]
    [InlineData("10:00", "10:00")]
    [InlineData("00:30", "00:30")]
    public void Closing_must_come_after_opening(string opens, string closes)
    {
        Errors(Week(monday: Open(opens, closes)))["monday.closes"].ShouldBe([NotAfterOpening]);
    }

    [Theory]
    [InlineData("22:00", "00:00")]
    [InlineData("00:00", "00:00")]
    [InlineData("23:30", "00:00")]
    public void Closing_at_00_00_is_midnight_at_the_end_of_the_day(string opens, string closes)
    {
        Errors(Week(monday: Open(opens, closes))).ShouldBeEmpty();
    }

    [Fact]
    public void Each_field_gets_one_message_and_the_order_rule_waits_for_valid_times()
    {
        var errors = Errors(Week(monday: Open("10:15", "09:00")));

        errors.Keys.ShouldBe(["monday.opens"]);
        errors["monday.opens"].Length.ShouldBe(1);
    }

    [Fact]
    public void The_week_maps_open_days_to_hours_and_closed_days_to_null()
    {
        var week = Week(monday: Open("09:00", "00:00")).Week();

        week.Count.ShouldBe(7);
        week[DayOfWeek.Monday]!.Opens.ShouldBe(new TimeOnly(9, 0));
        week[DayOfWeek.Monday]!.ClosesAtMidnight.ShouldBeTrue();
        week[DayOfWeek.Sunday].ShouldBeNull();
    }
}
