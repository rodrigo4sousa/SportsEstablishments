using SportsBooking.Application.Notifications;
using SportsBooking.Infrastructure.Notifications;

namespace SportsBooking.UnitTests.Notifications;

/// <summary>US22: the back-off of the email outbox, and what a message records as it is tried.</summary>
public sealed class OutboxRetryTests
{
    private static readonly DateTime Now = new(2026, 5, 4, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>The provisional defaults of the <c>Outbox</c> settings.</summary>
    private static readonly OutboxRetry Defaults = new OutboxOptions().Retry;

    [Fact]
    public void The_defaults_are_one_minute_doubling_up_to_an_hour_and_six_attempts()
    {
        Defaults.FirstDelay.ShouldBe(TimeSpan.FromMinutes(1));
        Defaults.MaximumDelay.ShouldBe(TimeSpan.FromHours(1));
        Defaults.MaxAttempts.ShouldBe(6);
    }

    [Fact]
    public void The_delay_doubles_after_each_failure()
    {
        Enumerable.Range(1, 6).Select(Defaults.DelayAfter).ShouldBe(
        [
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(16), TimeSpan.FromMinutes(32),
        ]);
    }

    [Fact]
    public void The_delay_never_goes_above_the_maximum_and_does_not_overflow()
    {
        Defaults.DelayAfter(7).ShouldBe(TimeSpan.FromHours(1));
        Defaults.DelayAfter(100).ShouldBe(TimeSpan.FromHours(1));
        Defaults.DelayAfter(int.MaxValue).ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public void There_is_no_delay_before_the_first_failure()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Defaults.DelayAfter(0));
    }

    [Fact]
    public void A_new_message_is_due_at_once_with_no_attempts()
    {
        var message = OutboxMessage.Create(OutboxMessageTypes.BookingConfirmed, "{}", Now);

        message.Attempts.ShouldBe(0);
        message.NextAttemptAt.ShouldBe(Now);
        message.CreatedAt.ShouldBe(Now);
        message.IsPending.ShouldBeTrue();
    }

    [Fact]
    public void Each_failure_counts_and_moves_the_next_attempt_by_the_back_off_until_the_last_gives_up()
    {
        var message = OutboxMessage.Create(OutboxMessageTypes.BookingConfirmed, "{}", Now);
        var at = Now;
        var schedule = new List<DateTime>();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            message.RecordFailure("SmtpCommandException", at, Defaults).ShouldBeFalse();
            message.Attempts.ShouldBe(attempt);
            message.IsPending.ShouldBeTrue();
            schedule.Add(message.NextAttemptAt);
            at = message.NextAttemptAt;
        }

        schedule.Select(time => time - Now).ShouldBe(
            [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(7), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(31)]);
        message.RecordFailure("SmtpCommandException", at, Defaults).ShouldBeTrue();
        message.Attempts.ShouldBe(6);
        message.FailedAt.ShouldBe(at);
        message.IsPending.ShouldBeFalse();
        message.LastError.ShouldBe("SmtpCommandException");
    }

    [Fact]
    public void Being_sent_clears_the_last_error()
    {
        var message = OutboxMessage.Create(OutboxMessageTypes.BookingConfirmed, "{}", Now);
        message.RecordFailure("IOException", Now, Defaults);

        message.MarkSent(Now.AddMinutes(1));

        message.SentAt.ShouldBe(Now.AddMinutes(1));
        message.LastError.ShouldBeNull();
        message.IsPending.ShouldBeFalse();
    }

    [Fact]
    public void Giving_up_counts_no_attempt()
    {
        var message = OutboxMessage.Create(OutboxMessageTypes.BookingConfirmed, "{}", Now);

        message.GiveUp("recipient_not_found", Now);

        message.Attempts.ShouldBe(0);
        message.FailedAt.ShouldBe(Now);
        message.LastError.ShouldBe("recipient_not_found");
    }

    [Fact]
    public void The_last_error_is_kept_short()
    {
        var message = OutboxMessage.Create(OutboxMessageTypes.BookingConfirmed, "{}", Now);

        message.RecordFailure(new string('x', 500), Now, Defaults);

        message.LastError!.Length.ShouldBe(OutboxMessage.LastErrorMaximumLength);
    }

    [Fact]
    public void Times_must_be_utc()
    {
        Should.Throw<ArgumentException>(() => OutboxMessage.Create(OutboxMessageTypes.BookingConfirmed, "{}", new DateTime(2026, 5, 4, 10, 0, 0, DateTimeKind.Local)));
    }

    // ---- The settings

    [Fact]
    public void Valid_email_settings_pass()
    {
        var result = new EmailOptionsValidator().Validate(null, new EmailOptions { Host = "smtp-relay.brevo.com", Port = 587, FromAddress = "reservas@exemplo.pt", Username = "u", Password = "p" });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Wrong_email_settings_are_named_and_their_values_are_not_shown()
    {
        var result = new EmailOptionsValidator().Validate(
            null,
            new EmailOptions { Host = "", Port = 0, FromAddress = "not-an-address", FromName = " ", Username = "secret-user", Password = null, TimeoutSeconds = 0 });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Email:Host");
        result.FailureMessage.ShouldContain("Email:Port");
        result.FailureMessage.ShouldContain("Email:FromAddress");
        result.FailureMessage.ShouldContain("Email:FromName");
        result.FailureMessage.ShouldContain("Email:Username and Email:Password");
        result.FailureMessage.ShouldContain("Email:TimeoutSeconds");
        result.FailureMessage.ShouldNotContain("not-an-address");
        result.FailureMessage.ShouldNotContain("secret-user");
    }

    [Fact]
    public void Wrong_outbox_settings_are_named()
    {
        var result = new OutboxOptionsValidator().Validate(
            null, new OutboxOptions { BatchSize = 0, PollIntervalSeconds = 0, FirstRetryDelaySeconds = 120, MaximumRetryDelaySeconds = 60, MaxAttempts = 0 });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Outbox:BatchSize");
        result.FailureMessage.ShouldContain("Outbox:PollIntervalSeconds");
        result.FailureMessage.ShouldContain("Outbox:FirstRetryDelaySeconds");
        result.FailureMessage.ShouldContain("Outbox:MaxAttempts");
        new OutboxOptionsValidator().Validate(null, new OutboxOptions()).Succeeded.ShouldBeTrue();
    }
}
