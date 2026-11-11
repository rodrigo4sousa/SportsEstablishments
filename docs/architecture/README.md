# Architecture

Architecture of the Pitch Booking App, in PlantUML diagrams and text. Each diagram link opens the SVG; the `.puml` link is the source. The documents describe the system as it is in the code and in production (live since 2026-11-09, T06): a Next.js app with three areas (player, owner, admin) on top of an ASP.NET Core API and a PostgreSQL database, with an email worker inside the API.

The rules are in the [backlog](../backlog.md), the technical choices in the [Stack](../stack.md) and the map of the code in [AGENTS.md](../../AGENTS.md). These documents do not repeat the rules: they show how the parts connect and point to the detail.

## How these documents are organized

Two ways of looking at the same system, because they answer different questions. **C4** zooms in by levels (the system, the containers, the components, the code). Kruchten's **4+1 views** look at one concern at a time (logical, process, development, physical and, in the centre, the scenarios).

- [`inventory.md`](inventory.md) is the catalogue of what exists: each element has a stable id (`C-API`, `API-BOOK`, `D-BOOKING`, `PL-RENDER`, …) and the path in the code, and section 12 links the ids to the drawn names. The diagrams are drawn from it.
- [`c4/README.md`](c4/README.md) has the text for the context, the containers and the deployment; [`4plus1/README.md`](4plus1/README.md) says, per view, who it is for and which decision it documents, and links to the text of each.
- [`adr/`](adr/README.md) has the decisions, with context, alternatives and consequences. An accepted ADR is not rewritten: if the decision changes, another one that replaces it is written.

All the diagrams follow the same rules:

