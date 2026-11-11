# Demo GIF

Generates the main README's [`docs/images/demo.gif`](../images/demo.gif): the application running, recorded **locally**, against the `docker compose` stack with the `seed-demo` data (never against production). It is not part of CI and no workflow runs it. Whoever changes the interface can record again with the steps below. The recording is in English (the site also has Portuguese, pt-PT; see [ADR 0012](../architecture/adr/0012-languages-without-locale-in-the-url.md)), so on-screen texts are quoted as they appear in `frontend/messages/en.json`.

## What it shows (about 45 s)

1. The language switcher in the header (`PT | EN`, English chosen), then club search by sport (Padel), location (Porto) and date (tomorrow), and opens Padel Foz Clube.
2. Picks the first free time tomorrow (read from the page, not fixed in the script), follows `Sign in to book`, signs in as `jogador01@example.test`, returns to the same time and confirms.
3. `My bookings` with the new booking.
4. Signs out, signs in as `dono01@example.test` (owner of Padel Foz Clube), opens the calendar on the day of that booking and opens its details.
5. Signs out, signs in as admin and shows the dashboard with the charts.

Only demo accounts are used: the password `demo-password-1` of the `jogador*` and `dono*` accounts is public (see the [README](../../README.md#run-locally)). The admin's is a throwaway value, chosen for this local recording.

## Prerequisites

- Docker with Compose v2, Node.js and `ffmpeg` with the `palettegen` and `paletteuse` filters (`ffmpeg -filters`). `ffmpeg` is not installed from here.
- Ports 3000 and 5080 free. The database and Mailpit are not published on the host (`compose.override.yml`), so they do not clash with a development stack that uses 5432.

## Recording

The recording creates a booking, so **the database has to be new before each recording** (the "first free time tomorrow" changes and the demo account may already have bookings). From the repository root, in bash:

```bash
# 1. Own stack (separate project and volume: it does not touch the development database)
export ADMIN_SEED_EMAIL=admin@example.test
export ADMIN_SEED_PASSWORD=<throwaway value, 8 or more characters>
DC="docker compose -p sportsbooking-demo -f docker-compose.yml -f docs/demo/compose.override.yml"
$DC down -v
$DC up -d --build --wait
$DC run --rm api seed-demo

# 2. Admin password, as in the README ("Changing the admin password")
read -rs ADMIN_NEW_PASSWORD && export ADMIN_NEW_PASSWORD
$DC run --rm -e ADMIN_NEW_PASSWORD api set-admin-password

# 3. Record (first time: install the dependencies and Chromium)
cd docs/demo
npm ci && npx playwright install chromium
ADMIN_PASSWORD="$ADMIN_NEW_PASSWORD" npm run record   # writes out/demo.webm
npm run encode                                         # writes ../images/demo.gif

# 4. Clean up: removes only this project and its volume
cd ../..
$DC down -v
unset ADMIN_NEW_PASSWORD ADMIN_SEED_PASSWORD
```

- **Outputs.** The video and the temporary files stay in `docs/demo/out/` (ignored by Git); the result is `docs/images/demo.gif`.
- **Size.** The goal is under 4 MB (the encoder uses a 128-colour palette). If it goes over, `npm run encode -- 800 10` (width and frames per second) encodes again without redoing the recording.
- **What `record-demo.mjs` does.** First it warms up all the routes in a throwaway context (without recording), then it records in a new context: 1280×720, `en-GB`, the `NEXT_LOCALE=en` cookie set before the first page load, `Europe/Lisbon`, no extensions, with a cursor and click animation drawn by the page itself. It refuses to record against an address that is not `localhost`. `DEMO_URL` changes the port, not the host.
- **Visible window.** Chromium runs with a window (not headless), because the date field in headless mode shows dates as month/day. A window follows the browser language, and in Chromium every English language (checked: en-GB, en-AU, en-IE, en-NZ) also gives month/day; only `--lang=pt-PT` gives dd/mm/yyyy. So the browser is launched with `--lang=pt-PT` (its own shell, which never appears in the video) while the site is English through the cookie and the `en-GB` context locale. Do not touch the mouse during the recording.
- **After recording.** Look at frames of the GIF at several moments (`ffmpeg -i ../images/demo.gif -vf fps=1/3 out/f%02d.png`): readable text, no half-loaded states, no spinner at the end and only demo data.

## Data

The dates are relative to the day of the recording (the seed covers the previous 60 days and the next 14). The day shown in the GIF is tomorrow on that day.
