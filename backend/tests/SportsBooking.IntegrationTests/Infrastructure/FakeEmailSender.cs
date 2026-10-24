using System.Collections.Concurrent;
using SportsBooking.Application.Notifications;

namespace SportsBooking.IntegrationTests.Infrastructure;

/// <summary>
/// Stands in for SMTP in the outbox tests (US22): keeps what it is asked to send, and can fail on purpose. Its failure
/// message carries the recipient's address, as a real SMTP error may, so a test can check that the address never reaches
/// a log or the outbox row.
/// </summary>
public sealed class FakeEmailSender : IEmailSender
{
    private int _failuresLeft;
    private int _calls;

    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    /// <summary>Every send fails.</summary>
    public bool AlwaysFail { get; set; }

    /// <summary>Waits this long before each send (to make two workers overlap).</summary>
    public TimeSpan Delay { get; set; }

    public int Calls => Volatile.Read(ref _calls);

    /// <summary>The next <paramref name="count"/> sends fail, then they succeed.</summary>
    public void FailNext(int count) => Volatile.Write(ref _failuresLeft, count);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        if (AlwaysFail || Interlocked.Decrement(ref _failuresLeft) >= 0)
        {
            throw new SmtpTestException($"Mailbox unavailable: {message.ToAddress}");
        }

        Sent.Enqueue(message);
    }
}

public sealed class SmtpTestException(string message) : Exception(message);
