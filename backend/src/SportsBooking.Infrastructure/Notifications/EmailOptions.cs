using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Infrastructure.Notifications;

/// <summary>How email leaves the API: an SMTP server (Mailpit in development, Brevo in production).</summary>
public enum SmtpSecurity
{
    /// <summary>Plain SMTP, as Mailpit speaks it. Never in production.</summary>
    None,

    /// <summary>Connect in plain text and upgrade with STARTTLS (port 587); refused if the server cannot.</summary>
    StartTls,

    /// <summary>TLS from the first byte (port 465).</summary>
    SslOnConnect,
}

/// <summary>
/// The <c>Email</c> settings (US22). In development they point at Mailpit (appsettings.Development.json, and
/// <c>Email__Host=mailpit</c> in docker compose); in production they come from environment variables (T06:
/// <c>Email__Host</c>, <c>Email__Port</c>, <c>Email__Security</c>, <c>Email__Username</c>, <c>Email__Password</c>,
/// <c>Email__FromAddress</c>). Never logged.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    /// <summary>Both or neither with <see cref="Password"/>; Mailpit needs none.</summary>
    public string? Username { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "Reservas de Campos";

    /// <summary>How long one SMTP exchange may take before it counts as failed.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>False when both the host and the sender are empty. Only Production may start like that (T06, provisional).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) || !string.IsNullOrWhiteSpace(FromAddress);
}

/// <summary>
/// Checked at start-up (<c>ValidateOnStart</c>); the messages name the settings, never their values. In Production SMTP may be left out
/// altogether: the API starts, <see cref="OutboxDispatcher"/> warns and sends nothing. Settings that are present are always checked.
/// </summary>
public sealed class EmailOptionsValidator(IHostEnvironment environment) : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        if (!options.IsConfigured && environment.IsProduction())
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            failures.Add("Email:Host is required.");
        }

        if (options.Port is < 1 or > 65535)
        {
            failures.Add("Email:Port must be between 1 and 65535.");
        }

        if (!Enum.IsDefined(options.Security))
        {
            failures.Add("Email:Security must be None, StartTls or SslOnConnect.");
        }

        if (string.IsNullOrWhiteSpace(options.FromAddress) || !EmailAddressPolicy.IsValid(options.FromAddress.Trim()))
        {
            failures.Add("Email:FromAddress must be a valid email address.");
        }

        if (string.IsNullOrWhiteSpace(options.FromName))
        {
            failures.Add("Email:FromName is required.");
        }

        if (string.IsNullOrEmpty(options.Username) != string.IsNullOrEmpty(options.Password))
        {
            failures.Add("Email:Username and Email:Password must be set together, or not at all.");
        }

        if (options.TimeoutSeconds is < 1 or > 600)
        {
            failures.Add("Email:TimeoutSeconds must be between 1 and 600.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>
/// The <c>Outbox</c> settings of the email worker (US22). All the defaults are provisional: a batch of 20, a poll every
/// 10 seconds, the first retry after 1 minute, doubling up to 1 hour, and giving up after 6 failed attempts.
/// <see cref="Enabled"/> turns the worker off (the integration tests drive it themselves).
/// </summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public bool Enabled { get; set; } = true;

    public int BatchSize { get; set; } = 20;

    public int PollIntervalSeconds { get; set; } = 10;

    public int FirstRetryDelaySeconds { get; set; } = 60;

    public int MaximumRetryDelaySeconds { get; set; } = 3600;

    public int MaxAttempts { get; set; } = 6;

    public Application.Notifications.OutboxRetry Retry =>
        new(TimeSpan.FromSeconds(FirstRetryDelaySeconds), TimeSpan.FromSeconds(MaximumRetryDelaySeconds), MaxAttempts);
}

public sealed class OutboxOptionsValidator : IValidateOptions<OutboxOptions>
{
    public ValidateOptionsResult Validate(string? name, OutboxOptions options)
    {
        var failures = new List<string>();
        if (options.BatchSize is < 1 or > 1000)
        {
            failures.Add("Outbox:BatchSize must be between 1 and 1000.");
        }

        if (options.PollIntervalSeconds < 1)
        {
            failures.Add("Outbox:PollIntervalSeconds must be at least 1.");
        }

        if (options.FirstRetryDelaySeconds < 1 || options.MaximumRetryDelaySeconds < options.FirstRetryDelaySeconds)
        {
            failures.Add("Outbox:FirstRetryDelaySeconds must be at least 1 and not above Outbox:MaximumRetryDelaySeconds.");
        }

        if (options.MaxAttempts is < 1 or > 100)
        {
            failures.Add("Outbox:MaxAttempts must be between 1 and 100.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
