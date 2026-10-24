using SportsBooking.Application.Notifications;

namespace SportsBooking.UnitTests.Notifications;

/// <summary>US22: what the booking emails say, in pt-PT, with Lisbon times and pt-PT prices.</summary>
public sealed class BookingEmailComposerTests
{
    private static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    /// <summary>Monday 4 May 2026, 10:00-11:00 in Lisbon (WEST, UTC+1).</summary>
    private static BookingEmailPayload Payload(string? reason = null, decimal price = 18.5m, DateTime? startsAt = null, DateTime? endsAt = null) => new(
        Guid.CreateVersion7(),
        "Clube da Foz",
        "Rua do Mar 1",
        "Porto",
        "Europe/Lisbon",
        "Padel 1",
        startsAt ?? Utc(2026, 5, 4, 9),
        endsAt ?? Utc(2026, 5, 4, 10),
        price,
        reason);

    [Fact]
    public void The_confirmation_has_the_club_address_pitch_date_time_and_price_in_portuguese()
    {
        var email = BookingEmailComposer.Compose(OutboxMessageTypes.BookingConfirmed, Payload(), "Rita Jogadora");

        email.Subject.ShouldBe("Reserva confirmada: Clube da Foz, 04/05/2026 às 10:00");
        email.TextBody.ShouldStartWith("Olá Rita Jogadora,");
        email.TextBody.ShouldContain("A tua reserva está confirmada.");
        email.TextBody.ShouldContain("Clube: Clube da Foz");
        email.TextBody.ShouldContain("Morada: Rua do Mar 1, Porto");
        email.TextBody.ShouldContain("Campo: Padel 1");
        email.TextBody.ShouldContain("Data: segunda-feira, 4 de maio de 2026");
        email.TextBody.ShouldContain("Hora: 10:00 – 11:00");
        email.TextBody.ShouldContain("Preço: 18,50 €");
        email.TextBody.ShouldNotContain("Motivo");
    }

    [Fact]
    public void The_html_version_has_the_same_lines()
    {
        var email = BookingEmailComposer.Compose(OutboxMessageTypes.BookingConfirmed, Payload(), "Rita Jogadora");

        email.HtmlBody.ShouldStartWith("<!DOCTYPE html><html lang=\"pt-PT\">");
        foreach (var (label, value) in new[]
                 {
                     ("Clube", "Clube da Foz"), ("Morada", "Rua do Mar 1, Porto"), ("Campo", "Padel 1"), ("Data", "segunda-feira, 4 de maio de 2026"),
                     ("Hora", "10:00 – 11:00"), ("Preço", "18,50 €"),
                 })
        {
            email.HtmlBody.ShouldContain($"<tr><th align=\"left\">{label}</th><td>{value}</td></tr>");
        }

        email.HtmlBody.ShouldContain("Olá Rita Jogadora,");
    }

    [Fact]
    public void Text_typed_by_people_is_escaped_in_the_html()
    {
        var payload = Payload() with { ClubName = "Clube <b>&</b>", PitchName = "<script>alert(1)</script>" };

        var email = BookingEmailComposer.Compose(OutboxMessageTypes.BookingConfirmed, payload, "Ana \"A\" <Silva>");

        email.HtmlBody.ShouldNotContain("<script>");
        email.HtmlBody.ShouldNotContain("<b>&</b>");
        email.HtmlBody.ShouldContain("&lt;script&gt;alert(1)&lt;/script&gt;");
        email.HtmlBody.ShouldContain("Clube &lt;b&gt;&amp;&lt;/b&gt;");
        email.HtmlBody.ShouldContain("Olá Ana &quot;A&quot; &lt;Silva&gt;,");
        // The plain text is not HTML: it says what was typed.
        email.TextBody.ShouldContain("Campo: <script>alert(1)</script>");
    }

    [Fact]
    public void The_player_cancellation_says_the_player_cancelled_and_has_no_reason_even_if_one_is_in_the_payload()
    {
        var email = BookingEmailComposer.Compose(OutboxMessageTypes.BookingCancelledByPlayer, Payload(reason: "não devia aparecer"), "Rita Jogadora");

        email.Subject.ShouldBe("Reserva cancelada: Clube da Foz, 04/05/2026 às 10:00");
        email.TextBody.ShouldContain("Cancelaste a tua reserva.");
        email.TextBody.ShouldContain("Hora: 10:00 – 11:00");
        email.TextBody.ShouldNotContain("Motivo");
        email.TextBody.ShouldNotContain("não devia aparecer");
        email.HtmlBody.ShouldNotContain("não devia aparecer");
    }

    [Fact]
    public void The_club_cancellation_says_the_club_cancelled_and_gives_the_owners_reason()
    {
        var email = BookingEmailComposer.Compose(OutboxMessageTypes.BookingCancelledByClub, Payload(reason: "Campo em obras"), "Rita Jogadora");

        email.Subject.ShouldBe("O clube cancelou a tua reserva: Clube da Foz, 04/05/2026 às 10:00");
        email.TextBody.ShouldContain("O clube cancelou a tua reserva.");
        email.TextBody.ShouldContain("Motivo: Campo em obras");
        email.HtmlBody.ShouldContain("<tr><th align=\"left\">Motivo</th><td>Campo em obras</td></tr>");
        email.TextBody.ShouldContain("Clube: Clube da Foz");
        email.TextBody.ShouldContain("Data: segunda-feira, 4 de maio de 2026");
    }

