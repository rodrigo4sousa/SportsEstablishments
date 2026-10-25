using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsBooking.Application.Auth;
using SportsBooking.Application.Notifications;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.Infrastructure.Auth;

/// <summary>
/// The <c>PublicSite</c> settings (US05): the address people open the site at, for the links in emails. Development has
/// <c>http://localhost:3000</c>; production sets <c>PublicSite__BaseUrl</c> to its HTTPS address (T06).
/// </summary>
public sealed class PublicSiteOptions
{
    public const string SectionName = "PublicSite";

    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>Checked at start-up (<c>ValidateOnStart</c>); the message names the setting, never its value.</summary>
public sealed class PublicSiteOptionsValidator : IValidateOptions<PublicSiteOptions>
{
    public ValidateOptionsResult Validate(string? name, PublicSiteOptions options) =>
        Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var url)
        && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)
        && string.IsNullOrEmpty(url.Query) && string.IsNullOrEmpty(url.Fragment)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("PublicSite:BaseUrl must be an absolute http or https address without a query, e.g. https://reservas.example.");
}

/// <summary>Who may reset a password by email (US05): active players and owners. The admin uses <c>set-admin-password</c> (provisional).</summary>
internal static class PasswordResetEligibility
{
    public static async Task<bool> IsEligibleAsync(UserManager<AppUser> userManager, AppUser user)
    {
        if (!user.IsActive)
        {
            return false;
        }

        var profiles = await userManager.GetRolesAsync(user);
        return profiles.Count == 1 && profiles[0] is Profiles.Player or Profiles.Owner;
    }
}

internal sealed class PasswordResetStore(UserManager<AppUser> userManager, AppDbContext dbContext, TimeProvider timeProvider) : IPasswordResetStore
{
    public async Task QueueResetEmailAsync(string email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || !await PasswordResetEligibility.IsEligibleAsync(userManager, user))
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // One request at a time per account, so two at the same moment cannot both pass the throttle.
        await dbContext.Database.ExecuteSqlAsync($"SELECT 1 FROM users WHERE id = {user.Id} FOR UPDATE", cancellationToken);
        var lastQueuedAt = await dbContext.Database
            .SqlQuery<DateTime?>($"""
                                  SELECT max(created_at) AS "Value" FROM outbox_messages
                                  WHERE type = {OutboxMessageTypes.PasswordReset} AND (payload ->> 'accountId')::uuid = {user.Id}
                                  """)
            .SingleAsync(cancellationToken);
        if (!PasswordResetRules.AllowsAnotherEmail(lastQueuedAt, now))
        {
            // Silently: the answer is the same either way.
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        dbContext.OutboxMessages.Add(OutboxMessage.Create(OutboxMessageTypes.PasswordReset, new AccountEmailPayload(user.Id).Serialize(), now));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> ResetAsync(string email, string token, string newPassword, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || !await PasswordResetEligibility.IsEligibleAsync(userManager, user))
        {
            return false;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Checks the token (purpose, lifetime, security stamp), sets the hash and gives the account a new security stamp,
        // so this token, and any other reset token issued before, never works again.
        var reset = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (!reset.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        // Whoever knew the old password is signed out everywhere, and the owner of the account can sign in at once.
        await dbContext.RefreshTokens
            .Where(refresh => refresh.UserId == user.Id && refresh.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(refresh => refresh.RevokedAt, (DateTime?)now), cancellationToken);
        await userManager.ResetAccessFailedCountAsync(user);
        await userManager.SetLockoutEndDateAsync(user, null);

        dbContext.OutboxMessages.Add(OutboxMessage.Create(OutboxMessageTypes.PasswordChanged, new AccountEmailPayload(user.Id).Serialize(), now));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}

/// <summary>
/// Writes out the US05 emails for the outbox worker. The reset token is made here, when the email is sent: it never sits in
/// the outbox. Returns null when there is nobody (eligible) to send to; the worker then gives the message up.
/// </summary>
public sealed class AccountEmailBuilder(UserManager<AppUser> userManager, IOptions<PublicSiteOptions> site)
{
    public const string RecipientNotEligible = "recipient_not_eligible";

    public async Task<EmailMessage?> BuildAsync(string type, AccountEmailPayload payload, DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var user = await userManager.FindByIdAsync(payload.AccountId.ToString());
        if (user?.Email is not { Length: > 0 } address)
        {
            return null;
        }

        EmailContent content;
        if (type == OutboxMessageTypes.PasswordReset)
        {
            if (!await PasswordResetEligibility.IsEligibleAsync(userManager, user))
            {
                return null;
            }

            if (user.SecurityStamp is null)
            {
                // An account made without one (the demo seed): Identity's tokens need it.
                await userManager.UpdateSecurityStampAsync(user);
            }

            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            content = AccountEmailComposer.PasswordReset(user.Name, PasswordResetRules.Link(site.Value.BaseUrl, address, token), PasswordResetRules.LinkLifetime);
        }
        else
        {
            content = AccountEmailComposer.PasswordChanged(user.Name, createdAt);
        }

        return new EmailMessage(address, user.Name, content.Subject, content.TextBody, content.HtmlBody);
    }
}
