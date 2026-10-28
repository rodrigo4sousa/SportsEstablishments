using System.Globalization;
using System.Text;
using System.Text.Json;
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
/// The booking emails of US22 in Portuguese (pt-PT): the confirmation (club, address, pitch, date, time, price) and the
/// cancellation, saying who cancelled and, when it was the club, why. Dates and times are the club's local ones; inside
/// the repeated hour of the last Sunday of October they say «(verão)» or «(inverno)», as the pages do. Names of days and
/// months are written out here, so the result does not depend on the server's culture data.
/// </summary>
public static class BookingEmailComposer
{
    private static readonly string[] Weekdays = ["domingo", "segunda-feira", "terça-feira", "quarta-feira", "quinta-feira", "sexta-feira", "sábado"];

    private static readonly string[] Months =
        ["janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"];

    public static EmailContent Compose(string type, BookingEmailPayload payload, string recipientName)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(payload.TimeZoneId);
        var date = LongDate(payload.StartsAt, timeZone);
        var time = $"{ClockTime(payload.StartsAt, timeZone)} – {ClockTime(payload.EndsAt, timeZone)}";
        var shortDate = ClubTime.ToLocal(payload.StartsAt, timeZone).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        var (subject, intro, closing) = type switch
        {
            OutboxMessageTypes.BookingConfirmed => (
                $"Reserva confirmada: {payload.ClubName}, {shortDate} às {ClockTime(payload.StartsAt, timeZone)}",
                "A tua reserva está confirmada.",
                "Se precisares de cancelar, podes fazê-lo em «As minhas reservas», dentro do prazo de cancelamento do clube."),
            OutboxMessageTypes.BookingCancelledByPlayer => (
                $"Reserva cancelada: {payload.ClubName}, {shortDate} às {ClockTime(payload.StartsAt, timeZone)}",
                "Cancelaste a tua reserva. O horário ficou livre para outros jogadores.",
                "Se foi engano, podes voltar a reservar se o horário ainda estiver livre."),
            OutboxMessageTypes.BookingCancelledByClub => (
                $"O clube cancelou a tua reserva: {payload.ClubName}, {shortDate} às {ClockTime(payload.StartsAt, timeZone)}",
                "O clube cancelou a tua reserva.",
                "Para mais informações, contacta o clube."),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a booking email."),
        };

        var lines = new List<(string Label, string Value)>
        {
            ("Clube", payload.ClubName),
            ("Morada", $"{payload.ClubAddress}, {payload.ClubLocality}"),
            ("Campo", payload.PitchName),
            ("Data", date),
            ("Hora", time),
            ("Preço", Price(payload.Price)),
        };
        if (type == OutboxMessageTypes.BookingCancelledByClub && !string.IsNullOrWhiteSpace(payload.Reason))
        {
            lines.Add(("Motivo", payload.Reason));
        }

