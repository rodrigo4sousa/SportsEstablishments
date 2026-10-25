using SportsBooking.Application.Notifications;
using SportsBooking.Domain.Bookings;

namespace SportsBooking.UnitTests.Notifications;

public sealed class BookingNotificationComposerTests
{
    private static BookingNotificationDetails Details(DateTime startsAt, string player = "Rita Jogadora", string club = "Clube da Foz") => new(
        club, "Rua do Mar 1", "Porto", "Europe/Lisbon", "Padel 1", startsAt, startsAt.AddHours(1), player);

    private static readonly DateTime Summer = new(2026, 5, 4, 9, 0, 0, DateTimeKind.Utc); // 10:00 in Lisbon

    [Fact]
    public void The_owner_email_has_club_pitch_date_time_in_lisbon_and_the_players_name()
    {
        var email = BookingNotificationComposer.OwnerNewBooking(Details(Summer), "Dona Clube");

        email.Subject.ShouldBe("Nova reserva: Clube da Foz, 04/05/2026 às 10:00");
        email.TextBody.ShouldStartWith("Olá Dona Clube,");
        email.TextBody.ShouldContain("Clube: Clube da Foz");
        email.TextBody.ShouldContain("Campo: Padel 1");
        email.TextBody.ShouldContain("Data: segunda-feira, 4 de maio de 2026");
        email.TextBody.ShouldContain("Hora: 10:00 – 11:00");
        email.TextBody.ShouldContain("Jogador: Rita Jogadora");
        email.TextBody.ShouldContain("Podes desligar estes emails nas definições do clube.");
        email.HtmlBody.ShouldContain("<th align=\"left\">Jogador</th><td>Rita Jogadora</td>");
    }

    [Fact]
    public void The_reminder_has_club_address_pitch_date_and_time()
    {
        var email = BookingNotificationComposer.Reminder(Details(Summer), "Rita Jogadora");

        email.Subject.ShouldBe("Lembrete: a tua reserva começa às 10:00, Clube da Foz");
        email.TextBody.ShouldStartWith("Olá Rita Jogadora,");
        email.TextBody.ShouldContain("A tua reserva começa dentro de 3 horas.");
        email.TextBody.ShouldContain("Morada: Rua do Mar 1, Porto");
        email.TextBody.ShouldContain("Campo: Padel 1");
        email.TextBody.ShouldContain("Hora: 10:00 – 11:00");
    }

    [Fact]
    public void Names_with_html_are_escaped_in_the_html_and_kept_in_the_text()
    {
        var email = BookingNotificationComposer.OwnerNewBooking(Details(Summer, player: "Ana <b>&</b>", club: "Clube \"A\""), "Dono");

        email.HtmlBody.ShouldContain("Ana &lt;b&gt;&amp;&lt;/b&gt;");
        email.HtmlBody.ShouldNotContain("<b>");
        email.TextBody.ShouldContain("Jogador: Ana <b>&</b>");
    }

    [Fact]
    public void The_repeated_hour_of_october_is_told_apart()
    {
        // 00:30 UTC on 25 October 2026 is 01:30 summer time, and 01:30 UTC is 01:30 winter time.
        BookingNotificationComposer.Reminder(Details(new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc)), "Rita").Subject.ShouldContain("01:00 (verão)");
        BookingNotificationComposer.Reminder(Details(new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc)), "Rita").Subject.ShouldContain("01:00 (inverno)");
    }

    [Fact]
    public void The_payload_holds_the_booking_id_and_nothing_else()
    {
        var id = Guid.CreateVersion7();

        var json = new BookingRefPayload(id).Serialize();

        json.ShouldBe($"{{\"bookingId\":\"{id}\"}}");
        BookingRefPayload.Deserialize(json).BookingId.ShouldBe(id);
    }
}

public sealed class BookingReminderRulesTests
{
    private static readonly DateTime Start = new(2026, 5, 4, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_reminder_is_three_hours_before_the_start()
    {
        BookingReminderRules.Lead.ShouldBe(TimeSpan.FromHours(3));
        BookingReminderRules.DueAt(Start, Start.AddDays(-1)).ShouldBe(Start.AddHours(-3));
    }

    [Theory]
    [InlineData(-180, 0)] // exactly 3 hours ahead: provisional, none
    [InlineData(-179, 0)]
    [InlineData(-60, 0)]
    [InlineData(0, 0)] // started
    [InlineData(60, 0)] // over
    public void A_booking_made_three_hours_ahead_or_less_gets_none(int minutesFromStart, int _)
    {
        BookingReminderRules.DueAt(Start, Start.AddMinutes(minutesFromStart)).ShouldBeNull();
    }

    [Fact]
    public void One_second_more_than_three_hours_ahead_gets_one_due_a_second_from_now()
    {
        var now = Start.AddHours(-3).AddSeconds(-1);

        BookingReminderRules.DueAt(Start, now).ShouldBe(now.AddSeconds(1));
    }

    [Fact]
    public void The_due_instant_is_elapsed_time_across_the_october_clock_change()
    {
        // 01:00 UTC on 25 October 2026 is the repeated 01:00 (winter): three elapsed hours before it is 22:00 UTC the day before.
        var start = new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc);

        BookingReminderRules.DueAt(start, new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc)).ShouldBe(new DateTime(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc));
    }
}

public sealed class NoShowRulesTests
{
    private static readonly DateTime Start = new(2026, 5, 4, 8, 0, 0, DateTimeKind.Utc);

    private static Booking Confirmed() => Booking.ForPlayer(Guid.CreateVersion7(), Guid.CreateVersion7(), Start, Start.AddHours(1), 20m, Start.AddDays(-2));

    [Fact]
    public void Not_confirmed_is_reported_before_not_ended()
    {
        var booking = Confirmed();
        booking.CancelByClub("Obras", Start.AddDays(-1));

        booking.RecordNoShow(Start.AddMinutes(10)).ShouldBe(NoShowOutcome.NotConfirmed);
        booking.Status.ShouldBe(BookingStatus.CancelledByClub);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(59)]
    public void A_booking_that_has_not_ended_is_not_marked(int minutesAfterStart)
    {
        var booking = Confirmed();

        booking.RecordNoShow(Start.AddMinutes(minutesAfterStart)).ShouldBe(NoShowOutcome.NotEnded);
        booking.Status.ShouldBe(BookingStatus.Confirmed);
    }

    [Fact]
    public void The_end_itself_counts_as_ended_and_a_second_mark_is_refused()
    {
        var booking = Confirmed();

        booking.RecordNoShow(Start.AddHours(1)).ShouldBe(NoShowOutcome.Marked);
        booking.Status.ShouldBe(BookingStatus.NoShow);
        booking.RecordNoShow(Start.AddHours(2)).ShouldBe(NoShowOutcome.NotConfirmed);
    }

    [Fact]
    public void Only_a_no_show_can_be_taken_back_and_it_is_confirmed_again()
    {
        var booking = Confirmed();
        booking.UndoNoShow().ShouldBeFalse();
        booking.RecordNoShow(Start.AddHours(2));

        booking.UndoNoShow().ShouldBeTrue();

        booking.Status.ShouldBe(BookingStatus.Confirmed);
        booking.UndoNoShow().ShouldBeFalse();
    }

    [Fact]
    public void A_cancelled_booking_cannot_be_taken_back_as_a_no_show()
    {
        var booking = Confirmed();
        booking.CancelByClub("Obras", Start.AddDays(-1));

        booking.UndoNoShow().ShouldBeFalse();
        booking.Status.ShouldBe(BookingStatus.CancelledByClub);
    }
}
