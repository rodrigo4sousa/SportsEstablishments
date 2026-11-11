# 0011 · Deleting the account by anonymization

- **Status:** Accepted on 2026-11-10 (US30), with the decisions marked as provisional in the [backlog](../../backlog.md).
- **Date:** 2026-11-10.
- **Elements:** API-AUTH, API-AUTH-PRIVACY, DB-USERS, DB-REFRESH, DB-BOOKINGS, DB-OUTBOX, D-BOOKING, WEB-F-ACCOUNT, DEV-E2E.

## Context

T07 published the Privacy Policy and left erasure and portability as a request by email, handled by hand. US30 moves them into the application. What the repository imposes:

- **Bookings are never deleted** and the clubs need the history; `bookings.player_id` points to `users`.
- **A club always has an owner**, and `owner_status_changes` (append-only, with `RESTRICT` keys) points to the accounts.
- **One email, one account**: the unique index on `normalized_email` has to become free so that the person can register again.
- **The access token is not checked on every request** (15 minutes accepted for a deactivated account, US29). Whoever deletes the account expects it to stop working at once, on every device.

Alternatives:

1. **Delete the `users` row.** It forces deleting or detaching everything that points to it: the clubs lose their history and it clashes with `owner_status_changes`.
2. **Only deactivate.** The personal data would stay in the database: that is not an erasure.
3. **Anonymize the row.** Everything that points to it stays valid, and it stops saying who the person was.

## Decision

Option 3, in `POST /api/me/delete-account`, with the password again:

- **In a single transaction** (`AccountPrivacyStore.DeleteAsync`): it locks the account row (`FOR UPDATE`, which waits for a booking being inserted through the foreign key); checks again whether it has clubs; replaces the name with `Conta apagada` (the stored placeholder name, "Deleted account") and the email and user name with `deleted-<id>@deleted.invalid` (`.invalid` does not receive mail, RFC 2606); deletes the password, the phone number and the record of acceptance; changes the security stamp; sets `deactivated_at` (all the deactivated-account checks now apply) and `deleted_at`; cancels future bookings; deletes the refresh tokens and the rest of Identity; deletes the unsent emails. Bookings become `CancelledByPlayer` through a conditional `UPDATE`, without the cancellation window: the person must not be stuck with a booking of an account that no longer exists.
- **The access token stops working at once.** `OnTokenValidated` asks, on every authenticated request, whether the account was deleted (`IAccountStore.IsDeletedAsync`, a read by primary key) and refuses it. This is only for deletion: a deactivated account keeps the 15 minutes accepted in US29. Without this, a token on another device could keep booking on behalf of a deleted account.
- **Refusals:** an owner with clubs (422 `owner_has_clubs`, whatever the state of the pitches; clubs have no state of their own) and the admin (422 `admin_cannot_delete_account`). They come before the password: they are about the account, which the session already identifies. A wrong password is 400 `invalid_password` and not 401, because for the browser client a 401 is an expired session, which it would try to renew; it counts towards the lockout, as in login.
- **No confirmation email**, because that would mean keeping the address. The confirmation appears on screen and the cookies are cleared.
- **Export:** `GET /api/me/data-export` returns a JSON file with the profile, the bookings and the acceptance of the Terms, without hashes, tokens, internal ids or other people's data. Per-IP limit, like the other sensitive endpoints.
- **Interface:** the `/account` page (`A minha conta`, "My account"), for any profile, with the section `Privacidade e dados` ("Privacy and data") (WEB-F-ACCOUNT).
- **E2E:** a fourth flow in `e2e/`, in addition to the three of [ADR 0010](0010-playwright-end-to-end-tests.md), because it is an irreversible operation that crosses the site, the cookies and the database. The stability rules of that ADR still apply.

## Consequences

- A new column (`users.deleted_at`, optional, compatible with the previous version of the API) and, on every authenticated request, one more read by primary key.
- A deleted account stops counting in the admin's totals and owners list, but stays in the registrations-over-time charts: the registration happened.
- What is not anonymized, because it is not an account field: a reason written by hand by a club when cancelling or by an admin when deactivating an owner (the latter is append-only). The Privacy Policy says so.
- Outbox messages already closed (sent, failed or skipped) stay: they have the id, which no longer identifies anyone, and club data.
- Known limit: a request that has already passed the token check and inserts a booking after the `COMMIT` (milliseconds) would leave a confirmed booking on a deleted account. The worker never sends an email to a `deleted.invalid` address.
