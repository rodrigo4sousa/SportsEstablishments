using SportsBooking.Application.Auth;
using SportsBooking.Application.Notifications;

namespace SportsBooking.UnitTests.Notifications;

public sealed class AccountEmailComposerTests
{
    private const string Link = "https://reservas.example/reset-password?email=rita%40exemplo.pt&token=abc%2B%2F%3D";

    [Fact]
    public void The_reset_email_greets_by_name_holds_the_link_and_says_how_long_it_works()
    {
        var email = AccountEmailComposer.PasswordReset("Rita Silva", Link, TimeSpan.FromHours(1));

        email.Subject.ShouldBe("Definir uma nova palavra-passe");
        email.TextBody.ShouldStartWith("Olá Rita Silva,");
        email.TextBody.ShouldContain($"\n{Link}\n");
        email.TextBody.ShouldContain("Só funciona uma vez e durante 1 hora");
        email.TextBody.ShouldContain("Se não foste tu que pediste, ignora este email");
        email.HtmlBody.ShouldContain("lang=\"pt-PT\"");
        // The '&' between the parameters is escaped in the attribute; the browser reads it back as '&'.
        email.HtmlBody.ShouldContain($"<a href=\"{Link.Replace("&", "&amp;", StringComparison.Ordinal)}\">Definir nova palavra-passe</a>");
    }

    [Fact]
    public void A_name_with_html_is_escaped_in_the_html_and_kept_in_the_text()
    {
        var email = AccountEmailComposer.PasswordReset("Ana <b>&</b>", Link, TimeSpan.FromHours(1));

        email.HtmlBody.ShouldContain("Olá Ana &lt;b&gt;&amp;&lt;/b&gt;,");
        email.HtmlBody.ShouldNotContain("<b>");
        email.TextBody.ShouldContain("Olá Ana <b>&</b>,");
    }

    [Fact]
    public void A_lifetime_other_than_one_hour_is_said_in_hours()
    {
        AccountEmailComposer.PasswordReset("Rita", Link, TimeSpan.FromHours(2)).TextBody.ShouldContain("durante 2 horas");
    }

    [Theory]
    [InlineData("2026-07-14T09:30:00Z", "terça-feira, 14 de julho de 2026, às 10:30")] // summer: UTC+1
    [InlineData("2026-12-01T09:30:00Z", "terça-feira, 1 de dezembro de 2026, às 09:30")] // winter: UTC+0
    public void The_changed_email_says_when_in_lisbon_time_and_has_no_link(string changedAt, string expected)
    {
        var email = AccountEmailComposer.PasswordChanged("Rui Costa", DateTime.Parse(changedAt, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime());

        email.Subject.ShouldBe("A tua palavra-passe foi alterada");
        email.TextBody.ShouldStartWith("Olá Rui Costa,");
        email.TextBody.ShouldContain($"foi alterada em {expected} (hora de Lisboa)");
        email.TextBody.ShouldContain("Se não foste tu");
        email.HtmlBody.ShouldNotContain("href");
    }
}

public sealed class PasswordResetRulesTests
{
    [Theory]
    [InlineData("https://reservas.example")]
    [InlineData("https://reservas.example/")]
    public void The_link_is_the_reset_page_on_the_public_address(string baseUrl)
    {
        PasswordResetRules.Link(baseUrl, "rita@exemplo.pt", "abc")
            .ShouldBe("https://reservas.example/reset-password?email=rita%40exemplo.pt&token=abc");
    }

    [Fact]
    public void The_token_and_the_email_are_escaped_so_they_come_back_unchanged()
    {
        const string token = "CfDJ8+a/b==";
        const string email = "o'neill+reservas@exemplo.pt";

        var link = new Uri(PasswordResetRules.Link("http://localhost:3000", email, token));

        link.AbsolutePath.ShouldBe("/reset-password");
        var query = link.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));
        query["token"].ShouldBe(token);
        query["email"].ShouldBe(email);
        link.Query.ShouldNotContain("+");
    }

    [Fact]
    public void The_lifetime_is_one_hour_and_the_throttle_five_minutes()
    {
        PasswordResetRules.LinkLifetime.ShouldBe(TimeSpan.FromHours(1));
        PasswordResetRules.ThrottleInterval.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Another_email_is_allowed_when_there_was_none_or_once_the_interval_has_passed()
    {
        var now = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

        PasswordResetRules.AllowsAnotherEmail(null, now).ShouldBeTrue();
        PasswordResetRules.AllowsAnotherEmail(now, now).ShouldBeFalse();
        PasswordResetRules.AllowsAnotherEmail(now - PasswordResetRules.ThrottleInterval + TimeSpan.FromSeconds(1), now).ShouldBeFalse();
        PasswordResetRules.AllowsAnotherEmail(now - PasswordResetRules.ThrottleInterval, now).ShouldBeTrue();
    }
}
