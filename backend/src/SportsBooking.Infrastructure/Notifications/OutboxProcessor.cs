using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SportsBooking.Application.Notifications;
using SportsBooking.Infrastructure.Auth;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.Infrastructure.Notifications;

/// <summary>
/// One pass of the email worker (US22): claims up to a batch of due messages and sends them. The claim is
/// <c>SELECT … FOR UPDATE SKIP LOCKED</c> inside a transaction that lasts until the batch is recorded, so two API
/// instances (or two passes) never take the same message: the second one skips the rows the first one holds. A send
/// that fails counts an attempt and waits for the back-off; after the last allowed attempt the message is given up and a
/// Warning is logged with its id and type only. The recipient is the player's current address, read now.
/// Delivery is at least once: if the process stops after a send and before the commit, that message is sent again.
/// </summary>
public sealed partial class OutboxProcessor(
    AppDbContext dbContext,
    IEmailSender sender,
    AccountEmailBuilder accountEmails,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor> logger)
{
    /// <summary>Used when the booking's player account is gone (or has no address): there is nobody to send it to.</summary>
    public const string RecipientNotFound = "recipient_not_found";

    /// <summary>Sends what is due now; returns how many messages were claimed (sent or not).</summary>
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var retry = settings.Retry;
        var now = Now();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var due = await dbContext.OutboxMessages
            .FromSql($"""
                      SELECT * FROM outbox_messages
                      WHERE sent_at IS NULL AND failed_at IS NULL AND next_attempt_at <= {now}
                      ORDER BY next_attempt_at, id
                      LIMIT {settings.BatchSize}
                      FOR UPDATE SKIP LOCKED
                      """)
            .ToListAsync(cancellationToken);

        foreach (var message in due)
        {
            await ProcessAsync(message, retry, cancellationToken);
            // Record each result as it happens, so a later failure in the batch cannot undo it in memory.
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return due.Count;
    }

    private async Task ProcessAsync(OutboxMessage message, OutboxRetry retry, CancellationToken cancellationToken)
    {
        EmailMessage email;
        try
        {
            if (await BuildAsync(message, cancellationToken) is not { } built)
            {
                message.GiveUp(RecipientNotFound, Now());
                LogGivenUp(logger, message.Id, message.Type, message.Attempts, RecipientNotFound);
                return;
            }

            email = built;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A message that cannot be read or written out will never work: give it up rather than retry it.
            message.GiveUp(exception.GetType().Name, Now());
            LogGivenUp(logger, message.Id, message.Type, message.Attempts, exception.GetType().Name);
            return;
        }

        try
        {
            await sender.SendAsync(email, cancellationToken);
            message.MarkSent(Now());
            LogSent(logger, message.Id, message.Type);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Only the exception's type is kept and logged: its message may hold the address.
            var error = exception.GetType().Name;
            if (message.RecordFailure(error, Now(), retry))
            {
                LogGivenUp(logger, message.Id, message.Type, message.Attempts, error);
            }
            else
            {
                LogRetry(logger, message.Id, message.Type, message.Attempts, error, message.NextAttemptAt);
            }
        }
    }

    /// <summary>The email of a message, or null when there is nobody to send it to (the account is gone, or may no longer get it).</summary>
    private async Task<EmailMessage?> BuildAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (message.Type is OutboxMessageTypes.PasswordReset or OutboxMessageTypes.PasswordChanged)
        {
            // US05: the reset token is made now, by Identity, and goes only into the email: never into a log or the outbox.
            return await accountEmails.BuildAsync(message.Type, AccountEmailPayload.Deserialize(message.Payload), message.CreatedAt);
        }

        var payload = BookingEmailPayload.Deserialize(message.Payload);
        var recipient = await dbContext.Users.AsNoTracking()
            .Where(user => user.Id == payload.PlayerId)
            .Select(user => new { user.Email, user.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (recipient?.Email is not { Length: > 0 } address)
        {
            return null;
        }

        var content = BookingEmailComposer.Compose(message.Type, payload, recipient.Name);
        return new EmailMessage(address, recipient.Name, content.Subject, content.TextBody, content.HtmlBody);
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox message {MessageId} ({Type}) sent.")]
    private static partial void LogSent(ILogger logger, Guid messageId, string type);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox message {MessageId} ({Type}) failed on attempt {Attempts} ({Error}); next attempt at {NextAttemptAt:O}.")]
    private static partial void LogRetry(ILogger logger, Guid messageId, string type, int attempts, string error, DateTime nextAttemptAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({Type}) given up after {Attempts} failed attempts ({Error}); it will not be sent.")]
    private static partial void LogGivenUp(ILogger logger, Guid messageId, string type, int attempts, string error);
}
