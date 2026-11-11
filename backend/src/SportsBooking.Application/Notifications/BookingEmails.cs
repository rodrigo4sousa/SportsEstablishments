using System.Globalization;
using System.Text;
using System.Text.Json;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Notifications;

/// <summary>An email ready to send: plain text and a simple HTML version of the same content.</summary>
public sealed record EmailMessage(string ToAddress, string ToName, string Subject, string TextBody, string HtmlBody);

/// <summary>Sends one email; implemented with SMTP in Infrastructure (Mailpit in development, Brevo in production).</summary>
public interface IEmailSender
{
    /// <summary>Throws on any failure; the outbox worker counts it and tries again later.</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// What a booking email shows (US22), kept in the outbox message. The player is referred to by id: the address and the
/// name are read when sending. Instants are UTC; <see cref="TimeZoneId"/> is the club's, to show them in its local time.
/// <see cref="Reason"/> is set only when the club cancelled.
/// </summary>
public sealed record BookingEmailPayload(
    Guid PlayerId,
    string ClubName,
    string ClubAddress,
    string ClubLocality,
    string TimeZoneId,
    string PitchName,
    DateTime StartsAt,
    DateTime EndsAt,
    decimal Price,
    string? Reason)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static BookingEmailPayload Deserialize(string payload) =>
        JsonSerializer.Deserialize<BookingEmailPayload>(payload, Json) ?? throw new InvalidOperationException("An outbox payload is empty.");
}

/// <summary>The subject and the two bodies of an email, before the recipient is put on it.</summary>
public sealed record EmailContent(string Subject, string TextBody, string HtmlBody);

/// <summary>
/// The booking emails of US22, in the recipient's language (T09: pt-PT by default, or English): the confirmation (club, address, pitch,
/// date, time, price) and the cancellation, saying who cancelled and, when it was the club, why. Dates and times are the club's local
/// ones; inside the repeated hour of the last Sunday of October they say «(verão)» or «(inverno)» («(summer time)», «(winter time)»), as
/// the pages do. Names of days and months are written out here, so the result does not depend on the server's culture data.
/// </summary>
public static class BookingEmailComposer
{
    private static readonly string[] Weekdays = ["domingo", "segunda-feira", "terça-feira", "quarta-feira", "quinta-feira", "sexta-feira", "sábado"];

    private static readonly string[] Months =
        ["janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"];

    private static readonly string[] EnglishWeekdays = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    private static readonly string[] EnglishMonths =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    public static EmailContent Compose(string type, BookingEmailPayload payload, string recipientName, Language language = Language.Portuguese)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(payload.TimeZoneId);
        var text = new EmailText(language);
        var date = LongDate(payload.StartsAt, timeZone, language);
        var start = ClockTime(payload.StartsAt, timeZone, language);
        var time = $"{start} – {ClockTime(payload.EndsAt, timeZone, language)}";
        var shortDate = ClubTime.ToLocal(payload.StartsAt, timeZone).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var at = text.Pick($"{shortDate} às {start}", $"{shortDate} at {start}");

        var (subject, intro, closing) = type switch
        {
            OutboxMessageTypes.BookingConfirmed => (
                text.Pick($"Reserva confirmada: {payload.ClubName}, {at}", $"Booking confirmed: {payload.ClubName}, {at}"),
                text.Pick("A tua reserva está confirmada.", "Your booking is confirmed."),
                text.Pick(
                    "Se precisares de cancelar, podes fazê-lo em «As minhas reservas», dentro do prazo de cancelamento do clube.",
                    "If you need to cancel, you can do it in “My bookings”, within the club's cancellation window.")),
            OutboxMessageTypes.BookingCancelledByPlayer => (
                text.Pick($"Reserva cancelada: {payload.ClubName}, {at}", $"Booking cancelled: {payload.ClubName}, {at}"),
                text.Pick(
                    "Cancelaste a tua reserva. O horário ficou livre para outros jogadores.",
                    "You cancelled your booking. The time is now free for other players."),
                text.Pick(
                    "Se foi engano, podes voltar a reservar se o horário ainda estiver livre.",
                    "If it was a mistake, you can book again while the time is still free.")),
            OutboxMessageTypes.BookingCancelledByClub => (
                text.Pick($"O clube cancelou a tua reserva: {payload.ClubName}, {at}", $"The club cancelled your booking: {payload.ClubName}, {at}"),
                text.Pick("O clube cancelou a tua reserva.", "The club cancelled your booking."),
                text.Pick("Para mais informações, contacta o clube.", "For more information, contact the club.")),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a booking email."),
        };

        var lines = new List<(string Label, string Value)>
        {
            (text.Pick("Clube", "Club"), payload.ClubName),
            (text.Pick("Morada", "Address"), $"{payload.ClubAddress}, {payload.ClubLocality}"),
            (text.Pick("Campo", "Pitch"), payload.PitchName),
            (text.Pick("Data", "Date"), date),
            (text.Pick("Hora", "Time"), time),
            (text.Pick("Preço", "Price"), Price(payload.Price, language)),
        };
        if (type == OutboxMessageTypes.BookingCancelledByClub && !string.IsNullOrWhiteSpace(payload.Reason))
        {
            // Written by the club in its own words: never translated.
            lines.Add((text.Pick("Motivo", "Reason"), payload.Reason));
        }

