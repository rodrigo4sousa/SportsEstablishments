# Architecture decision records (ADR)

Each ADR records an important decision: the context, the decision, the alternatives considered and the consequences. An accepted ADR is not rewritten; if the decision changes, a new one is written that replaces it and the old one becomes "Superseded by …". The elements mentioned use the ids from the [inventory](../inventory.md).

File names: `NNNN-short-title.md` (for example `0001-one-app-three-areas.md`).

**Statuses:** "To be written" (no file yet; none today), "Proposed" (written, not yet accepted: the decision may change), "Accepted", "Superseded by …".

## Index

| No. | Title | Status | In one line |
| --- | --- | --- | --- |
| [0001](0001-one-app-three-areas.md) | A single application with three areas | Accepted | One Next.js app with `/`, `/manage` and `/admin` on top of one API, instead of one application per profile. |
| [0002](0002-session-in-httponly-cookies.md) | Session in httpOnly cookies instead of localStorage | Accepted | A short-lived JWT and a rotating refresh token in httpOnly cookies, on the same origin, so that no script can read the session. |
| [0003](0003-booking-exclusion-constraint.md) | Exclusion constraint against duplicate bookings | Accepted | The database guarantees that two confirmed bookings of the same pitch never overlap, even with simultaneous requests. |
| [0004](0004-outbox-for-emails.md) | Outbox for emails | Accepted | Emails go into a table in the same transaction as the change and a worker sends them later, without slowing down or failing the requests. |
| [0005](0005-single-marketplace.md) | One marketplace instead of one site per owner | Accepted | One player account works at every club of every owner; there are no per-owner sites or domains. |
| [0006](0006-guid-v7-ids.md) | GUID v7 public ids | Accepted | `Guid.CreateVersion7()` ids on business and account entities: they do not reveal how many exist and they sort by creation time. |
| [0007](0007-calculated-slots.md) | Slots are calculated, not stored | Accepted | Slots are generated from the schedule when they are needed; only bookings and blocks are stored, and a schedule change never touches bookings. |
| [0008](0008-data-protection-keys-in-the-database.md) | Data Protection keys in the database | Accepted | The keys that protect the recovery tokens live in PostgreSQL, so they survive restarts and serve several instances. |
| [0009](0009-deployment-target.md) | Deployment target | Accepted (T06) | Site on Vercel, API with the worker in a public free Render service, Supabase, Brevo and GitHub Actions; Next.js is the same-origin proxy, with no Caddy. Replaces the Stack's virtual machine with Caddy. Accepted after the first real deploy. |
| [0010](0010-playwright-end-to-end-tests.md) | End-to-end tests with Playwright | Accepted | Three flows in Chromium against docker compose with the demo data, in a separate workflow (`e2e.yml`) so it does not slow down the deploy. |
| [0011](0011-account-deletion-by-anonymization.md) | Deleting the account by anonymization | Accepted (US30) | The account row stays, without personal data, for the clubs' history; in a single transaction future bookings are cancelled, sessions end and unsent emails are removed. |
| [0012](0012-languages-without-locale-in-the-url.md) | Portuguese and English, with no language in the URL | Accepted (T09) | next-intl without i18n routing: the language comes from a cookie the switcher writes, then `Accept-Language`, then pt-PT; every string in two catalogs; emails in the recipient's language. |
