using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SportsBooking.Application.Notifications;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Bookings;
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
                      WHERE sent_at IS NULL AND failed_at IS NULL AND skipped_at IS NULL AND next_attempt_at <= {now}
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
            var built = await BuildAsync(message, cancellationToken);
            if (built.SkipReason is { } skipReason)
            {
                // The message no longer applies (a reminder for a booking that was cancelled): closed, not sent, not a failure.
                message.Skip(skipReason, Now());
                LogSkipped(logger, message.Id, message.Type, skipReason);
                return;
            }

            if (built.Email is not { } ready)
            {
                message.GiveUp(RecipientNotFound, Now());
                LogGivenUp(logger, message.Id, message.Type, message.Attempts, RecipientNotFound);
                return;
            }

            if (DeletedAccount.IsTombstone(ready.ToAddress))
            {
                // US30: the deletion removes what is pending; this is a message written by a request that was already running.
                message.Skip(SkippedAccountDeleted, Now());
                LogSkipped(logger, message.Id, message.Type, SkippedAccountDeleted);
                return;
            }

            email = ready;
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

    /// <summary>What a message turned into: an email, nobody to send it to (both null), or a reason not to send it at all.</summary>
    private sealed record Built(EmailMessage? Email, string? SkipReason = null);

    /// <summary>US21: the codes a reminder is skipped with.</summary>
    public const string SkippedNotConfirmed = "booking_not_confirmed";

    public const string SkippedStarted = "booking_started";

    /// <summary>US23: the owner's account was deactivated before the email was sent.</summary>
    public const string SkippedOwnerDeactivated = "owner_deactivated";

    /// <summary>US30: the recipient deleted the account before the email was sent.</summary>
    public const string SkippedAccountDeleted = "account_deleted";

    private async Task<Built> BuildAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (message.Type is OutboxMessageTypes.PasswordReset or OutboxMessageTypes.PasswordChanged)
        {
            // US05: the reset token is made now, by Identity, and goes only into the email: never into a log or the outbox.
            return new Built(await accountEmails.BuildAsync(message.Type, AccountEmailPayload.Deserialize(message.Payload), message.CreatedAt));
        }

        if (message.Type is OutboxMessageTypes.OwnerNewBooking or OutboxMessageTypes.BookingReminder)
        {
            return await BuildBookingNotificationAsync(message, cancellationToken);
        }

        if (message.Type == OutboxMessageTypes.BookingSeriesConfirmed)
        {
            // US20: the summary of a weekly series, to the player's current address.
            var series = BookingSeriesEmailPayload.Deserialize(message.Payload);
            var player = await dbContext.Users.AsNoTracking()
                .Where(user => user.Id == series.PlayerId)
                .Select(user => new { user.Email, user.Name })
                .SingleOrDefaultAsync(cancellationToken);
            if (player?.Email is not { Length: > 0 } playerAddress)
            {
                return new Built(null);
            }

            var summary = BookingSeriesEmailComposer.Compose(series, player.Name);
            return new Built(new EmailMessage(playerAddress, player.Name, summary.Subject, summary.TextBody, summary.HtmlBody));
        }

        var payload = BookingEmailPayload.Deserialize(message.Payload);
        var recipient = await dbContext.Users.AsNoTracking()
            .Where(user => user.Id == payload.PlayerId)
            .Select(user => new { user.Email, user.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (recipient?.Email is not { Length: > 0 } address)
        {
            return new Built(null);
        }

        var content = BookingEmailComposer.Compose(message.Type, payload, recipient.Name);
        return new Built(new EmailMessage(address, recipient.Name, content.Subject, content.TextBody, content.HtmlBody));
    }

    /// <summary>
    /// US23 and US21: the booking is read now, so the email says what is true at the moment it is sent. The owner's email goes to the
    /// owner's current address and names the player; the reminder goes to the player, and only if the booking is still confirmed and
    /// has not started (a reminder that is due after a cancellation, or after the start because the worker was down, is skipped).
    /// </summary>
    private async Task<Built> BuildBookingNotificationAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var bookingId = BookingRefPayload.Deserialize(message.Payload).BookingId;
        var booking = await (
                from candidate in dbContext.Bookings.AsNoTracking()
                where candidate.Id == bookingId
                join pitch in dbContext.Pitches on candidate.PitchId equals pitch.Id
                join club in dbContext.Clubs on pitch.ClubId equals club.Id
                select new
                {
                    candidate.Status,
                    candidate.PlayerId,
                    candidate.StartsAt,
                    candidate.EndsAt,
                    PitchName = pitch.Name,
                    ClubName = club.Name,
                    club.Address,
                    club.Locality,
                    club.TimeZoneId,
                    club.OwnerId,
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (booking?.PlayerId is not { } playerId)
        {
            return new Built(null);
        }

        var isReminder = message.Type == OutboxMessageTypes.BookingReminder;
        if (isReminder && booking.Status != BookingStatus.Confirmed)
        {
            return new Built(null, SkippedNotConfirmed);
        }

        if (isReminder && booking.StartsAt <= Now())
        {
            return new Built(null, SkippedStarted);
        }

        var player = await dbContext.Users.AsNoTracking().Where(user => user.Id == playerId).Select(user => new { user.Name, user.Email }).SingleOrDefaultAsync(cancellationToken);
        var recipient = isReminder
            ? player
            : await dbContext.Users.AsNoTracking().Where(user => user.Id == booking.OwnerId).Select(user => new { user.Name, user.Email }).SingleOrDefaultAsync(cancellationToken);
        if (player is null || recipient?.Email is not { Length: > 0 } address)
        {
            return new Built(null);
        }

        // US29, US23: a deactivated owner is not mailed about bookings (their clubs take none): closed like a reminder for a cancelled booking.
        if (!isReminder && !await accountEmails.IsActiveAsync(booking.OwnerId))
        {
            return new Built(null, SkippedOwnerDeactivated);
        }

        var details = new BookingNotificationDetails(
            booking.ClubName, booking.Address, booking.Locality, booking.TimeZoneId, booking.PitchName, booking.StartsAt, booking.EndsAt, player.Name);
        var content = isReminder ? BookingNotificationComposer.Reminder(details, recipient.Name) : BookingNotificationComposer.OwnerNewBooking(details, recipient.Name);
        return new Built(new EmailMessage(address, recipient.Name, content.Subject, content.TextBody, content.HtmlBody));
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox message {MessageId} ({Type}) skipped: {Reason}; it was not sent.")]
    private static partial void LogSkipped(ILogger logger, Guid messageId, string type, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox message {MessageId} ({Type}) sent.")]
    private static partial void LogSent(ILogger logger, Guid messageId, string type);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox message {MessageId} ({Type}) failed on attempt {Attempts} ({Error}); next attempt at {NextAttemptAt:O}.")]
    private static partial void LogRetry(ILogger logger, Guid messageId, string type, int attempts, string error, DateTime nextAttemptAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({Type}) given up after {Attempts} failed attempts ({Error}); it will not be sent.")]
    private static partial void LogGivenUp(ILogger logger, Guid messageId, string type, int attempts, string error);
}
