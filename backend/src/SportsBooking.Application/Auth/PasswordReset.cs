using FluentValidation;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Application.Auth;

/// <summary>US05 rules that do not depend on Identity or the database (provisional values).</summary>
public static class PasswordResetRules
{
    /// <summary>How long a reset link works: Identity's token lifespan (<c>DataProtectionTokenProviderOptions</c>).</summary>
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromHours(1);

    /// <summary>At most one reset email is queued per account in this time; further requests get the same answer and queue nothing.</summary>
    public static readonly TimeSpan ThrottleInterval = TimeSpan.FromMinutes(5);

    /// <summary>The path of the front end's page that takes the link (<c>?email=&amp;token=</c>).</summary>
    public const string ResetPagePath = "/reset-password";

    /// <summary>True when a new reset email may be queued, given when the last one was (null: never).</summary>
    public static bool AllowsAnotherEmail(DateTime? lastQueuedAt, DateTime now) =>
        lastQueuedAt is not { } last || now - last >= ThrottleInterval;

    /// <summary>
    /// The link of the email: the site's public address, the reset page, and the email and token as query parameters
    /// (escaped: an Identity token holds '+', '/' and '='). The link is a secret for an hour: never log it.
    /// </summary>
    public static string Link(string publicBaseUrl, string email, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicBaseUrl);
        return $"{publicBaseUrl.TrimEnd('/')}{ResetPagePath}?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
    }
}

/// <summary>The body of <c>POST /api/auth/password-reset/request</c>.</summary>
public sealed record PasswordResetRequest(string Email);

/// <summary>The body of <c>POST /api/auth/password-reset/confirm</c>: the email and token from the link, and the new password.</summary>
public sealed record PasswordResetConfirmRequest(string Email, string Token, string NewPassword);

/// <summary>The one answer to a reset request, whatever the email (US05).</summary>
public sealed record PasswordResetRequested(string Message)
{
    public static PasswordResetRequested Always { get; } =
        new("Se existir uma conta com este email, vais receber uma ligação para definir uma nova palavra-passe. A ligação é válida durante 1 hora.");
}

public sealed class PasswordResetRequestValidator : AbstractValidator<PasswordResetRequest>
{
    public PasswordResetRequestValidator()
    {
        RuleFor(request => request.Email)
            .Must(email => !string.IsNullOrWhiteSpace(email)).WithMessage("Indica o email.")
            .Must(email => string.IsNullOrWhiteSpace(email) || EmailAddressPolicy.IsValid(email.Trim())).WithMessage("Indica um email válido.");
    }
}

/// <summary>Only the new password is judged field by field; a bad email or token is the one generic error, never a field message.</summary>
public sealed class PasswordResetConfirmRequestValidator : AbstractValidator<PasswordResetConfirmRequest>
{
    public PasswordResetConfirmRequestValidator()
    {
        RuleFor(request => request.NewPassword)
            .Must(PasswordPolicy.IsSatisfiedBy)
            .WithMessage($"A palavra-passe tem de ter pelo menos {PasswordPolicy.MinimumLength} caracteres.");
    }
}

/// <summary>
/// The Identity side of US05, in Infrastructure. Neither method tells the caller whether the email has an account: the
/// request returns nothing at all, and the reset only says whether it worked.
/// </summary>
public interface IPasswordResetStore
{
    /// <summary>
    /// Queues the reset email (the account id only, never a token) when the email is an active player's or owner's and none
    /// was queued for it in <see cref="PasswordResetRules.ThrottleInterval"/>; otherwise does nothing.
    /// </summary>
    Task QueueResetEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the new password if the token is valid for this email's account (an active player or owner). On success, in one
    /// transaction: the password (a new security stamp, so no reset token works again), every refresh token revoked, the lockout
    /// cleared, and the "your password was changed" email queued. False for an unknown email or a wrong, expired or used token.
    /// </summary>
    Task<bool> ResetAsync(string email, string token, string newPassword, CancellationToken cancellationToken);
}

public static class PasswordResetErrors
{
    /// <summary>Every bad link gives exactly this: unknown email, wrong token, expired, already used.</summary>
    public static ServiceError InvalidOrExpiredLink { get; } = new(
        ErrorKind.Validation,
        ErrorCodes.InvalidOrExpiredResetLink,
        "Ligação inválida ou expirada",
        "Esta ligação já não é válida: expirou ou já foi usada. Pede uma nova.");
}

/// <summary>
/// US05: forgotten passwords. The request always answers the same, after only queuing work; the token is made by the email
/// worker when it sends (Identity's reset token, 1 hour). The confirmation checks the new password (US01) and then the
/// link; on success the user signs in again with the new password (no automatic login, provisional).
/// </summary>
public sealed class PasswordResetService(
    IPasswordResetStore store,
    PasswordResetRequestValidator requestValidator,
    PasswordResetConfirmRequestValidator confirmValidator)
{
    public async Task<Result<PasswordResetRequested>> RequestAsync(PasswordResetRequest request, CancellationToken cancellationToken)
    {
        var validation = await requestValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        await store.QueueResetEmailAsync(request.Email.Trim(), cancellationToken);
        return PasswordResetRequested.Always;
    }

    public async Task<ServiceError?> ConfirmAsync(PasswordResetConfirmRequest request, CancellationToken cancellationToken)
    {
        var validation = await confirmValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Token))
        {
            return PasswordResetErrors.InvalidOrExpiredLink;
        }

        return await store.ResetAsync(request.Email.Trim(), request.Token, request.NewPassword, cancellationToken)
            ? null
            : PasswordResetErrors.InvalidOrExpiredLink;
    }
}
