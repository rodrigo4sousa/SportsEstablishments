using System.Globalization;
using System.Text;
using System.Text.Json;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Notifications;

/// <summary>
/// What the owner's email about a new booking (US23) and the player's reminder (US21) keep in the outbox: the booking id and
/// nothing else. Club, pitch, times and names are read when the message is sent, so they are always the current ones, and no
/// address or name is stored.
/// </summary>
public sealed record BookingRefPayload(Guid BookingId)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static BookingRefPayload Deserialize(string payload) =>
        JsonSerializer.Deserialize<BookingRefPayload>(payload, Json) ?? throw new InvalidOperationException("An outbox payload is empty.");
}

/// <summary>US21 rules (provisional where marked).</summary>
public static class BookingReminderRules
{
    /// <summary>How long before the start the reminder is sent.</summary>
    public static readonly TimeSpan Lead = TimeSpan.FromHours(3);

    /// <summary>
    /// When the reminder of a booking is due: the start minus <see cref="Lead"/>, an instant (no time zone is involved, so the clock
    /// change of the last Sunday of March and October cannot move it); or null when that moment is not in the future, so a
    /// booking made less than 3 hours ahead gets none. Provisional: a booking made exactly 3 hours ahead gets none either.
    /// </summary>
    public static DateTime? DueAt(DateTime startsAtUtc, DateTime nowUtc)
    {
        var due = startsAtUtc - Lead;
        return due > nowUtc ? due : null;
    }
}

/// <summary>What the two emails show: the booking as it is when the email is sent. Instants are UTC; the club's time zone is shown.</summary>
public sealed record BookingNotificationDetails(
    string ClubName,
    string ClubAddress,
    string ClubLocality,
    string TimeZoneId,
    string PitchName,
    DateTime StartsAt,
    DateTime EndsAt,
    string PlayerName);

/// <summary>The emails of US23 (to the owner, with the player's name) and US21 (to the player), in Portuguese (pt-PT).</summary>
public static class BookingNotificationComposer
{
    /// <summary>The owner's email: club, pitch, date, time (Lisbon) and the player's name (and nothing else about the player).</summary>
    public static EmailContent OwnerNewBooking(BookingNotificationDetails booking, string ownerName)
    {
        ArgumentNullException.ThrowIfNull(booking);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(booking.TimeZoneId);
        var shortDate = ClubTime.ToLocal(booking.StartsAt, zone).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var lines = new List<(string Label, string Value)>
        {
            ("Clube", booking.ClubName),
            ("Campo", booking.PitchName),
            ("Data", BookingEmailComposer.LongDate(booking.StartsAt, zone)),
            ("Hora", $"{BookingEmailComposer.ClockTime(booking.StartsAt, zone)} – {BookingEmailComposer.ClockTime(booking.EndsAt, zone)}"),
            ("Jogador", booking.PlayerName),
        };
        return Build(
            $"Nova reserva: {booking.ClubName}, {shortDate} às {BookingEmailComposer.ClockTime(booking.StartsAt, zone)}",
            $"Olá {ownerName},",
            "Entrou uma reserva nova num dos teus clubes.",
            lines,
            "Podes desligar estes emails nas definições do clube.");
    }

    /// <summary>The player's reminder: club, address, pitch, date and time.</summary>
    public static EmailContent Reminder(BookingNotificationDetails booking, string playerName)
    {
        ArgumentNullException.ThrowIfNull(booking);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(booking.TimeZoneId);
        var start = BookingEmailComposer.ClockTime(booking.StartsAt, zone);
        var lines = new List<(string Label, string Value)>
        {
            ("Clube", booking.ClubName),
            ("Morada", $"{booking.ClubAddress}, {booking.ClubLocality}"),
            ("Campo", booking.PitchName),
            ("Data", BookingEmailComposer.LongDate(booking.StartsAt, zone)),
            ("Hora", $"{start} – {BookingEmailComposer.ClockTime(booking.EndsAt, zone)}"),
        };
        return Build(
            $"Lembrete: a tua reserva começa às {start}, {booking.ClubName}",
            $"Olá {playerName},",
            "A tua reserva começa dentro de 3 horas.",
            lines,
            "Se já não puderes ir, avisa o clube.");
    }

    private static EmailContent Build(string subject, string greeting, string intro, List<(string Label, string Value)> lines, string closing)
    {
        var text = new StringBuilder().AppendLine(greeting).AppendLine().AppendLine(intro).AppendLine();
        foreach (var (label, value) in lines)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{label}: {value}");
        }

        text.AppendLine().AppendLine(closing).AppendLine().Append("Reservas de Campos");

        var html = new StringBuilder()
            .Append("<!DOCTYPE html><html lang=\"pt-PT\"><body style=\"font-family:sans-serif;line-height:1.5\">")
            .Append(CultureInfo.InvariantCulture, $"<p>{Html.Encode(greeting)}</p><p>{Html.Encode(intro)}</p><table cellpadding=\"4\">");
        foreach (var (label, value) in lines)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><th align=\"left\">{Html.Encode(label)}</th><td>{Html.Encode(value)}</td></tr>");
        }

        html.Append(CultureInfo.InvariantCulture, $"</table><p>{Html.Encode(closing)}</p><p>Reservas de Campos</p></body></html>");
        return new EmailContent(subject, text.ToString(), html.ToString());
    }
}
