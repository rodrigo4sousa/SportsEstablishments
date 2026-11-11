using SportsBooking.Application.Notifications;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.UnitTests.Notifications;

/// <summary>
/// T09: every email also exists in English, with British dates ("Monday, 4 May 2026"), Lisbon times, "(summer time)"/"(winter time)" in the
/// repeated October hour and "€18.50". The Portuguese texts are covered by the other composer tests, which call the composers without a
/// language (Portuguese is the default).
/// </summary>
public sealed class EnglishEmailComposerTests
{
    private static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static BookingEmailPayload Payload(string? reason = null) => new(
        Guid.CreateVersion7(), "Clube da Foz", "Rua do Mar 1", "Porto", "Europe/Lisbon", "Padel 1", Utc(2026, 5, 4, 9), Utc(2026, 5, 4, 10), 18.5m, reason);

    private static BookingNotificationDetails Details() => new(
        "Clube da Foz", "Rua do Mar 1", "Porto", "Europe/Lisbon", "Padel 1", Utc(2026, 5, 4, 9), Utc(2026, 5, 4, 10), "Rita Jogadora");

    [Fact]
    public void The_confirmation_in_english()
    {
        var email = BookingEmailComposer.Compose(OutboxMessageTypes.BookingConfirmed, Payload(), "Emma Player", Language.English);

        email.Subject.ShouldBe("Booking confirmed: Clube da Foz, 04/05/2026 at 10:00");
        email.TextBody.ShouldStartWith("Hello Emma Player,");
        email.TextBody.ShouldContain("Your booking is confirmed.");
        email.TextBody.ShouldContain("Club: Clube da Foz");
        email.TextBody.ShouldContain("Address: Rua do Mar 1, Porto");
        email.TextBody.ShouldContain("Pitch: Padel 1");
        email.TextBody.ShouldContain("Date: Monday, 4 May 2026");
        email.TextBody.ShouldContain("Time: 10:00 – 11:00");
        email.TextBody.ShouldContain("Price: €18.50");
        email.TextBody.ShouldContain("“My bookings”");
        email.TextBody.ShouldEndWith("Reservas de Campos");
        email.HtmlBody.ShouldStartWith("<!DOCTYPE html><html lang=\"en\">");
        email.HtmlBody.ShouldContain("<tr><th align=\"left\">Price</th><td>€18.50</td></tr>");
    }

    [Fact]
    public void The_cancellations_in_english_keep_the_clubs_reason_as_written()
    {
        var byPlayer = BookingEmailComposer.Compose(OutboxMessageTypes.BookingCancelledByPlayer, Payload(), "Emma", Language.English);
        var byClub = BookingEmailComposer.Compose(OutboxMessageTypes.BookingCancelledByClub, Payload("Campo em obras"), "Emma", Language.English);

        byPlayer.Subject.ShouldBe("Booking cancelled: Clube da Foz, 04/05/2026 at 10:00");
        byPlayer.TextBody.ShouldContain("You cancelled your booking.");
        byClub.Subject.ShouldBe("The club cancelled your booking: Clube da Foz, 04/05/2026 at 10:00");
        byClub.TextBody.ShouldContain("Reason: Campo em obras");
    }

    [Fact]
    public void Dates_times_and_prices_in_english()
    {
        BookingEmailComposer.LongDate(Utc(2026, 8, 1, 12), Lisbon, Language.English).ShouldBe("Saturday, 1 August 2026");
        BookingEmailComposer.Price(7m, Language.English).ShouldBe("€7.00");
        BookingEmailComposer.Price(7m).ShouldBe("7,00 €");

        // 25 October 2026: 01:30 happens at 00:30 UTC (summer time) and again at 01:30 UTC (winter time).
        BookingEmailComposer.ClockTime(Utc(2026, 10, 25, 0, 30), Lisbon, Language.English).ShouldBe("01:30 (summer time)");
        BookingEmailComposer.ClockTime(Utc(2026, 10, 25, 1, 30), Lisbon, Language.English).ShouldBe("01:30 (winter time)");
        BookingEmailComposer.ClockTime(Utc(2026, 10, 25, 0, 30), Lisbon).ShouldBe("01:30 (verão)");
    }

    [Fact]
    public void The_owner_email_and_the_reminder_in_english()
    {
        var owner = BookingNotificationComposer.OwnerNewBooking(Details(), "Rui Dono", Language.English);
        var reminder = BookingNotificationComposer.Reminder(Details(), "Rita Jogadora", Language.English);

        owner.Subject.ShouldBe("New booking: Clube da Foz, 04/05/2026 at 10:00");
        owner.TextBody.ShouldStartWith("Hello Rui Dono,");
        owner.TextBody.ShouldContain("Player: Rita Jogadora");
        owner.TextBody.ShouldContain("You can turn these emails off in the club's settings.");
        reminder.Subject.ShouldBe("Reminder: your booking starts at 10:00, Clube da Foz");
        reminder.TextBody.ShouldContain("Your booking starts in 3 hours.");
        reminder.HtmlBody.ShouldStartWith("<!DOCTYPE html><html lang=\"en\">");
    }

    [Fact]
    public void The_series_summary_in_english()
    {
        var payload = new BookingSeriesEmailPayload(
            Guid.CreateVersion7(), "Clube da Foz", "Rua do Mar 1", "Porto", "Europe/Lisbon", "Padel 1",
            [new SeriesBookedLine(Utc(2026, 5, 4, 9), Utc(2026, 5, 4, 10), 18.5m), new SeriesBookedLine(Utc(2026, 5, 11, 9), Utc(2026, 5, 11, 10), 18.5m)],
            [new SeriesSkippedLine(Utc(2026, 5, 18, 9), Utc(2026, 5, 18, 10), "taken")]);

        var email = BookingSeriesEmailComposer.Compose(payload, "Emma", Language.English);

        email.Subject.ShouldBe("Weekly bookings confirmed: Clube da Foz, 2 dates");
        email.TextBody.ShouldContain("Your weekly bookings are confirmed for these 2 dates:");
        email.TextBody.ShouldContain("- Monday, 4 May 2026, 10:00 – 11:00 · €18.50");
        email.TextBody.ShouldContain("These dates were left out and not booked:");
        email.TextBody.ShouldContain("- Monday, 18 May 2026, 10:00 – 11:00: is already booked");
        BookingSeriesEmailComposer.ReasonText("something-new", Language.English).ShouldBe("is not available");
    }

    [Fact]
    public void The_password_emails_in_english()
    {
        var reset = AccountEmailComposer.PasswordReset("Emma", "https://site.test/reset-password?email=a&token=b", TimeSpan.FromHours(2), Language.English);
        var changed = AccountEmailComposer.PasswordChanged("Emma", Utc(2026, 5, 4, 13, 5), Language.English);

        reset.Subject.ShouldBe("Set a new password");
        reset.TextBody.ShouldContain("It works only once and for 2 hours:");
        reset.HtmlBody.ShouldContain(">Set a new password</a>");
        changed.Subject.ShouldBe("Your password was changed");
        changed.TextBody.ShouldContain("was changed on Monday, 4 May 2026, at 14:05 (Lisbon time).");
    }
}
