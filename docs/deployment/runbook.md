# Deployment runbook (T06)

The steps that only the project owner can do, in the right order. The target is Vercel, Render, Supabase, Brevo and GitHub Actions, at **zero cost**. The variables are explained in [environment-variables.md](environment-variables.md).

These steps were followed on the first real deploy (2026-11-09), with no differences from what is written.

The names of buttons and fields may change in the providers' dashboards: where it says "(the name may vary)", look for the setting described.

## Values to note down

Keep these values in a private place. Never write them in the repository.

| Value | Where it comes from |
| --- | --- |
| `<REF>` | Supabase project reference |
| `<POOLER_HOST>` | Host of the session-mode pooler, from Supabase |
| `<OWNER_PASSWORD>` | Database password, chosen when creating the Supabase project |
| `<APP_PASSWORD>` | Password of `sportsbooking_app`, chosen by you (long and random, letters and digits only) |
| `<SMTP_LOGIN>`, `<SMTP_KEY>`, `<SENDER>` | Brevo |
| `<RENDER_URL>` | `https://<service>.onrender.com`, without a trailing slash |
| `<VERCEL_URL>` | `https://<project>.vercel.app`, without a trailing slash (the production address, not that of a deploy) |
| `<DEPLOY_HOOK>` | Render |
| `<VERCEL_TOKEN>`, `<VERCEL_ORG_ID>`, `<VERCEL_PROJECT_ID>` | Vercel |

## 1. Supabase

- [ ] Create an account and a project on the free plan, in a **region near Frankfurt**. Keep `<OWNER_PASSWORD>`. In the current installation it is West EU (Ireland), the region the Privacy Policy states.
- [ ] Note `<REF>`.
- [ ] In the SQL Editor, confirm that the extension is available (it must return a row). **Do not enable it**: the migration creates it.
  ```sql
  select name from pg_available_extensions where name = 'btree_gist';
  ```
- [ ] Open [`supabase-roles.sql`](supabase-roles.sql), replace `REPLACE_WITH_A_LONG_RANDOM_PASSWORD` with `<APP_PASSWORD>`, paste it in the SQL Editor and run it. Run it **before** the first migration. Do not save the changed file in the repository.
- [ ] In the project's connection dialog, choose the **Session pooler** and copy the host: it is `<POOLER_HOST>`. Do not deduce it from the region.
- [ ] Build the two connections. **They must be in this format**, not the `postgresql://` format the dashboard shows:
  - Owner (GitHub secret):
    `Host=<POOLER_HOST>;Port=5432;Database=postgres;Username=postgres.<REF>;Password=<OWNER_PASSWORD>;SSL Mode=Require`
  - Application (Render):
    `Host=<POOLER_HOST>;Port=5432;Database=postgres;Username=sportsbooking_app.<REF>;Password=<APP_PASSWORD>;SSL Mode=Require;Maximum Pool Size=5`
- [ ] Always use the pooler's port **5432**. Do not use the direct host `db.<REF>.supabase.co` (it is IPv6 and GitHub's runners have no IPv6) or transaction mode (port 6543).

## 2. Brevo

- [ ] Create a free account.
- [ ] Add and **verify a sender by email**: it is the `<SENDER>`. Without a domain of your own, emails may land in spam.
- [ ] In the SMTP and API settings, generate an **SMTP key** (not an API key). Note `<SMTP_LOGIN>` and `<SMTP_KEY>`.
- [ ] Use the server `smtp-relay.brevo.com` and port **2525** with STARTTLS (it worked on the first deploy). Render's free plan blocks outbound ports 25, 465 and 587.

## 3. The two URLs depend on each other

Render needs Vercel's address (`Security__AllowedOrigins__0`, `PublicSite__BaseUrl`) and Vercel needs Render's address (`API_INTERNAL_URL`). To break the cycle:

1. Create the service on Render first (section 4), with `https://placeholder.invalid` in the two site variables. The first deploy may fail, no problem: the address already exists. Note `<RENDER_URL>`.
2. Create the project on Vercel (section 5), with `API_INTERNAL_URL=<RENDER_URL>`. Note `<VERCEL_URL>`.
3. Go back to Render and replace the two variables with the real `<VERCEL_URL>`.

## 4. Render

- [ ] Create a **Web Service** from the GitHub repository, branch `main`.
- [ ] **Do not add any payment method.** Without one, exceeding a limit suspends the service instead of charging.
- [ ] Settings:

  | Setting (the name may vary) | Value |
  | --- | --- |
  | Language / Runtime | `Docker` |
  | Root Directory | `backend` |
  | Dockerfile Path | `Dockerfile` (relative to the Root Directory) |
  | Docker Build Context Directory | `.` (relative to the Root Directory) |
  | Instance Type | `Free` |
  | Region | Frankfurt |
  | Auto-Deploy | **Off** |
  | Health Check Path, if it exists on the free plan | `/health` |

