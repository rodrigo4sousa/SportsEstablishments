using System.Globalization;
using System.Text.Json;
using SportsBooking.Domain.Accounts;
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

/// <summary>
/// The US05 emails, in the recipient's language (T09: pt-PT by default, or English): the reset link, and the notice that the password
/// was changed.
/// </summary>
public static class AccountEmailComposer
{
    private static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById(ClubTimeZones.LisbonId);

    /// <summary>The link, how long it works, and what to do when the request was not the reader's.</summary>
    public static EmailContent PasswordReset(string recipientName, string link, TimeSpan validFor, Language language = Language.Portuguese)
    {
        var hours = validFor.TotalHours;
        var count = hours.ToString("0.#", CultureInfo.InvariantCulture);
        var lifetime = language == Language.English
            ? (hours == 1 ? "1 hour" : $"{count} hours")
            : (hours == 1 ? "1 hora" : $"{count} horas");
        var text = new EmailText(language);
        var greeting = text.Greeting(recipientName);
        var intro = text.Pick(
            "Pediste para definir uma nova palavra-passe na Reservas de Campos.",
            "You asked to set a new password on Reservas de Campos.");
        var instructions = text.Pick(
            $"Abre esta ligação para escolheres a nova palavra-passe. Só funciona uma vez e durante {lifetime}:",
            $"Open this link to choose your new password. It works only once and for {lifetime}:");
        var ignore = text.Pick(
            "Se não foste tu que pediste, ignora este email: a tua palavra-passe continua a mesma.",
            "If you did not ask for this, ignore this email: your password stays the same.");
        var linkText = text.Pick("Definir nova palavra-passe", "Set a new password");

        var body = string.Join(
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
            EmailText.SiteName);
        var html =
            text.HtmlStart() +
            $"<p>{Html.Encode(greeting)}</p><p>{Html.Encode(intro)}</p><p>{Html.Encode(instructions)}</p>" +
            $"<p><a href=\"{Html.Encode(link)}\">{Html.Encode(linkText)}</a></p>" +
            $"<p>{Html.Encode(ignore)}</p><p>{EmailText.SiteName}</p></body></html>";
        return new EmailContent(text.Pick("Definir uma nova palavra-passe", "Set a new password"), body, html);
    }

    /// <summary>When it was changed (Lisbon time), and what to do when it was not the reader.</summary>
    public static EmailContent PasswordChanged(string recipientName, DateTime changedAtUtc, Language language = Language.Portuguese)
    {
        var local = ClubTime.ToLocal(changedAtUtc, Lisbon);
        var clock = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        var date = BookingEmailComposer.LongDate(changedAtUtc, Lisbon, language);
        var text = new EmailText(language);
        var greeting = text.Greeting(recipientName);
        var body = text.Pick(
            $"A palavra-passe da tua conta na Reservas de Campos foi alterada em {date}, às {clock} (hora de Lisboa). As sessões abertas foram terminadas.",
            $"The password of your Reservas de Campos account was changed on {date}, at {clock} (Lisbon time). Every open session was signed out.");
        var warning = text.Pick(
            "Se não foste tu, pede já uma nova palavra-passe em «Esqueci-me da palavra-passe» e avisa-nos.",
            "If this was not you, ask for a new password now with “Forgot your password?” and let us know.");

        var plain = string.Join("\n", greeting, string.Empty, body, string.Empty, warning, string.Empty, EmailText.SiteName);
        var html =
            text.HtmlStart() +
            $"<p>{Html.Encode(greeting)}</p><p>{Html.Encode(body)}</p><p>{Html.Encode(warning)}</p><p>{EmailText.SiteName}</p></body></html>";
        return new EmailContent(text.Pick("A tua palavra-passe foi alterada", "Your password was changed"), plain, html);
    }
}

/// <summary>
/// T09: the words shared by every email, and the choice between the two languages. Each email keeps both of its texts side by side, so a
/// sentence and its translation are reviewed together. The site's name is a name and is not translated.
/// </summary>
internal readonly struct EmailText(Language language)
{
    public const string SiteName = "Reservas de Campos";

    public Language Language { get; } = language;

    public string Pick(string portuguese, string english) => Language == Language.English ? english : portuguese;

    public string Greeting(string name) => Pick($"Olá {name},", $"Hello {name},");

    /// <summary>The opening of the HTML body, with the language of the text.</summary>
    public string HtmlStart() =>
        $"<!DOCTYPE html><html lang=\"{Pick(PreferredLanguage.PortugueseCode, PreferredLanguage.EnglishCode)}\"><body style=\"font-family:sans-serif;line-height:1.5\">";
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