    [Fact]
    public void Times_are_in_lisbon_in_winter_too()
    {
        // 2 February 2026, 09:00 UTC = 09:00 WET.
        var email = BookingEmailComposer.Compose(OutboxMessageTypes.BookingConfirmed, Payload(startsAt: Utc(2026, 2, 2, 9), endsAt: Utc(2026, 2, 2, 10, 30)), "Rita");

        email.TextBody.ShouldContain("Data: segunda-feira, 2 de fevereiro de 2026");
        email.TextBody.ShouldContain("Hora: 09:00 – 10:30");
    }

    [Fact]
    public void A_slot_just_after_midnight_utc_in_summer_is_on_the_lisbon_day()
    {
        // 23:30 UTC on 4 May is 00:30 on 5 May in Lisbon.
        var email = BookingEmailComposer.Compose(OutboxMessageTypes.BookingConfirmed, Payload(startsAt: Utc(2026, 5, 4, 23, 30), endsAt: Utc(2026, 5, 5, 0, 30)), "Rita");

        email.Subject.ShouldBe("Reserva confirmada: Clube da Foz, 05/05/2026 às 00:30");
        email.TextBody.ShouldContain("Data: terça-feira, 5 de maio de 2026");
        email.TextBody.ShouldContain("Hora: 00:30 – 01:30");
    }

    [Fact]
    public void On_2026_10_25_the_two_0130_are_told_apart()
    {
        // The clocks go back at 02:00 WEST: 00:30Z is 01:30 WEST (summer), 01:30Z is 01:30 WET (winter).
        var first = BookingEmailComposer.Compose(OutboxMessageTypes.BookingConfirmed, Payload(startsAt: Utc(2026, 10, 25, 0, 30), endsAt: Utc(2026, 10, 25, 1)), "Rita");
        var second = BookingEmailComposer.Compose(OutboxMessageTypes.BookingConfirmed, Payload(startsAt: Utc(2026, 10, 25, 1, 30), endsAt: Utc(2026, 10, 25, 2)), "Rita");

        first.TextBody.ShouldContain("Data: domingo, 25 de outubro de 2026");
        first.TextBody.ShouldContain("Hora: 01:30 (verão) – 01:00 (inverno)");
        first.Subject.ShouldBe("Reserva confirmada: Clube da Foz, 25/10/2026 às 01:30 (verão)");
        second.TextBody.ShouldContain("Hora: 01:30 (inverno) – 02:00");
        second.Subject.ShouldBe("Reserva confirmada: Clube da Foz, 25/10/2026 às 01:30 (inverno)");
    }

    [Fact]
    public void On_2026_10_25_hours_outside_the_repeated_one_have_no_label()
    {
        // 09:00 UTC on 25 October is 09:00 WET.
        var email = BookingEmailComposer.Compose(OutboxMessageTypes.BookingConfirmed, Payload(startsAt: Utc(2026, 10, 25, 9), endsAt: Utc(2026, 10, 25, 10)), "Rita");

        email.TextBody.ShouldContain("Hora: 09:00 – 10:00");
        email.TextBody.ShouldNotContain("verão");
        email.TextBody.ShouldNotContain("inverno");
    }

    [Fact]
    public void On_2026_03_29_the_hour_after_the_gap_is_0200()
    {
        // The clocks go forward at 01:00 WET: 01:00Z is 02:00 WEST.
        var email = BookingEmailComposer.Compose(OutboxMessageTypes.BookingConfirmed, Payload(startsAt: Utc(2026, 3, 29, 1), endsAt: Utc(2026, 3, 29, 2)), "Rita");

        email.TextBody.ShouldContain("Data: domingo, 29 de março de 2026");
        email.TextBody.ShouldContain("Hora: 02:00 – 03:00");
    }

    [Theory]
    [InlineData("18.5", "18,50 €")]
    [InlineData("20", "20,00 €")]
    [InlineData("7.05", "7,05 €")]
    [InlineData("500", "500,00 €")]
    [InlineData("0.5", "0,50 €")]
    public void Prices_are_written_the_portuguese_way(string price, string expected)
    {
        BookingEmailComposer.Price(decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(expected);
    }

    [Fact]
    public void Every_day_and_month_has_its_portuguese_name()
    {
        // Monday 4 May to Sunday 10 May 2026, noon UTC.
        var days = Enumerable.Range(4, 7).Select(day => BookingEmailComposer.LongDate(Utc(2026, 5, day, 12), Lisbon).Split(',')[0]).ToList();
        days.ShouldBe(["segunda-feira", "terça-feira", "quarta-feira", "quinta-feira", "sexta-feira", "sábado", "domingo"]);

        var months = Enumerable.Range(1, 12).Select(month => BookingEmailComposer.LongDate(Utc(2026, month, 15, 12), Lisbon).Split(" de ")[1]).ToList();
        months.ShouldBe(["janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"]);
    }

    [Fact]
    public void A_type_that_is_not_a_booking_email_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => BookingEmailComposer.Compose("something_else", Payload(), "Rita"));
    }

    [Fact]
    public void The_payload_round_trips_and_holds_no_address()
    {
        var payload = Payload(reason: "Campo em obras");

        var json = payload.Serialize();

        BookingEmailPayload.Deserialize(json).ShouldBe(payload);
        json.ShouldNotContain("@");
        json.ShouldContain("\"playerId\"");
        json.ShouldContain("\"clubName\":\"Clube da Foz\"");
    }
}