        return EmailLayout.Table(text, subject, text.Greeting(recipientName), intro, lines, closing);
    }

    /// <summary>"segunda-feira, 4 de maio de 2026" or "Monday, 4 May 2026", in the club's time zone.</summary>
    public static string LongDate(DateTime utc, TimeZoneInfo timeZone, Language language = Language.Portuguese)
    {
        var local = ClubTime.ToLocal(utc, timeZone);
        return language == Language.English
            ? $"{EnglishWeekdays[(int)local.DayOfWeek]}, {local.Day} {EnglishMonths[local.Month - 1]} {local.Year}"
            : $"{Weekdays[(int)local.DayOfWeek]}, {local.Day} de {Months[local.Month - 1]} de {local.Year}";
    }

    /// <summary>
    /// "10:00", or "01:30 (verão)" / "01:30 (inverno)" ("01:30 (summer time)" / "01:30 (winter time)") when that clock time happens twice
    /// that day.
    /// </summary>
    public static string ClockTime(DateTime utc, TimeZoneInfo timeZone, Language language = Language.Portuguese)
    {
        var local = ClubTime.ToLocal(utc, timeZone);
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (!timeZone.IsAmbiguousTime(local))
        {
            return time;
        }

        // The first occurrence is summer time (an offset above the zone's standard one), the second is winter time.
        var text = new EmailText(language);
        return timeZone.GetUtcOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)) > timeZone.BaseUtcOffset
            ? $"{time} {text.Pick("(verão)", "(summer time)")}"
            : $"{time} {text.Pick("(inverno)", "(winter time)")}";
    }

    /// <summary>
    /// "18,50 €" (decimal comma, two decimals) or, in English, "€18.50". Prices are at most 500, so there is no thousands separator.
    /// </summary>
    public static string Price(decimal price, Language language = Language.Portuguese)
    {
        var amount = price.ToString("0.00", CultureInfo.InvariantCulture);
        return language == Language.English ? $"€{amount}" : $"{amount.Replace('.', ',')} €";
    }
}

/// <summary>The HTML and plain-text shape shared by the booking emails: greeting, intro, a table of labelled values and a closing.</summary>
internal static class EmailLayout
{
    public static EmailContent Table(EmailText text, string subject, string greeting, string intro, IReadOnlyList<(string Label, string Value)> lines, string closing)
    {
        var plain = new StringBuilder().AppendLine(greeting).AppendLine().AppendLine(intro).AppendLine();
        foreach (var (label, value) in lines)
        {
            plain.AppendLine(CultureInfo.InvariantCulture, $"{label}: {value}");
        }

        plain.AppendLine().AppendLine(closing).AppendLine().Append(EmailText.SiteName);

        var html = new StringBuilder()
            .Append(text.HtmlStart())
            .Append(CultureInfo.InvariantCulture, $"<p>{Html.Encode(greeting)}</p><p>{Html.Encode(intro)}</p><table cellpadding=\"4\">");
        foreach (var (label, value) in lines)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><th align=\"left\">{Html.Encode(label)}</th><td>{Html.Encode(value)}</td></tr>");
        }

        html.Append(CultureInfo.InvariantCulture, $"</table><p>{Html.Encode(closing)}</p><p>{EmailText.SiteName}</p></body></html>");
        return new EmailContent(subject, plain.ToString(), html.ToString());
    }
}

/// <summary>A booking made in a weekly series (US20): UTC instants and the price it stored.</summary>
public sealed record SeriesBookedLine(DateTime StartsAt, DateTime EndsAt, decimal Price);

/// <summary>A date left out of a weekly series (US20), with its reason code (<c>taken</c>, <c>blocked</c>, …).</summary>
public sealed record SeriesSkippedLine(DateTime StartsAt, DateTime EndsAt, string Reason);

/// <summary>What the series summary shows (US20). The player is referred to by id, like <see cref="BookingEmailPayload"/>.</summary>
public sealed record BookingSeriesEmailPayload(
    Guid PlayerId,
    string ClubName,
    string ClubAddress,
    string ClubLocality,
    string TimeZoneId,
    string PitchName,
    IReadOnlyList<SeriesBookedLine> Booked,
    IReadOnlyList<SeriesSkippedLine> Skipped)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static BookingSeriesEmailPayload Deserialize(string payload) =>
        JsonSerializer.Deserialize<BookingSeriesEmailPayload>(payload, Json) ?? throw new InvalidOperationException("An outbox payload is empty.");
}