        var greeting = $"Olá {recipientName},";
        var text = new StringBuilder()
            .AppendLine(greeting)
            .AppendLine()
            .AppendLine(intro)
            .AppendLine();
        foreach (var (label, value) in lines)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{label}: {value}");
        }

        text.AppendLine().AppendLine(closing).AppendLine().Append("Reservas de Campos");

        var html = new StringBuilder()
            .Append("<!DOCTYPE html><html lang=\"pt-PT\"><body style=\"font-family:sans-serif;line-height:1.5\">")
            .Append(CultureInfo.InvariantCulture, $"<p>{Encode(greeting)}</p><p>{Encode(intro)}</p><table cellpadding=\"4\">");
        foreach (var (label, value) in lines)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><th align=\"left\">{Encode(label)}</th><td>{Encode(value)}</td></tr>");
        }

        html.Append(CultureInfo.InvariantCulture, $"</table><p>{Encode(closing)}</p><p>Reservas de Campos</p></body></html>");

        return new EmailContent(subject, text.ToString(), html.ToString());
    }

    /// <summary>"segunda-feira, 4 de maio de 2026", in the club's time zone.</summary>
    public static string LongDate(DateTime utc, TimeZoneInfo timeZone)
    {
        var local = ClubTime.ToLocal(utc, timeZone);
        return $"{Weekdays[(int)local.DayOfWeek]}, {local.Day} de {Months[local.Month - 1]} de {local.Year}";
    }

    /// <summary>"10:00", or "01:30 (verão)" / "01:30 (inverno)" when that clock time happens twice that day.</summary>
    public static string ClockTime(DateTime utc, TimeZoneInfo timeZone)
    {
        var local = ClubTime.ToLocal(utc, timeZone);
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (!timeZone.IsAmbiguousTime(local))
        {
            return time;
        }

        // The first occurrence is summer time (an offset above the zone's standard one), the second is winter time.
        return timeZone.GetUtcOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)) > timeZone.BaseUtcOffset ? $"{time} (verão)" : $"{time} (inverno)";
    }

    /// <summary>"18,50 €": decimal comma, two decimals (prices are at most 500, so there is no thousands separator).</summary>
    public static string Price(decimal price) =>
        $"{price.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',')} €";

    // Only the characters that mean something in HTML: the email is UTF-8, so accented letters stay as they are.
    private static string Encode(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&#39;", StringComparison.Ordinal);
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

/// <summary>US20: the one email of a weekly series, in pt-PT and the club's local time, with the same date, time and price formats.</summary>
public static class BookingSeriesEmailComposer
{
    /// <summary>The reason a date was left out, in words.</summary>
    public static string ReasonText(string reason) => reason switch
    {
        "taken" => "já está reservado",
        "blocked" => "está bloqueado pelo clube",
        "closed" => "o campo está fechado nesse dia",
        "not_a_slot" => "não é um horário do campo nesse dia",
        "outside_window" => "fica fora do período de reservas (até 30 dias)",
        "started" => "já começou",
        "pitch_inactive" => "o campo deixou de aceitar reservas",
        _ => "não está disponível",
    };

    public static EmailContent Compose(BookingSeriesEmailPayload payload, string recipientName)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(payload.TimeZoneId);
        string When(DateTime startsAt, DateTime endsAt) =>
            $"{BookingEmailComposer.LongDate(startsAt, timeZone)}, {BookingEmailComposer.ClockTime(startsAt, timeZone)} – {BookingEmailComposer.ClockTime(endsAt, timeZone)}";

        var count = payload.Booked.Count;
        var subject = $"Reservas semanais confirmadas: {payload.ClubName}, {count} {(count == 1 ? "data" : "datas")}";
        var greeting = $"Olá {recipientName},";
        var intro = count == 1 ? "A tua reserva semanal está confirmada para esta data:" : $"As tuas reservas semanais estão confirmadas para estas {count} datas:";
        var place = new List<(string Label, string Value)>
        {
            ("Clube", payload.ClubName),
            ("Morada", $"{payload.ClubAddress}, {payload.ClubLocality}"),
            ("Campo", payload.PitchName),
        };
        var booked = payload.Booked.Select(line => $"{When(line.StartsAt, line.EndsAt)} · {BookingEmailComposer.Price(line.Price)}").ToList();
        var skipped = payload.Skipped.Select(line => $"{When(line.StartsAt, line.EndsAt)}: {ReasonText(line.Reason)}").ToList();
        const string SkippedIntro = "Estas datas ficaram de fora e não foram reservadas:";
        const string Closing = "Cada reserva é independente: podes cancelar qualquer uma delas em «As minhas reservas», dentro do prazo de cancelamento do clube.";

        var text = new StringBuilder().AppendLine(greeting).AppendLine().AppendLine(intro).AppendLine();
        foreach (var line in booked)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {line}");
        }

        text.AppendLine();
        foreach (var (label, value) in place)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{label}: {value}");
        }

        if (skipped.Count > 0)
        {
            text.AppendLine().AppendLine(SkippedIntro);
            foreach (var line in skipped)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {line}");
            }
        }

        text.AppendLine().AppendLine(Closing).AppendLine().Append("Reservas de Campos");

        var html = new StringBuilder()
            .Append("<!DOCTYPE html><html lang=\"pt-PT\"><body style=\"font-family:sans-serif;line-height:1.5\">")
            .Append(CultureInfo.InvariantCulture, $"<p>{Encode(greeting)}</p><p>{Encode(intro)}</p><ul>");
        foreach (var line in booked)
        {
            html.Append(CultureInfo.InvariantCulture, $"<li>{Encode(line)}</li>");
        }

        html.Append("</ul><table cellpadding=\"4\">");
        foreach (var (label, value) in place)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><th align=\"left\">{Encode(label)}</th><td>{Encode(value)}</td></tr>");
        }

        html.Append("</table>");
        if (skipped.Count > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p>{Encode(SkippedIntro)}</p><ul>");
            foreach (var line in skipped)
            {
                html.Append(CultureInfo.InvariantCulture, $"<li>{Encode(line)}</li>");
            }

            html.Append("</ul>");
        }

        html.Append(CultureInfo.InvariantCulture, $"<p>{Encode(Closing)}</p><p>Reservas de Campos</p></body></html>");
        return new EmailContent(subject, text.ToString(), html.ToString());
    }

    private static string Encode(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&#39;", StringComparison.Ordinal);
}