- [ ] Environment variables:

  | Name | Value |
  | --- | --- |
  | `ASPNETCORE_ENVIRONMENT` | `Production` |
  | `ConnectionStrings__Default` | The **application** connection (section 1) |
  | `Jwt__SigningKey` | The output of `openssl rand -base64 48` |
  | `Security__AllowedOrigins__0` | `<VERCEL_URL>` |
  | `PublicSite__BaseUrl` | `<VERCEL_URL>` |
  | `ForwardedHeaders__TrustAnyProxy` | `true` |
  | `ForwardedHeaders__ForwardLimit` | `1` (confirmed on the first deploy; see section 8) |
  | `Email__Host` | `smtp-relay.brevo.com` |
  | `Email__Port` | `2525` |
  | `Email__Security` | `StartTls` |
  | `Email__Username` | `<SMTP_LOGIN>` |
  | `Email__Password` | `<SMTP_KEY>` |
  | `Email__FromAddress` | `<SENDER>` |

- [ ] Do not set `PORT` (Render provides it), `AdminSeed__*`, `ADMIN_NEW_PASSWORD` or `DemoData__AllowInProduction`. The admin is created by `prod-data`.
- [ ] Copy the service's **Deploy Hook**: it is `<DEPLOY_HOOK>`. It is a secret: whoever has it can launch deploys.

## 5. Vercel

The `sports-establishments` project **already exists** on Vercel and must have Git disconnected: connected, every push launches a deploy outside the workflow, which fails without `API_INTERNAL_URL`. If you are using it, skip the creation and confirm the settings below.

- [ ] If the project does not exist, create it from the repository, on a **Hobby** account (non-commercial use only).
- [ ] **Root Directory:** `frontend`. Framework: Next.js.
- [ ] Environment variables, in the Production environment:

  | Name | Value |
  | --- | --- |
  | `API_INTERNAL_URL` | `<RENDER_URL>` |
  | `ENABLE_HSTS` | `true` |

- [ ] Keep Vercel's system variables exposure turned on (the name may vary).
- [ ] If the setting exists, choose a Functions region near Frankfurt, so Server Components are close to the API. In the current installation it is Washington DC (USA). If you change a region, update the Privacy Policy (`/privacy`), which states them.
- [ ] **Turn off the automatic Git deploys**, so only the workflow publishes: disconnect the repository in the project's Git settings (the name may vary) and also remove the Vercel GitHub app's access to this repository. The CLI works without a Git connection. Confirm on a new commit: no `Vercel` check may appear on GitHub.
- [ ] Create an access token in your account: it is `<VERCEL_TOKEN>`.
- [ ] To get the ids, in a temporary folder outside the repository, run `npx vercel@65.14.0 link` and read `.vercel/project.json`: `orgId` is `<VERCEL_ORG_ID>` and `projectId` is `<VERCEL_PROJECT_ID>`.

## 6. GitHub

In Settings → Secrets and variables → Actions:

- [ ] **Secrets:**

  | Name | Value |
  | --- | --- |
  | `DB_OWNER_CONNECTION_STRING` | The **owner** connection (section 1) |
  | `RENDER_DEPLOY_HOOK_URL` | `<DEPLOY_HOOK>` |
  | `VERCEL_TOKEN` | `<VERCEL_TOKEN>` |
  | `VERCEL_ORG_ID` | `<VERCEL_ORG_ID>` |
  | `VERCEL_PROJECT_ID` | `<VERCEL_PROJECT_ID>` |
  | `ADMIN_EMAIL` | The admin's email |
  | `ADMIN_PASSWORD` | The admin's password (at least 8 characters) |

- [ ] **Variables:**

  | Name | Value |
  | --- | --- |
  | `RENDER_API_URL` | `<RENDER_URL>` |
  | `SITE_URL` | `<VERCEL_URL>` |
  | `DEPLOY_ENABLED` | `true`, **only after** everything above is done |

## 7. First run, in this order

- [ ] In Actions → **Production data** → Run workflow, branch `main`, action **`migrate-only`**. It must finish green.
- [ ] In Actions → **Deploy**, re-run the last run (Re-run all jobs), or merge a change into `main`. It must finish green, including `smoke`.
- [ ] **Production data** → **`set-admin-password`**. It creates the admin, if it does not exist yet, with `ADMIN_EMAIL` and `ADMIN_PASSWORD`.
- [ ] **Production data** → **`seed-demo`**, if you want demo data. The log says it ran.

Every migration must work with the previous version of the API, because the new schema goes live before the new API.

## 8. Verification

- [ ] At `<VERCEL_URL>`, sign in as the admin and confirm that you go to `/admin`.
- [ ] In the browser tools, confirm the `access_token` and `refresh_token` cookies on Vercel's domain.
- [ ] Sign in as a demo player (see the README), make a booking and cancel it.
- [ ] Logs are kept 7 days on Render (Hobby workspace) and 1 hour on Vercel (Hobby): check them right after the deploy. The Privacy Policy states these periods; if you change plan, update it.
- [ ] Confirm the email: the Render log must show no worker failures, and the message reaches the player's inbox. The demo players use `example.test`, which does not receive email: to see a real email, register a player with an address of your own. In the SQL Editor:
  ```sql
  select type, sent_at, attempts, last_error from outbox_messages order by created_at desc limit 10;
  ```