/// <summary>
/// US20: the one email of a weekly series, in the recipient's language (T09) and the club's local time, with the same date, time and price
/// formats.
/// </summary>
public static class BookingSeriesEmailComposer
{
    /// <summary>The reason a date was left out, in words.</summary>
    public static string ReasonText(string reason, Language language = Language.Portuguese)
    {
        var text = new EmailText(language);
        return reason switch
        {
            "taken" => text.Pick("já está reservado", "is already booked"),
            "blocked" => text.Pick("está bloqueado pelo clube", "is blocked by the club"),
            "closed" => text.Pick("o campo está fechado nesse dia", "the pitch is closed that day"),
            "not_a_slot" => text.Pick("não é um horário do campo nesse dia", "is not one of the pitch's times that day"),
            "outside_window" => text.Pick("fica fora do período de reservas (até 30 dias)", "is outside the booking period (up to 30 days ahead)"),
            "started" => text.Pick("já começou", "has already started"),
            "pitch_inactive" => text.Pick("o campo deixou de aceitar reservas", "the pitch no longer takes bookings"),
            _ => text.Pick("não está disponível", "is not available"),
        };
    }

    public static EmailContent Compose(BookingSeriesEmailPayload payload, string recipientName, Language language = Language.Portuguese)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(payload.TimeZoneId);
        var text = new EmailText(language);
        string When(DateTime startsAt, DateTime endsAt) =>
            $"{BookingEmailComposer.LongDate(startsAt, timeZone, language)}, {BookingEmailComposer.ClockTime(startsAt, timeZone, language)} – {BookingEmailComposer.ClockTime(endsAt, timeZone, language)}";

        var count = payload.Booked.Count;
        var subject = text.Pick(
            $"Reservas semanais confirmadas: {payload.ClubName}, {count} {(count == 1 ? "data" : "datas")}",
            $"Weekly bookings confirmed: {payload.ClubName}, {count} {(count == 1 ? "date" : "dates")}");
        var greeting = text.Greeting(recipientName);
        var intro = count == 1
            ? text.Pick("A tua reserva semanal está confirmada para esta data:", "Your weekly booking is confirmed for this date:")
            : text.Pick($"As tuas reservas semanais estão confirmadas para estas {count} datas:", $"Your weekly bookings are confirmed for these {count} dates:");
        var place = new List<(string Label, string Value)>
        {
            (text.Pick("Clube", "Club"), payload.ClubName),
            (text.Pick("Morada", "Address"), $"{payload.ClubAddress}, {payload.ClubLocality}"),
            (text.Pick("Campo", "Pitch"), payload.PitchName),
        };
        var booked = payload.Booked.Select(line => $"{When(line.StartsAt, line.EndsAt)} · {BookingEmailComposer.Price(line.Price, language)}").ToList();
        var skipped = payload.Skipped.Select(line => $"{When(line.StartsAt, line.EndsAt)}: {ReasonText(line.Reason, language)}").ToList();
        var skippedIntro = text.Pick("Estas datas ficaram de fora e não foram reservadas:", "These dates were left out and not booked:");
        var closing = text.Pick(
            "Cada reserva é independente: podes cancelar qualquer uma delas em «As minhas reservas», dentro do prazo de cancelamento do clube.",
            "Each booking stands on its own: you can cancel any of them in “My bookings”, within the club's cancellation window.");

        var plain = new StringBuilder().AppendLine(greeting).AppendLine().AppendLine(intro).AppendLine();
        foreach (var line in booked)
        {
            plain.AppendLine(CultureInfo.InvariantCulture, $"- {line}");
        }

        plain.AppendLine();
        foreach (var (label, value) in place)
        {
            plain.AppendLine(CultureInfo.InvariantCulture, $"{label}: {value}");
        }

        if (skipped.Count > 0)
        {
            plain.AppendLine().AppendLine(skippedIntro);
            foreach (var line in skipped)
            {
                plain.AppendLine(CultureInfo.InvariantCulture, $"- {line}");
            }
        }

        plain.AppendLine().AppendLine(closing).AppendLine().Append(EmailText.SiteName);

        var html = new StringBuilder()
            .Append(text.HtmlStart())
            .Append(CultureInfo.InvariantCulture, $"<p>{Html.Encode(greeting)}</p><p>{Html.Encode(intro)}</p><ul>");
        foreach (var line in booked)
        {
            html.Append(CultureInfo.InvariantCulture, $"<li>{Html.Encode(line)}</li>");
        }

        html.Append("</ul><table cellpadding=\"4\">");
        foreach (var (label, value) in place)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><th align=\"left\">{Html.Encode(label)}</th><td>{Html.Encode(value)}</td></tr>");
        }

        html.Append("</table>");
        if (skipped.Count > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p>{Html.Encode(skippedIntro)}</p><ul>");
            foreach (var line in skipped)
            {
                html.Append(CultureInfo.InvariantCulture, $"<li>{Html.Encode(line)}</li>");
            }

            html.Append("</ul>");
        }

        html.Append(CultureInfo.InvariantCulture, $"<p>{Html.Encode(closing)}</p><p>{EmailText.SiteName}</p></body></html>");
        return new EmailContent(subject, plain.ToString(), html.ToString());
    }
}
