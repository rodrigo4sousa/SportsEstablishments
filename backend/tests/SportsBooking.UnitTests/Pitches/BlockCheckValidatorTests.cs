using System.Globalization;
using SportsBooking.Application.Common;
using SportsBooking.Application.Pitches;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.UnitTests.Pitches;

/// <summary>US09: block field rules, with pt-PT messages keyed by JSON field name.</summary>
public sealed class BlockCheckValidatorTests
{
    private const string BadFormat = "Indica a data e a hora no formato AAAA-MM-DDTHH:mm, por exemplo 2026-10-25T09:00.";
    private const string OffStep = "As horas vão de meia em meia hora, por exemplo 09:00 ou 09:30.";

    // 10:00 UTC on 1 May 2026 (11:00 in Lisbon).
    private static readonly DateTime Now = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyDictionary<string, string[]> Errors(string start, string end, string? reason = null, DateTime? now = null) =>
        new BlockCheckValidator().Validate(new BlockCheck(new BlockRequest(start, end, reason).Normalized(), now ?? Now, ClubTimeZones.Lisbon)).ToFieldErrors();

    [Fact]
    public void A_future_period_in_steps_of_30_minutes_is_valid()
    {
        Errors("2026-05-04T09:00", "2026-05-04T11:30").ShouldBeEmpty();
        Errors("2026-05-04T09:00", "2026-05-04T11:30", "Manutenção").ShouldBeEmpty();
    }

    [Fact]
    public void Start_and_end_are_required()
    {
        var errors = Errors("", "  ");

        errors["start"].ShouldBe(["Indica o início do bloqueio."]);
        errors["end"].ShouldBe(["Indica o fim do bloqueio."]);
        errors.ContainsKey("reason").ShouldBeFalse();
    }

    [Theory]
    [InlineData("2026-05-04 09:00")]
    [InlineData("2026-05-04T9:00")]
    [InlineData("2026-05-04T09:00:00")]
    [InlineData("2026-05-04T09:00Z")]
    [InlineData("04/05/2026")]
    [InlineData("2026-02-30T09:00")]
    [InlineData("amanha")]
    public void Dates_must_be_yyyy_mm_dd_t_hh_mm(string start)
    {
        Errors(start, "2026-05-04T11:00")["start"].ShouldBe([BadFormat]);
    }

    [Theory]
    [InlineData("2026-05-04T09:15")]
    [InlineData("2026-05-04T09:45")]
    [InlineData("2026-05-04T09:01")]
    public void Times_go_in_steps_of_30_minutes(string start)
    {
        Errors(start, "2026-05-04T11:00")["start"].ShouldBe([OffStep]);
        Errors("2026-05-04T09:00", start.Replace("T09", "T12", StringComparison.Ordinal))["end"].ShouldBe([OffStep]);
    }

    [Fact]
    public void The_start_cannot_be_in_the_past()
    {
        // 09:30 WEST on 1 May is 08:30Z, before Now (10:00Z).
        Errors("2026-05-01T09:30", "2026-05-01T14:00")["start"].ShouldBe(["O início não pode estar no passado."]);
        Errors("2026-05-01T11:00", "2026-05-01T14:00").ShouldBeEmpty(); // right now (11:00 WEST) is not the past
        Errors("2026-05-01T11:30", "2026-05-01T14:00").ShouldBeEmpty();
    }

    [Fact]
    public void The_start_is_compared_in_utc_after_converting_from_lisbon_time()
    {
        // 10:00 UTC is 11:00 in Lisbon: a local 10:30 is already past, though 10:30 would be after 10:00 if read as UTC.
        Errors("2026-05-01T10:30", "2026-05-01T14:00")["start"].ShouldBe(["O início não pode estar no passado."]);
    }

    [Theory]
    [InlineData("2026-05-04T10:00", "2026-05-04T09:00")]
    [InlineData("2026-05-04T10:00", "2026-05-04T10:00")]
    [InlineData("2026-05-04T10:00", "2026-05-03T12:00")]
    public void The_end_must_be_after_the_start(string start, string end)
    {
        Errors(start, end)["end"].ShouldBe(["O fim tem de ser depois do início."]);
    }

    [Fact]
    public void Ending_after_midnight_is_fine_because_a_block_is_not_opening_hours()
    {
        Errors("2026-05-04T22:00", "2026-05-05T02:00").ShouldBeEmpty();
    }

    [Fact]
    public void The_end_is_at_most_90_days_ahead()
    {
        // 90 days after 10:00Z on 1 May is 10:00Z on 30 July = 11:00 WEST.
        Errors("2026-07-29T10:00", "2026-07-30T11:00").ShouldBeEmpty();
        Errors("2026-07-29T10:00", "2026-07-30T11:30")["end"].ShouldBe(["O bloqueio não pode acabar daqui a mais de 90 dias."]);
    }

    [Fact]
    public void A_clock_change_day_is_read_with_the_t04_rules()
    {
        var now = new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Utc);

        // 01:30 does not exist on 29 March; it becomes 02:30 WEST, which is 01:30Z, so 03:00 WEST (02:00Z) is after it.
        Errors("2026-03-29T01:30", "2026-03-29T03:00", now: now).ShouldBeEmpty();
        // 02:00 WEST and 01:30 (moved to 02:30 WEST) : the end 02:00 WEST (01:00Z) is before the start 01:30Z.
        Errors("2026-03-29T01:30", "2026-03-29T02:00", now: now)["end"].ShouldBe(["O fim tem de ser depois do início."]);
    }

    [Fact]
    public void The_reason_is_at_most_200_characters_after_trimming()
    {
        Errors("2026-05-04T09:00", "2026-05-04T10:00", new string('a', 200)).ShouldBeEmpty();
        Errors("2026-05-04T09:00", "2026-05-04T10:00", $"  {new string('a', 200)}  ").ShouldBeEmpty();
        Errors("2026-05-04T09:00", "2026-05-04T10:00", new string('a', 201))["reason"].ShouldBe(["O motivo pode ter no máximo 200 caracteres."]);
    }

    [Fact]
    public void The_converted_instants_follow_clubtime()
    {
        var check = new BlockCheck(new BlockRequest("2026-10-25T01:30", "2026-10-25T02:30"), Now, ClubTimeZones.Lisbon);

        check.StartUtc.ShouldBe(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc));
        check.EndUtc.ShouldBe(new DateTime(2026, 10, 25, 2, 30, 0, DateTimeKind.Utc));
        check.ToUtc("nope").ShouldBeNull();
        DateTime.Parse("2026-05-04", CultureInfo.InvariantCulture).Kind.ShouldNotBe(DateTimeKind.Utc);
    }
}
