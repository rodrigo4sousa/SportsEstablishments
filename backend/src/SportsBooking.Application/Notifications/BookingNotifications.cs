using System.Globalization;
using System.Text.Json;
using SportsBooking.Domain.Accounts;
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

/// <summary>The emails of US23 (to the owner, with the player's name) and US21 (to the player), in the recipient's language (T09).</summary>
public static class BookingNotificationComposer
{
    /// <summary>The owner's email: club, pitch, date, time (Lisbon) and the player's name (and nothing else about the player).</summary>
    public static EmailContent OwnerNewBooking(BookingNotificationDetails booking, string ownerName, Language language = Language.Portuguese)
    {
        ArgumentNullException.ThrowIfNull(booking);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(booking.TimeZoneId);
        var text = new EmailText(language);
        var shortDate = ClubTime.ToLocal(booking.StartsAt, zone).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var start = BookingEmailComposer.ClockTime(booking.StartsAt, zone, language);
        var lines = new List<(string Label, string Value)>
        {
            (text.Pick("Clube", "Club"), booking.ClubName),
            (text.Pick("Campo", "Pitch"), booking.PitchName),
            (text.Pick("Data", "Date"), BookingEmailComposer.LongDate(booking.StartsAt, zone, language)),
            (text.Pick("Hora", "Time"), $"{start} – {BookingEmailComposer.ClockTime(booking.EndsAt, zone, language)}"),
            (text.Pick("Jogador", "Player"), booking.PlayerName),
        };
        return EmailLayout.Table(
            text,
            text.Pick($"Nova reserva: {booking.ClubName}, {shortDate} às {start}", $"New booking: {booking.ClubName}, {shortDate} at {start}"),
            text.Greeting(ownerName),
            text.Pick("Entrou uma reserva nova num dos teus clubes.", "A new booking came in at one of your clubs."),
            lines,
            text.Pick("Podes desligar estes emails nas definições do clube.", "You can turn these emails off in the club's settings."));
    }

    /// <summary>The player's reminder: club, address, pitch, date and time.</summary>
    public static EmailContent Reminder(BookingNotificationDetails booking, string playerName, Language language = Language.Portuguese)
    {
        ArgumentNullException.ThrowIfNull(booking);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(booking.TimeZoneId);
        var text = new EmailText(language);
        var start = BookingEmailComposer.ClockTime(booking.StartsAt, zone, language);
        var lines = new List<(string Label, string Value)>
        {
            (text.Pick("Clube", "Club"), booking.ClubName),
            (text.Pick("Morada", "Address"), $"{booking.ClubAddress}, {booking.ClubLocality}"),
            (text.Pick("Campo", "Pitch"), booking.PitchName),
            (text.Pick("Data", "Date"), BookingEmailComposer.LongDate(booking.StartsAt, zone, language)),
            (text.Pick("Hora", "Time"), $"{start} – {BookingEmailComposer.ClockTime(booking.EndsAt, zone, language)}"),
        };
        return EmailLayout.Table(
            text,
            text.Pick($"Lembrete: a tua reserva começa às {start}, {booking.ClubName}", $"Reminder: your booking starts at {start}, {booking.ClubName}"),
            text.Greeting(playerName),
            text.Pick("A tua reserva começa dentro de 3 horas.", "Your booking starts in 3 hours."),
            lines,
            text.Pick("Se já não puderes ir, avisa o clube.", "If you can no longer go, let the club know."));
    }
}
