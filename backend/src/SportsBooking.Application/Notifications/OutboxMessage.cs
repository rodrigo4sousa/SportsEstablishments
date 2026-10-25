namespace SportsBooking.Application.Notifications;

/// <summary>The kinds of message the outbox carries (US22). Stored as text; US21 adds the reminder.</summary>
public static class OutboxMessageTypes
{
    public const string BookingConfirmed = "booking_confirmed";

    public const string BookingCancelledByPlayer = "booking_cancelled_by_player";

    public const string BookingCancelledByClub = "booking_cancelled_by_club";

    /// <summary>US05: the link to set a new password; the token is made when it is sent.</summary>
    public const string PasswordReset = "password_reset";

    /// <summary>US05: the notice that the password was changed.</summary>
    public const string PasswordChanged = "password_changed";
}

/// <summary>
/// One email to send, written in the same transaction and the same <c>SaveChanges</c> as the booking or the
/// cancellation it is about (docs/stack.md, "Trabalho em segundo plano"), so a booking that is rolled back (a lost race,
/// 23P01) never produces an email, and one that is committed always does. The <see cref="Payload"/> holds only what the
/// email shows (JSON); the recipient's address is read when sending, so it is always the player's current one and is
/// never stored here. Delivery is at least once: a worker that sends and then crashes before recording it sends again.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>The longest <see cref="LastError"/> kept: the exception's type name, never its message (it could hold an address).</summary>
    public const int LastErrorMaximumLength = 200;

    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; } = string.Empty;

    /// <summary>JSON, <c>jsonb</c> in the database.</summary>
    public string Payload { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }

    /// <summary>How many sends have failed so far.</summary>
    public int Attempts { get; private set; }

    /// <summary>When a worker may try (again).</summary>
    public DateTime NextAttemptAt { get; private set; }

    public DateTime? SentAt { get; private set; }

    /// <summary>Set when the worker gave up (too many failures, or nobody to send it to); it is never tried again.</summary>
    public DateTime? FailedAt { get; private set; }

    /// <summary>Why the last attempt failed, without personal data: an exception type name or a fixed code.</summary>
    public string? LastError { get; private set; }

    public bool IsPending => SentAt is null && FailedAt is null;

    public static OutboxMessage Create(string type, string payload, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        var at = EnsureUtc(now);
        return new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            Payload = payload,
            CreatedAt = at,
            NextAttemptAt = at,
        };
    }

    public void MarkSent(DateTime now)
    {
        SentAt = EnsureUtc(now);
        LastError = null;
    }

    /// <summary>
    /// A send failed: one more attempt counted; the next one after the back-off, or none if that was the last allowed.
    /// Returns true when the message is now given up.
    /// </summary>
    public bool RecordFailure(string error, DateTime now, OutboxRetry retry)
    {
        ArgumentNullException.ThrowIfNull(retry);
        var at = EnsureUtc(now);
        Attempts++;
        LastError = Truncate(error);
        if (Attempts >= retry.MaxAttempts)
        {
            FailedAt = at;
            return true;
        }

        NextAttemptAt = at + retry.DelayAfter(Attempts);
        return false;
    }

    /// <summary>Gives up at once, without counting an attempt (e.g. the player's account is gone).</summary>
    public void GiveUp(string error, DateTime now)
    {
        LastError = Truncate(error);
        FailedAt = EnsureUtc(now);
    }

    private static string Truncate(string error) =>
        string.IsNullOrWhiteSpace(error) ? "unknown" : error.Length <= LastErrorMaximumLength ? error : error[..LastErrorMaximumLength];

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : throw new ArgumentException("Times must be UTC.", nameof(value));
}

/// <summary>
/// When to try again after a failed send: the first retry after <see cref="FirstDelay"/>, each next one after twice the
/// previous delay, never more than <see cref="MaximumDelay"/>; after <see cref="MaxAttempts"/> failures the message is
/// given up. The values come from the <c>Outbox</c> settings (provisional defaults: 1 minute, 1 hour, 6 attempts).
/// </summary>
public sealed record OutboxRetry(TimeSpan FirstDelay, TimeSpan MaximumDelay, int MaxAttempts)
{
    /// <summary>The wait after the <paramref name="failedAttempts"/>-th failure (1, 2, 3, …).</summary>
    public TimeSpan DelayAfter(int failedAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failedAttempts, 1);
        // 2^(n-1) times the first delay, computed so that a large n cannot overflow.
        var delay = FirstDelay;
        for (var i = 1; i < failedAttempts && delay < MaximumDelay; i++)
        {
            delay += delay;
        }

        return delay < MaximumDelay ? delay : MaximumDelay;
    }
}
