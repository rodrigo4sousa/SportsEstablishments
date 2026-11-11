using FluentValidation;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Application.Accounts;

/// <summary>T09: the language of the signed-in account's emails, <c>pt-PT</c> or <c>en</c>.</summary>
public sealed record ChangePreferredLanguageRequest(string PreferredLanguage)
{
    /// <summary>A missing value counts as empty; surrounding spaces are removed.</summary>
    public ChangePreferredLanguageRequest Normalized() => new(PreferredLanguage?.Trim() ?? string.Empty);
}

/// <summary>Messages are pt-PT, as every API message; the site shows its own text for the field (T09).</summary>
public static class PreferredLanguageMessages
{
    public const string NotSupported = "Escolhe uma língua disponível: português (pt-PT) ou inglês (en).";
}

public sealed class ChangePreferredLanguageRequestValidator : AbstractValidator<ChangePreferredLanguageRequest>
{
    public ChangePreferredLanguageRequestValidator()
    {
        RuleFor(request => request.PreferredLanguage)
            .Must(PreferredLanguage.IsSupported)
            .WithMessage(PreferredLanguageMessages.NotSupported);
    }
}

/// <summary>T09: changes the language of the signed-in account's emails. Any profile; the site's own language is a cookie and is not touched.</summary>
public sealed class AccountPreferencesService(
    ICurrentUser currentUser,
    IValidator<ChangePreferredLanguageRequest> validator,
    IAccountStore accounts)
{
    public async Task<Result<bool>> ChangePreferredLanguageAsync(ChangePreferredLanguageRequest request, CancellationToken cancellationToken)
    {
        var accountId = currentUser.UserId ?? throw new InvalidOperationException("Changing a preference requires a session.");
        var normalized = request.Normalized();
        var validation = await validator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        return await accounts.ChangePreferredLanguageAsync(accountId, normalized.PreferredLanguage, cancellationToken)
            ? true
            : AccountPrivacyErrors.AccountNotFound;
    }
}