- **One level of abstraction per diagram:** one C4 level or one 4+1 view.
- **No invented elements:** everything drawn exists in the code or in the configuration. A new element goes into the inventory first, in the same pull request.
- **What is planned is kept apart:** it is drawn dashed (the `planned` tag in [`style.puml`](style.puml)) and only if it does not exist yet. Today no diagram uses it.
- **Ids live in the inventory and in the aliases, not in the labels:** a diagram draws only names, with no ids in names, stereotypes or packages (a stereotype stays only when it has meaning, such as `<<external>>` or `<<sealed>>`). The PlantUML alias is the id with `_` (`C_API`), and [section 12 of the inventory](inventory.md#12-names-and-aliases-in-the-diagrams) links each alias to the id and to the drawn names.
- **C4 includes only in `c4/`:** the diagrams in `4plus1/` are plain UML, and the sequences use `participant`, `box`, `database` and `autonumber`.
- **Compact boxes in C4:** name and technology, with no description or legend; each element's responsibility is in the tables of [`c4/README.md`](c4/README.md#elements-of-each-diagram) and, for the sequences, in the [process view](4plus1/process.md#5-participants-in-each-diagram).
- **Titles** in the format `<level or view> - <subject>`.
- **A common style:** all include [`style.puml`](style.puml) and start with a comment with the title, the version, the date, what is not drawn and, in the 4+1 views, who it is for and which decision it documents.
- **Arrows with a verb in English** from the caller's point of view, and the protocol or format in the technology argument or, in the sequences and plain UML, in square brackets in the text itself.
- **About 10 elements per diagram** (see [Known limitations](#known-limitations)).

A pull request that creates, removes or changes the responsibility or the path of an element updates the inventory and the diagrams that show it.

## Index

### C4 ([`c4/`](c4/))

The text is in [`c4/README.md`](c4/README.md), except that of the components (level 3), which is in the [logical view](4plus1/logical.md).

| Diagram | Source | Shows |
|---|---|---|
| [1. Context](out/01-context.svg) | [`01-context.puml`](c4/01-context.puml) | People (visitor, player, owner, admin), the system as one box, the SMTP server and the hosting platforms |
| [2. Containers: development](out/02-containers-dev.svg) | [`02-containers-dev.puml`](c4/02-containers-dev.puml) | Browser, Next.js, API with the email worker, PostgreSQL and Mailpit (docker compose) |
| [2. Containers: production](out/02-containers-prod.svg) | [`02-containers-prod.puml`](c4/02-containers-prod.puml) | Browser, Next.js on Vercel, API and worker on Render, PostgreSQL on Supabase, Brevo |
| [3. API components: overview](out/03-components-api-overview.svg) | [`03-components-api-overview.puml`](c4/03-components-api-overview.puml) | The components by feature and the cross-cutting pieces |
| [3. API components: bookings](out/03-components-api-bookings.svg) | [`03-components-api-bookings.puml`](c4/03-components-api-bookings.puml) | A single booking, weekly series, my bookings, the owner's side, and the shared slot check |
| [3. API components: accounts](out/03-components-api-accounts.svg) | [`03-components-api-accounts.puml`](c4/03-components-api-accounts.puml) | Registration, login, sessions, password recovery, seeds and commands |
| [3. API components: notifications](out/03-components-api-notifications.svg) | [`03-components-api-notifications.puml`](c4/03-components-api-notifications.puml) | Outbox, worker, composing and sending the emails |
| [3. Front end components: routes and guards](out/03-components-web.svg) | [`03-components-web.puml`](c4/03-components-web.puml) | Routes, guards and the front end's paths to the API |
| [3. Front end components: features](out/03-components-web-features.svg) | [`03-components-web-features.puml`](c4/03-components-web-features.puml) | The areas and the feature folders of each |
| [Deployment: development](out/04-deployment-dev.svg) | [`04-deployment-dev.puml`](c4/04-deployment-dev.puml) | The development machine, the compose network, the ports and the database volume |
| [Deployment: production](out/04-deployment-prod.svg) | [`04-deployment-prod.puml`](c4/04-deployment-prod.puml) | Vercel, Render, Supabase, Brevo and GitHub Actions |

### 4+1 ([`4plus1/`](4plus1/))

Who each view is for and which decision it documents: [`4plus1/README.md`](4plus1/README.md).

| View | Text | Diagrams |
|---|---|---|
| Logical | [`logical.md`](4plus1/logical.md) | [back end layers](out/logical-layers.svg) ([`.puml`](4plus1/logical-layers.puml)), [domain: clubs, pitches, blocks and bookings](out/logical-domain-booking.svg) ([`.puml`](4plus1/logical-domain-booking.puml)), [domain: accounts, sessions, owner history and outbox](out/logical-domain-accounts.svg) ([`.puml`](4plus1/logical-domain-accounts.puml)), [states of a booking](out/logical-state-booking.svg) ([`.puml`](4plus1/logical-state-booking.puml)), [states of an outbox message](out/logical-state-outbox.svg) ([`.puml`](4plus1/logical-state-outbox.puml)), [states of an owner](out/logical-state-owner.svg) ([`.puml`](4plus1/logical-state-owner.puml)), [code: the Booking aggregate](out/logical-code-booking.svg) ([`.puml`](4plus1/logical-code-booking.puml)), [code: slots, «free» and price](out/logical-code-slots.svg) ([`.puml`](4plus1/logical-code-slots.puml)) |
| Process | [`process.md`](4plus1/process.md) | [processes and threads](out/process-runtime.svg) ([`.puml`](4plus1/process-runtime.puml)), [pipeline of an API request](out/process-pipeline.svg) ([`.puml`](4plus1/process-pipeline.puml)), [booking a slot](out/process-book.svg) ([`.puml`](4plus1/process-book.puml)), [weekly series](out/process-series.svg) ([`.puml`](4plus1/process-series.puml)), [cancelling (player)](out/process-cancel-player.svg) ([`.puml`](4plus1/process-cancel-player.puml)), [cancelling (owner)](out/process-cancel-club.svg) ([`.puml`](4plus1/process-cancel-club.puml)), [login and logout](out/process-login.svg) ([`.puml`](4plus1/process-login.puml)), [refresh on the API](out/process-refresh.svg) ([`.puml`](4plus1/process-refresh.puml)), [refresh in the browser](out/process-refresh-client.svg) ([`.puml`](4plus1/process-refresh-client.puml)), [requesting a new password](out/process-reset-request.svg) ([`.puml`](4plus1/process-reset-request.puml)), [setting the new password](out/process-reset-confirm.svg) ([`.puml`](4plus1/process-reset-confirm.puml)), [email worker](out/process-outbox.svg) ([`.puml`](4plus1/process-outbox.puml)), [skipped notices and reminders](out/process-outbox-skip.svg) ([`.puml`](4plus1/process-outbox-skip.puml)), [guarded page](out/process-guard.svg) ([`.puml`](4plus1/process-guard.puml)), [deactivating an owner](out/process-owner-deactivation.svg) ([`.puml`](4plus1/process-owner-deactivation.puml)) |
| Development | [`development.md`](4plus1/development.md) | [repository structure](out/development-repo.svg) ([`.puml`](4plus1/development-repo.puml)), [back end projects](out/development-backend.svg) ([`.puml`](4plus1/development-backend.puml)), [dependency injection](out/development-di.svg) ([`.puml`](4plus1/development-di.puml)), [front end folders](out/development-frontend.svg) ([`.puml`](4plus1/development-frontend.puml)), [from the controller to the types](out/development-api-types.svg) ([`.puml`](4plus1/development-api-types.puml)), [CI (ci.yml)](out/development-ci.svg) ([`.puml`](4plus1/development-ci.puml)), [deploy, production data and keepalive (deploy.yml, prod-data.yml, keepalive.yml)](out/development-deploy.svg) ([`.puml`](4plus1/development-deploy.puml)) |
| Physical | [`physical.md`](4plus1/physical.md) | [UML deployment of production](out/physical.svg) ([`.puml`](4plus1/physical.puml)) |
| Scenarios (+1) | [`scenarios.md`](4plus1/scenarios.md) | [use cases](out/scenarios-use-cases.svg) ([`.puml`](4plus1/scenarios-use-cases.puml)) |

### Decisions (ADR)

Status in [`adr/`](adr/README.md). In ADRs 0001 to 0008 the decision is older than the record and comes from the Stack or the backlog, as each one indicates.

| No. | Decision | Status |
|---|---|---|
| [0001](adr/0001-one-app-three-areas.md) | A single application with three areas | Accepted |
| [0002](adr/0002-session-in-httponly-cookies.md) | Session in httpOnly cookies instead of localStorage | Accepted |
| [0003](adr/0003-booking-exclusion-constraint.md) | Exclusion constraint against duplicate bookings | Accepted |
| [0004](adr/0004-outbox-for-emails.md) | Outbox for emails | Accepted |
| [0005](adr/0005-single-marketplace.md) | One marketplace instead of one site per owner | Accepted |
| [0006](adr/0006-guid-v7-ids.md) | GUID v7 public ids | Accepted |
| [0007](adr/0007-calculated-slots.md) | Slots are calculated, not stored | Accepted |
| [0008](adr/0008-data-protection-keys-in-the-database.md) | Data Protection keys in the database | Accepted |
| [0009](adr/0009-deployment-target.md) | Deployment target | Accepted on 2026-11-09, after the first real deploy |
| [0010](adr/0010-playwright-end-to-end-tests.md) | End-to-end tests with Playwright | Accepted on 2026-11-10 |
| [0011](adr/0011-account-deletion-by-anonymization.md) | Deleting the account by anonymization | Accepted on 2026-11-10 |

## Known limitations

The diagrams show the code as it is, with its flaws. These are facts about the code that the views and the ADRs state as they are; they are not mistakes.

1. **The API can also be reached directly, in development.** Besides going through Next.js, the API is on compose port 5080 (Swagger at `/swagger`), and `http://localhost:5080` is one of the origins accepted in Development. The development container and deployment diagrams show both entry points.
2. **The reverse proxy is Next.js, not Caddy.** The browser talks to C-WEB and it is Next.js (WEB-REWRITE) that forwards `/api/*` to C-API, in development and in production. The client IP is in the [physical view, 4.4](4plus1/physical.md#44-the-client-ip).
3. **Persisted entities outside Domain.** The account (`AppUser`, from Identity), the refresh token and an owner's status change live in Infrastructure, and the outbox message in Application. In Domain the account exists only as rules (`Dom/Accounts`). The domain diagram shows them with the layer they belong to.
4. **One exception to the injected clock.** All current time comes from `TimeProvider` (CC-TIME), except Identity's password-reset token generator, which reads the system clock; that is why the tests fabricate old tokens in Identity's format.
5. **The exclusion constraint only covers `Confirmed` bookings.** `bookings_no_overlap` (DB-NOOVERLAP) has `WHERE status = 'Confirmed'`: a cancelled booking or a no-show does not occupy the slot, and undoing a no-show only succeeds if no other confirmed booking overlaps in the meantime; otherwise the database refuses (23P01 → 409 `slot_taken`).

**Diagram size.** The rule is about 10 elements; the API component diagrams go up to 16 because they are already split by area. The count below is approximate (element lines in the `.puml` files, steps in the activity diagrams). These exceed 12:

- `development-ci` (24 steps, in three jobs with at most 10 each), `physical` (18) and `04-deployment-prod` (17);
- `03-components-api-overview` and `03-components-api-bookings` (16), `03-components-api-notifications` and `03-components-api-accounts` (15);
- `development-frontend` (14), `03-components-web-features` (13) and `process-pipeline` (13).

The widest ones become unreadable when reduced to the width of a page; open the SVG at real size.

**Deployment.** The deploy sections of the [Stack](../stack.md) (Caddy, the virtual machine, the `Caddyfile`, the GitHub Container Registry, deploy over SSH) are replaced by [ADR 0009](adr/0009-deployment-target.md), which names them; the Stack was not changed. On this point the diagrams and the ADR prevail, not the Stack. What the deployment views do **not** cover or leave open:

- The client IP in production is best-effort: the API believes `X-Forwarded-For` with `ForwardLimit=1` and is public, so the per-IP limits are not a strong protection. Render's `X-Forwarded-For` chain is not documented ([physical view, 4.4](4plus1/physical.md#44-the-client-ip)).
- The worker runs inside the API, which goes to sleep without traffic on Render's free plan: emails and reminders only go out while the API is awake.
- Not adopted (ADR 0009): a domain of our own, automatic backups, a private network between Vercel and Render.
- The points the first real deploy did not confirm, and the accepted risks, are in the [physical view, section 5](4plus1/physical.md#5-risks-and-open-points-for-t06).
- The providers' plan and price data change; the data in the physical view carry a date and a source.

**Text and code.** `docs/stack.md` shows a `Caddyfile` in the repository tree that does not exist. Everything was checked by hand against the code; there is no test that proves a diagram matches the code. What the automatic check guarantees is in [Rendering the diagrams](#rendering-the-diagrams).

## Rendering the diagrams

The SVGs in [`out/`](out/) are generated from the `.puml` files; they are not edited by hand. [`render.sh`](render.sh) (Git Bash, macOS, Linux) and [`render.ps1`](render.ps1) (Windows PowerShell) draw them all with the `plantuml/plantuml` Docker image, **whose version is pinned in [`style.puml`](style.puml)** (the line `' plantuml-version: X.Y.Z`). The typeface (Liberation Sans, metric-compatible with Arial) is in [`fonts/`](fonts/README.md) and is mounted read-only in the container, where PlantUML measures the text. Afterwards, [`postprocess-svg.sh`](postprocess-svg.sh) replaces, in every SVG, the typeface name with the list `Arial, Liberation Sans, Helvetica, sans-serif`. No font from the computer and no network is used, so the result is the same on any machine and in CI. Docker has to be running.

```bash
bash docs/architecture/render.sh
powershell -ExecutionPolicy Bypass -File docs\architecture\render.ps1
```

1. Edit the `.puml` (those in `c4/` use `!include <C4/…>`, which PlantUML ships, and then `!include ../style.puml`; those in `4plus1/` only `style.puml`) and, if there are new elements, the inventory.
2. Render, and commit the `.puml` and the `.svg` together.
3. Put the diagram in the [index](#index) of this file. To change the PlantUML version, change the line in `style.puml` and render everything again.
4. Run `scripts/check-architecture-docs.sh` (or `.ps1`): it fails if any `.svg` in `out/` has no link in this file, if this file or another `.md` in `docs/` or the root `README.md` has a relative link or image that does not reach a file, if any `.puml` does not include `../style.puml`, if any `.svg` mentions DejaVu, if any file in `4plus1/` includes C4, or if the diagrams' aliases and section 12 of the inventory do not match (in both directions, with each id existing in the inventory). It does not check the anchors (`#section`) or the text of the labels.

The `diagrams` job in CI ("Docs (architecture diagrams)") runs `render.sh`, fails if any `.svg` changed or is missing (`git status` of `out/` must stay clean) and runs the checker.