- [ ] Confirm that no table is reachable through Supabase's data API. It must return `0`:
  ```sql
  select count(*) from information_schema.role_table_grants
  where grantee in ('anon', 'authenticated', 'service_role') and table_schema = 'public';
  ```
- [ ] **`ForwardLimit`:** with the value `1`, make more than 10 logins in a minute on the site, from your connection, with an email that does not exist, until you get 429. Look in the Render log for:
  ```
  Too many requests from client <address> to /api/auth/login
  ```
  If it is your address, it is right (on IPv6, your /64 network appears). If not, raise `ForwardedHeaders__ForwardLimit` by 1, deploy again and repeat. Use the lowest value that gives the right address. On the first deploy, the value `1` gave the right address.
- [x] ADR 0009's status became "Accepted" after the first deploy (2026-11-09).

## 9. Known limitations

- **Cold start:** the API goes to sleep after 15 minutes without traffic and takes about a minute to wake up.
- **Emails and reminders** only go out while the API is awake; a reminder whose booking has already started is skipped.
- **Best-effort rate limiting:** the API is public, and whoever connects to it directly can forge the IP. The real protection against guessing passwords is the account lockout.
- **The demo accounts' passwords are public** (they are in the README): anyone can sign in with them while the demo is online.
- **Supabase:** a free project with little database activity for 7 days is paused. The `keepalive.yml` workflow makes a query for that reason (section 10), but it is not a guarantee: if the project is paused, reactivate it in the dashboard ("Resume project").
- **No backups** and no domain of your own.

## 10. Keep the demo alive

Supabase's free plan pauses a project with little activity, and `/health` does not touch the database, so it does not count. The `.github/workflows/keepalive.yml` workflow makes a `GET {SITE_URL}/api/localities` through the public site: it is a read query against the database (the visible clubs), which also wakes the API on Render. It uses no secrets, only the `SITE_URL` variable, and does not run while `DEPLOY_ENABLED` is not `true`.

- **When it runs:** on Mondays and Thursdays at 07:23 UTC, and by hand.
- **What it does:** it requests with `Cache-Control: no-cache` and a `keepalive=<time>` parameter that changes each time. Each request waits up to 130 s (a Render cold start takes about a minute and Vercel gives the rewrite 120 s); if it fails, it retries every 20 s for about 5 minutes. It succeeds with HTTP 200 and a body that is JSON. It never writes the body to the log. If it cannot, the job fails with a message telling you to look at Render and the Supabase dashboard.
- **Run it by hand:** Actions → Keepalive → *Run workflow*, or `gh workflow run keepalive.yml`.
- **Turn it off:** change `DEPLOY_ENABLED` to `false` (this also turns off the deploy), or disable only this workflow in Actions → Keepalive → "⋯" → *Disable workflow* (`gh workflow disable keepalive.yml`).
- **It only runs on the default branch** (`main`): GitHub's documentation says scheduled workflows only run there, on the latest commit. The workflow could not be tried before it reached `main`; the first run can be requested by hand with `workflow_dispatch`.

**What the documentation says** (read on 2026-11-09, with no update date on the pages):

- **Supabase** ([free-project-pausing](https://supabase.com/docs/guides/platform/free-project-pausing)): "Supabase pauses Free Plan projects that show low activity over a 7-day period" and a project is considered inactive if it does not have "sufficient user database activity over the past week"; "Typically a few user requests to the database each day over the previous week is enough". The "low activity" threshold is not given in numbers. They warn by email about a week before pausing. A paused project can be restored up to 1 year later, in the dashboard (*Resume project*). **It is not clear** whether a visit to the dashboard counts as database activity (the page suggests it as a way to generate activity), nor whether one query twice a week is enough: the text speaks of a few requests per day. That is why the frequency is **provisional**.
- **GitHub** ([disabling and enabling a workflow](https://docs.github.com/en/actions/managing-workflow-runs-and-deployments/managing-workflow-runs/disabling-and-enabling-a-workflow)): "In a public repository, scheduled workflows are automatically disabled when no repository activity has occurred in 60 days." The documentation **does not define** "repository activity", and the pages read do not say whether the owner is notified. To re-enable: Actions → the workflow → *Enable workflow*, or `gh workflow enable keepalive.yml`; a commit with write permissions that changes the `cron` also re-enables it. The scheduled run may be delayed at times of load (hence 07:23 and not 07:00) and, under heavy load, some jobs may be dropped.

## 11. Shutting everything down and avoiding any charge

- [ ] **GitHub:** change `DEPLOY_ENABLED` to `false` and delete the secrets and variables from section 6.
- [ ] **Render:** delete the web service.
- [ ] **Vercel:** delete the project and revoke the token.
- [ ] **Supabase:** delete the project.
- [ ] **Brevo:** delete the SMTP key.
- [ ] Confirm that none of the providers has a payment method attached.
