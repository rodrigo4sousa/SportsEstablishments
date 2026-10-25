using System.Globalization;
using System.Text.Json;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Notifications;

/// <summary>
/// What an account email keeps in the outbox (US05): the account id and nothing else. The address and the name are read
/// when sending, and the reset token is made then too, so no secret is ever stored in the outbox.
/// </summary>
public sealed record AccountEmailPayload(Guid AccountId)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static AccountEmailPayload Deserialize(string payload) =>
        JsonSerializer.Deserialize<AccountEmailPayload>(payload, Json) ?? throw new InvalidOperationException("An outbox payload is empty.");
}

/// <summary>The US05 emails in Portuguese (pt-PT): the reset link, and the notice that the password was changed.</summary>
public static class AccountEmailComposer
{
    private static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById(ClubTimeZones.LisbonId);

    /// <summary>The link, how long it works, and what to do when the request was not the reader's.</summary>
    public static EmailContent PasswordReset(string recipientName, string link, TimeSpan validFor)
    {
        var hours = validFor.TotalHours;
        var lifetime = hours == 1 ? "1 hora" : $"{hours.ToString("0.#", CultureInfo.InvariantCulture)} horas";
        var greeting = $"Olá {recipientName},";
        const string intro = "Pediste para definir uma nova palavra-passe na Reservas de Campos.";
        var instructions = $"Abre esta ligação para escolheres a nova palavra-passe. Só funciona uma vez e durante {lifetime}:";
        const string ignore = "Se não foste tu que pediste, ignora este email: a tua palavra-passe continua a mesma.";

        var text = string.Join(
            "\n",
            greeting,
            string.Empty,
            intro,
            instructions,
            string.Empty,
            link,
            string.Empty,
            ignore,
            string.Empty,
            "Reservas de Campos");
        var html =
            "<!DOCTYPE html><html lang=\"pt-PT\"><body style=\"font-family:sans-serif;line-height:1.5\">" +
            $"<p>{Html.Encode(greeting)}</p><p>{Html.Encode(intro)}</p><p>{Html.Encode(instructions)}</p>" +
            $"<p><a href=\"{Html.Encode(link)}\">Definir nova palavra-passe</a></p>" +
            $"<p>{Html.Encode(ignore)}</p><p>Reservas de Campos</p></body></html>";
        return new EmailContent("Definir uma nova palavra-passe", text, html);
    }

    /// <summary>When it was changed (Lisbon time), and what to do when it was not the reader.</summary>
    public static EmailContent PasswordChanged(string recipientName, DateTime changedAtUtc)
    {
        var local = ClubTime.ToLocal(changedAtUtc, Lisbon);
        var when = $"{BookingEmailComposer.LongDate(changedAtUtc, Lisbon)}, às {local.ToString("HH:mm", CultureInfo.InvariantCulture)}";
        var greeting = $"Olá {recipientName},";
        var body = $"A palavra-passe da tua conta na Reservas de Campos foi alterada em {when} (hora de Lisboa). As sessões abertas foram terminadas.";
        const string warning = "Se não foste tu, pede já uma nova palavra-passe em «Esqueci-me da palavra-passe» e avisa-nos.";

        var text = string.Join("\n", greeting, string.Empty, body, string.Empty, warning, string.Empty, "Reservas de Campos");
        var html =
            "<!DOCTYPE html><html lang=\"pt-PT\"><body style=\"font-family:sans-serif;line-height:1.5\">" +
            $"<p>{Html.Encode(greeting)}</p><p>{Html.Encode(body)}</p><p>{Html.Encode(warning)}</p><p>Reservas de Campos</p></body></html>";
        return new EmailContent("A tua palavra-passe foi alterada", text, html);
    }
}

/// <summary>Only the characters that mean something in HTML: the email is UTF-8, so accented letters stay as they are.</summary>
internal static class Html
{
    public static string Encode(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&#39;", StringComparison.Ordinal);
}
