# 0008 · Data Protection keys in the database

- **Status:** Accepted.
- **Date:** 2026-10-30 (record; the decision comes from US05).
- **Elements:** CC-DATAPROT, DB-DPKEYS, API-AUTH-RESET, C-API, C-DB.

## Context

The password recovery links (US05) carry a token from Identity's provider, protected with ASP.NET Core Data Protection and valid for 1 hour. Data Protection stores its keys in a key ring; by default, in a local file in the container. A link sent before a restart, a new deploy or a second instance would stop being accepted, and the person would receive "invalid or expired link" through no fault of their own.

Alternatives:

1. **Keys in a local file** (the default). They are lost when the container is recreated, and each instance would have its own.
2. **A shared volume or storage.** It solves it, but it is a new piece to administer, and some deployment targets have no persistent disk.
3. **Keys in PostgreSQL**, which the application already uses.

## Decision

Option 3: `AddDataProtection().SetApplicationName("SportsBooking").PersistKeysToDbContext<AppDbContext>()`; the keys live in the `data_protection_keys` table (DB-DPKEYS), with `AppDbContext` implementing `IDataProtectionKeyContext`.

- The application name is fixed (`DependencyInjection.DataProtectionApplicationName`) and **is never changed**: it is part of every protected token.
- The token's lifetime is `PasswordResetRules.LinkLifetime` (1 hour). The token is made by the worker when the email is sent; the outbox stores only the account id ([ADR 0004](0004-outbox-for-emails.md)).

## Consequences

**Good**

- Restarts, new deploys and several instances keep accepting links already sent; nothing needs persistent disk.
- Everything stays in the database, with the same backups and the same connection.

**Bad or to watch**

- The code does not encrypt the keys at rest (there is no `ProtectKeysWith…`): **whoever has a copy of the database has the keys**, so a backup is as sensitive as the secrets.
- Identity's token generator reads the system clock, not `TimeProvider`: the lifetime cannot be tested with a fake clock, and the tests fabricate old tokens in Identity's format.
- Changing the application name invalidates every link in circulation.
