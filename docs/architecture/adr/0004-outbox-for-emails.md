# 0004 · Outbox for emails

- **Status:** Accepted.
- **Date:** 2026-10-30 (record; the decision comes from the [Stack](../../stack.md) and from US22).
- **Elements:** C-WORKER, C-DB, API-NOTIF, API-NOTIF-OUTBOX, API-NOTIF-DISPATCH, API-NOTIF-PROC, API-NOTIF-COMPOSE, API-NOTIF-SENDER, D-OUTBOX, DB-OUTBOX, DB-OUTBOXUQ, X-SMTP.

## Context

Booking, cancelling and recovering the password have to send emails (confirmations, owner notices, reminders, recovery links). The SMTP server may be slow or down, and sending must not slow down or fail the booking. But the email must not be lost either, nor go out for a booking that turned out not to be saved.

Alternatives:

1. **Send inside the request.** It slows the request down and an SMTP failure fails a booking that was already made (or, if the error is ignored, the email is lost).
2. **An in-memory queue or "fire and forget".** Everything is lost when the process stops, and there is no way to postpone a reminder for hours.
3. **An `outbox_messages` table in the same database and a worker.**

## Decision

Option 3:

- **Writing:** the service attaches the `OutboxMessage` to the request's `IAppDbContext` **before** `SaveChanges`, inside the pitch lock transaction; the message and the booking are saved, or undone, together. The payload never carries addresses: it carries ids (the booking, or only the account), and the recipient and the text are read at send time.
- **Sending:** `OutboxDispatcher` (a `BackgroundService` in the API process) runs `OutboxProcessor.ProcessDueAsync` in one transaction per batch, with `SELECT … FOR UPDATE SKIP LOCKED` (batch of 20, pass every 10 s). It composes the email in pt-PT in Lisbon time and sends it over SMTP (MailKit, one connection per message).
- **Failures:** each failure counts as an attempt and postpones the next one with a doubling interval (1, 2, 4, 8, 16 minutes, at most 1 hour); on the 6th the message is marked failed and a Warning is logged with the id and the exception type, never the address. A message that no longer applies (a reminder for a cancelled or already started booking, a notice to a deactivated owner) is closed with `Skip`, which is not a failure.
- A partial unique index (DB-OUTBOXUQ) guarantees at most one reminder and one owner notice per booking.

## Consequences

**Good**

- An email exists if and only if the change was committed; a booking lost in the race leaves no email.
- An SMTP failure never fails a request, and sending resumes when the server comes back.
- Several API instances can run the worker without picking up the same message.
- Tests do not need SMTP: they run the processor by hand with a `FakeEmailSender`.

**Bad or to watch**

- **At least once:** sending happens before the batch's `COMMIT`; if the process stops between the two, the batch's messages become pending again and may go out twice.
- The worker lives in the API process: in production the API has to be always on, otherwise emails stop and reminders arrive late or are skipped.
- The summary of a weekly series is saved after the dates, in a separate transaction: a crash between the two steps loses the summary (provisional decision of US20).
