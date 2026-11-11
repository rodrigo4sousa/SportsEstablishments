# UX and accessibility audit

Oct 30, 2026 · before T06

Audit of the running application (complete stack with the demo data), to decide what to improve before the deploy. It changes no code: each problem has a proposed fix and an estimate, and section 5 proposes the order of work. The interface is in Portuguese (pt-PT), so the on-screen texts are quoted in Portuguese, followed by their English meaning in parentheses where it is not obvious.

## 1. Method and coverage

**Environment.** `docker compose` in a separate project (`-p uxaudit`), production build of the front end, `seed-demo` (5 owners, 60 players, 10 clubs, 43 pitches, about 10,400 bookings) and, added through the API, a price band on Futsal A of Arena Futsal Boavista (weekdays, 18:00–00:00, €46.00), because the seed does not create bands. Only demo accounts (`example.test`) and a demo admin. Everything removed at the end.

**Tools.** Playwright (Chromium) and axe-core 4.14, both outside the repository, with the rules `wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa`, `wcag22aa` and `best-practice`. On each screen: full-page capture, axe, title, headings, landmarks, horizontal scroll, small targets, layout shift (CLS) and the element with focus after each action. The captures were left outside the repository.

**Widths.** Desktop 1440×900 and mobile 390×844 (touch emulation), locale `pt-PT`, time zone `Europe/Lisbon`.

**Covered.**

- Visitor: home and search, filters, results (with and without results), club page, slots, pitch with price bands, invitation to sign in, registration (empty and with errors), owner registration, login, a real 429 on login, password recovery, recovery link without a token, club 404 and global 404.
- Player: booking summary, weekly repeat (preview and confirmation), simple confirmation, `As minhas reservas` (My bookings) (upcoming and past), cancelling within the window, disabled button outside the window, a real race for the same slot (`slot_taken`), a 500 error and a network failure when cancelling, slow loading, empty state (a new account created through the interface).
- Owner: `/manage` (with clubs and empty, new account), club edit and save, pitches, schedule, blocks (with form errors), price bands, new club, daily and weekly calendar, `Nova reserva` (New booking), details of a past booking, marking a no-show, cancelling with a reason (empty and filled in), general and per-club statistics, club 404.
- Admin: dashboard (totals, two charts, top 10, owners), search and sorting of the owners, an owner's page, deactivation form (empty), owner 404, slow loading.
- Global: headers per profile, loading, empty states, error and 404 pages, toasts (500 on login), focus after actions, page titles.
- Keyboard (desktop): focus order at the start, login with the keyboard only, booking a slot with the keyboard only, the tabs of `As minhas reservas`, opening a booking in the calendar and closing it with Escape. The look of the focus was checked on images of the focused elements.

**Not covered.**

- A real screen reader (NVDA, VoiceOver); only what axe and the DOM show (names, roles, live regions).
- Real phones and Safari/Firefox. In mobile emulation, Playwright's `tap()` on each pitch's buttons (`Horário` (Schedule), `Bloqueios` (Blocks), `Faixas de preço` (Price bands)) did not manage to click, although the test that clicks the centre of the button hits it: treated as an artifact of the emulation, to be confirmed on a phone.
- 200 % zoom, 320 px width, dark mode (`next-themes` is installed), reduced motion.
- Flows that need a real email: setting the new password with a valid token. A series with refused dates (`series_unavailable`), undoing a no-show, deactivating and reactivating an owner to the end, creating and editing a price band with errors.
- Chromium's `type=date` and `type=time` fields without a pt-PT interface show "10/30/2026" and "12:00 AM"; in a Portuguese browser they show dd/mm/yyyy and 24 h. It is not a problem of the application and is not among the findings.

## 2. Findings

Severity: **blocker** prevents the task; **major** makes the task much harder or visibly fails WCAG AA; **minor** gets in the way but has a path; **polish** finishing touches. Effort: **S** up to half a day, **M** one to two days, **L** more.

### UX-001 · Focus indicator almost invisible

- **Screen and profile:** all. **Viewport:** both. **Severity:** major. **Effort:** S–M.
- **What is seen:** with the keyboard, the focus on a field, a list or a header link is a very light grey ring; on the black main button (`Entrar` (Sign in), `Confirmar reserva` (Confirm booking)) no ring is seen at all. The slots and the calendar boxes have a thin grey ring that hardly stands out from the border.
- **Why it matters:** someone using a keyboard does not know where they are. Fails WCAG 2.4.7 and 1.4.11 (component contrast, 3:1). The cause is a single one: `--ring` is `oklch(0.708 0 0)` and the components use `ring-ring/50` (half opacity).
- **Fix:** a dark `--ring` at full opacity (for example `oklch(0.205 0 0)`, the `--primary`) and a common focus style (`focus-visible:outline-2 focus-visible:outline-offset-2`) on `Button`, `Input`, `NativeSelect`, on links and on hand-made buttons (`SlotList`, tabs, `BookingBox`; not `HourCell`, which is outside the Tab order).

### UX-002 · Focus falls to `<body>` after actions

- **Screen and profile:** player (confirming a booking and a series, opening and confirming a cancellation), owner (marking a no-show, cancelling), admin (opening `Desativar dono` (Deactivate owner)), failed login. **Viewport:** both. **Severity:** major. **Effort:** M.
- **What is seen:** after `Confirmar reserva`, the button disappears and the focus goes back to the start of the document; the next Tab key leads to the logo. The same when opening the cancellation confirmation: the `Cancelar reserva` (Cancel booking) button is replaced and the focus is lost, so `Sim, cancelar reserva` (Yes, cancel booking) is dozens of Tabs away. The confirmation (`role="status"`) is announced, but the keyboard user has to find their place again.
- **Why it matters:** WCAG 2.4.3 (focus order). It is the most repeated pattern in the audit.
- **Fix:** a single rule: when a focused element is replaced, the focus goes to what replaced it. The title of the confirmation or notice gets `tabIndex={-1}` and receives the focus. A form that opens focuses its first field, and on closing returns to the button that opened it. A small shared hook (`useFocusOnMount`) solves almost all cases.

### UX-003 · The cancellation notice appears out of view

- **Screen and profile:** `As minhas reservas`, player. **Viewport:** both (worse on mobile). **Severity:** major. **Effort:** S.
- **What is seen:** when cancelling a booking further down the list, the confirmation closes and the card changes state, but the message `Reserva cancelada. O horário voltou a ficar livre.` (Booking cancelled. The time is free again.) is written at the top of the list: measured at 3,700 px above the viewport on desktop and 5,900 px on mobile. The same for refusals and for a 500 error (see UX-019). Whoever does not use a screen reader sees nothing happening.
- **Why it matters:** the player's most sensitive action is left with no visible response; the user may repeat it or think it failed.
- **Fix:** show the notice inside the card, in place of the confirmation (success in green and refusal in red), and give it the focus (UX-002). The notice at the top can stay only for errors that do not belong to a card.

### UX-004 · A booking's details open far from the grid

- **Screen and profile:** calendar, owner. **Viewport:** both. **Severity:** major. **Effort:** M.
- **What is seen:** when clicking a booking, the details (with `Marcar falta` (Mark no-show) and `Cancelar reserva`) appear below the grid: measured at 1,400–1,740 px from the top of the viewport on desktop and 1,370 px on mobile. Nothing moves, the focus stays on the box and Escape does not close. The owner has to know to scroll.
- **Why it matters:** it is the path of all the owner's actions on a booking (US12, US15).
- **Fix:** on desktop, a fixed side panel to the right of the grid; on mobile, a sheet that rises from the bottom. As a minimal alternative: `scrollIntoView` and focus on the title `Reserva` (Booking) when opening, Escape closes and returns the focus to the box.

### UX-005 · The calendar grid does not use the screen

- **Screen and profile:** daily and weekly calendar, owner. **Viewport:** both. **Severity:** major. **Effort:** M–L.
- **What is seen:** at 1440 px the calendar sits in the 768 px column of all the pages (`max-w-3xl`), with fixed columns of 176 px (`w-44`): three pitches fit, a club with four or five pitches already needs horizontal scroll, while more than 600 px of the screen stay empty. At 390 px you see the hours column and a pitch and a half, with no sign that the grid continues sideways. The text of the boxes is cut off (`Confirmada · Criada pelo cl…` (Confirmed · Created by the cl…)).
- **Why it matters:** it is the screen the owner opens every day.
- **Fix:** the calendar at a width of its own (like the admin, `max-w-6xl`, or the full width), flexible columns with a minimum, hours column and pitch header fixed when scrolling. On mobile, decide (section 6): one pitch at a time with a selector (like the weekly view) or scroll with a shadow and an indication "swipe to see more pitches".

### UX-006 · Statistics on mobile hide columns and cannot be reached by keyboard

- **Screen and profile:** club statistics and general statistics, owner. **Viewport:** mobile. **Severity:** major. **Effort:** M.
- **What is seen:** at 390 px the table shows Pitch, Booked hours, Open hours and Occupation and looks complete; Revenue, Confirmed and No-shows are hidden on the right, in a scroll container with no visual cue. The pitch names break (`Futebol / 7` (Football / 7)). The container (`.overflow-x-auto`) is not focusable, so the keyboard does not scroll it (axe `scrollable-region-focusable`).
- **Why it matters:** revenue, the number the owner looks for most, is invisible.
- **Fix:** on small screens, one card per pitch with the seven numbers in two columns; or keep the table with a fixed first column, scroll shadow and `tabIndex={0}`, `role="region"` and `aria-label` on the container (also valid for the admin's tables).

### UX-007 · The global 404 page is in English and is a dead end

- **Screen and profile:** any unknown address, all. **Viewport:** both. **Severity:** major. **Effort:** S.
- **What is seen:** "404 | This page could not be found." (Next.js's default page), title "404: This page could not be found.", no header, no link to the home page, no `<main>` (axe `landmark-one-main`, `region`).
- **Why it matters:** it breaks the pt-PT interface rule and leaves the visitor without a path. An old shared link lands here.
- **Fix:** `app/not-found.tsx` in pt-PT, with the public header, `Página não encontrada` (Page not found), links to the search and to sign in, and a title of its own.

### UX-008 · Colours that fail AA contrast

- **Screen and profile:** several (list below). **Viewport:** both. **Severity:** major. **Effort:** S.
- **What is seen:** two colour pairs repeat. Error text `#e7000b` on the pinkish alert background `#fde6e7`: 4.0:1 (login with 429, invalid recovery link, taken slot, reason errors). Grey `--muted-foreground` `#737373` on `--muted` `#f5f5f5` or on tinted backgrounds: 4.33–4.34:1 (the `Cancelada` (Cancelled)/`Realizada` (Completed) state in past bookings, `Reservado` (Booked) slots in the preview, the owner's club list row on hover, the help text of the reason fields).
- **Why it matters:** WCAG 1.4.3 (4.5:1 for normal text). It is 48 nodes on 9 screens.
- **Fix:** in the tokens, once: `--muted-foreground` slightly darker (about `oklch(0.50 0 0)`, more than 5:1 on `--muted`) and the text of destructive alerts in a dark red (`text-red-800` or a `--destructive-foreground` token of its own for light backgrounds).

### UX-009 · In the results, only the club name is a link

- **Screen and profile:** search (and the club name in `As minhas reservas`), visitor and player. **Viewport:** both (worse on mobile). **Severity:** major. **Effort:** S.
- **What is seen:** each result is a card with a border that looks clickable, but only the name (about 21 px tall) leads to the club; tapping the rest of the card, or `Há horários livres` (There are free times), does nothing.
- **Why it matters:** it is the step between searching and booking. On mobile the target is under 24 px tall (WCAG 2.5.8).
- **Fix:** the whole card as a link (stretched-link pattern: `a::after` with `inset-0` inside a `relative` card), with a hover and focus state on the card.

### UX-010 · On the club page, booking is at the end

- **Screen and profile:** club page, visitor and player. **Viewport:** both (worse on mobile). **Severity:** major. **Effort:** M.
- **What is seen:** the order is title, `← Pesquisar clubes` (← Search clubs), Contacts, Pitches, and only then Times. On mobile the times start at about 750 px and the summary with `Confirmar reserva` sits below the slot grid: after choosing a slot, nothing indicates that you need to scroll down. The back link appears below the title.
- **Why it matters:** it is the screen where the player decides to book; the main step is the last to appear.
- **Fix:** Times right after the title; contacts in a side column on desktop and at the end on mobile; the back link before the title. On mobile, a fixed bar at the bottom with the chosen slot, the price and the main button, which appears when a slot is chosen.

### UX-011 · Generic page titles

- **Screen and profile:** several. **Viewport:** both. **Severity:** minor. **Effort:** S.
- **What is seen:** any club's page is called `Clube · Reservas de Campos` (Club · Pitch Bookings); `/`, `/manage` and `/admin` use only `Reservas de Campos`; the calendar and the statistics do not say which club they belong to; an owner's page is `Dono · Administração` (Owner · Administration), with another suffix.
- **Why it matters:** tabs, history and screen readers do not tell the pages apart (WCAG 2.4.2).
- **Fix:** `generateMetadata` with the club's or owner's name where it is already read on the server (`loadPublicClub`), titles of their own on the rest (`Pesquisar clubes` (Search clubs), `Os teus clubes` (Your clubs), `Administração` (Administration)) and a single suffix.

### UX-012 · Owner and admin pages without the name of what they show

- **Screen and profile:** calendar, club edit, owner's page (admin). **Viewport:** both. **Severity:** major. **Effort:** S.
- **What is seen:** the `h1` is `Calendário` (Calendar), `Editar clube` (Edit club) or `Dono` (Owner). In the calendar the club's name does not appear anywhere; an owner with two clubs does not know which one they are looking at. On an owner's page, the name is not in the title.
- **Why it matters:** basic orientation, and costly mistakes (cancelling a booking at the wrong club).
- **Fix:** the name in the `h1` ("Arena Futsal Boavista · Calendário", the owner's name), with the page type above or below in small text.

### UX-013 · The owner and admin areas have no navigation

- **Screen and profile:** all the pages of `/manage` and `/admin`. **Viewport:** both. **Severity:** major. **Effort:** M.
- **What is seen:** the header has the area's title, `Olá, …` (Hello, …) and `Sair` (Sign out). To get to the calendar, the owner opens the club's edit page and presses `Calendário de reservas` (Booking calendar) (two steps, and the club list does not offer it). Between clubs or between calendar and statistics, the path is always to go back.
- **Why it matters:** the calendar is the daily task; editing the club is rare.
- **Fix (to decide, section 6):** in the club list, direct actions per club (`Calendário`, `Estatísticas` (Statistics), `Definições` (Settings)); an area navigation menu in the header (`<nav>`), with the current club when there is one.

### UX-014 · No "skip to content" and incomplete landmarks

- **Screen and profile:** all. **Viewport:** both. **Severity:** minor. **Effort:** S.
- **What is seen:** there is no link to skip the header; the headers do not use `<nav>`. The account pages (sign in, registration, recovery) have no header or brand, only `Voltar ao início` (Back to the start).
- **Why it matters:** WCAG 2.4.1; consistency between pages.
- **Fix:** a `Saltar para o conteúdo` (Skip to content) link in the root layout, visible on focus; `<nav aria-label>` around the header links; the public header also on the account pages.

### UX-015 · The tabs of `As minhas reservas` do not follow the pattern

- **Screen and profile:** `As minhas reservas`, player. **Viewport:** both. **Severity:** minor. **Effort:** S.
- **What is seen:** `Próximas` (Upcoming) and `Anteriores` (Past) have `role="tab"`, but the arrows do not change tab and both stay in the Tab order.
- **Why it matters:** a screen reader announces tabs and the user expects the arrows.
- **Fix:** either arrows with a roving `tabIndex`, or, simpler and consistent with the state in the address, normal links with `aria-current="page"`, without the tab roles.

### UX-016 · Calendar boxes with an accessible name different from the visible text

- **Screen and profile:** calendar, owner. **Viewport:** both. **Severity:** minor. **Effort:** S.
- **What is seen:** each booking has an `aria-label` ("Bruno Oliveira, 14:00 – 15:00, Confirmada") that does not contain the visible text as it is, and the visible text is cut off with an ellipsis. axe `label-content-name-mismatch`: 264 nodes on 7 screens.
- **Why it matters:** WCAG 2.5.3: someone using voice control says what they see and the command does not match.
- **Fix:** remove the `aria-label` and let the visible text be the name (with the rest in `sr-only`), or start the `aria-label` with the visible text; show the full text in a `title` or in the details instead of cutting it.

### UX-017 · Jumps in the heading hierarchy

- **Screen and profile:** search, `As minhas reservas`. **Viewport:** both. **Severity:** polish. **Effort:** S.
- **What is seen:** the page's `h1` goes straight to `h3` in the cards (axe `heading-order`, 14 nodes on 8 screens).
- **Fix:** an `h2` (can be `sr-only`, `Resultados` (Results), `Reservas` (Bookings)) before the list, or the cards as `h2`.

### UX-018 · The rate-limit notice does not say how long to wait

- **Screen and profile:** login (and the other forms with a limit), visitor. **Viewport:** both. **Severity:** minor. **Effort:** S.
- **What is seen:** after 10 attempts, `Fizeste demasiados pedidos seguidos. Espera um pouco e tenta novamente.` (You made too many requests in a row. Wait a little and try again.); the button stays active and each click repeats the 429.
- **Why it matters:** the API sends `Retry-After`; the user is left guessing.
- **Fix:** keep `Retry-After` in `ApiError` and show `Tenta de novo dentro de N segundos` (Try again in N seconds), with the button disabled until then. It needs no API change (the same origin reads the header).

### UX-019 · Unexpected errors when cancelling do not reach the toast (different from the documented code)

- **Screen and profile:** cancelling in `As minhas reservas`, player (the same pattern exists in other components). **Viewport:** both. **Severity:** minor. **Effort:** S.
- **What is seen:** a 500 or a network failure when cancelling shows `Ocorreu um erro inesperado. Tenta novamente mais tarde.` (An unexpected error occurred. Try again later.) in the notice at the top of the list (out of view, UX-003), not in a toast. Cause: `client.ts` turns a network failure into `ApiError(0, "network_error")`, and `MyBookings` only uses the toast when the error is **not** an `ApiError`, so that branch never runs. The same `if (!(error instanceof ApiError))` appears in 16 places in 15 components; in the account forms (login, registration, recovery) the toast works because they also use it for unknown codes (checked with a 500 on login); in the calendar and the pitch forms it was not tested with a 500. AGENTS.md says that unexpected mutation errors go to a toast.
- **Why it matters:** the behaviour differs from the documented one, and the error ends up where it cannot be seen.
- **Fix:** treat `status === 0` and `status >= 500` as unexpected (toast) in a shared helper, or fix the documentation if the local notice is the choice. A code error: listed here, not fixed.

### UX-020 · Loading states with text only

- **Screen and profile:** `As minhas reservas`, admin dashboard, routes (`RouteLoading`). **Viewport:** both. **Severity:** polish. **Effort:** M.
- **What is seen:** `A carregar…` (Loading…), `A carregar as reservas…` (Loading the bookings…), `A carregar o gráfico…` (Loading the chart…) on an empty page; the content appears all at once. The measured layout shift is low (CLS ≤ 0.022 on all screens), so it is only perception.
- **Fix:** skeletons with the shape of the cards, tables and charts in the slowest places (booking list, charts, slots).

### UX-021 · The player's header on mobile breaks into two lines

- **Screen and profile:** public pages with a session, player. **Viewport:** mobile. **Severity:** minor. **Effort:** S.
- **What is seen:** `Reservas de / Campos`, `As minhas / reservas` and `Olá, Luís / Costa` wrap, and the header becomes 73 px.
- **Fix:** on small screens, hide the greeting and use a menu (or an account icon) with `As minhas reservas` and `Sair`.

### UX-022 · `As minhas reservas` is long and repetitive

- **Screen and profile:** `As minhas reservas`, player. **Viewport:** both (worse on mobile). **Severity:** minor. **Effort:** M.
- **What is seen:** 20 tall cards per page (more than 6,000 px on mobile), each with the date twice ("sexta-feira, 30 de outubro (30/10/2026)"), Date and Time on separate lines and a `Cancelar reserva` button.
- **Fix:** compact rows grouped by day (`Hoje` (Today), `Amanhã` (Tomorrow), "sábado, 31 de outubro"), time and club on the same line, without the repeated numeric date; the cancellation deadline on a small line.

### UX-023 · Club edit mixes settings and pitches, with actions of the same weight

- **Screen and profile:** club edit, owner. **Viewport:** both. **Severity:** minor. **Effort:** M.
- **What is seen:** a single page with the club form and, below it, the pitches; each pitch has five equal buttons (`Horário`, `Bloqueios`, `Faixas de preço`, `Editar` (Edit), `Desativar` (Deactivate)), and the destructive one is next to `Editar`. On mobile the buttons break onto two lines.
- **Fix:** separate `Dados do clube` (Club details) and `Campos` (Pitches) (sections with anchors or tabs); `Desativar` in a more-actions menu or with a destructive style; main actions with an icon and short text on mobile.

### UX-024 · Toast region with an English name

- **Screen and profile:** all. **Viewport:** both. **Severity:** polish. **Effort:** S.
- **What is seen:** the screen reader announces the region as "Notifications alt+T" (Sonner's default name).
- **Fix:** `containerAriaLabel="Notificações"` (Notifications) on the `Toaster`.

### UX-025 · Unavailable slots hard to read and without a legend

- **Screen and profile:** club page, visitor and player. **Viewport:** both. **Severity:** polish. **Effort:** S.
- **What is seen:** taken slots are very light grey on grey; there is no legend saying what it means (the rule of never saying the reason stays).
- **Fix:** a short legend (`Livre` (Free), `Indisponível` (Unavailable)), slightly darker text and strike-through on the unavailable ones.

### UX-026 · The pitch choice shows the base price when there are bands

- **Screen and profile:** club page, visitor and player. **Viewport:** both. **Severity:** polish. **Effort:** S.
- **What is seen:** "Futsal A · Futsal · €38.00" in the pitch list, while half the slots cost €46.00.
- **Fix:** `desde 38,00 €` (from €38.00) when `hasPriceBands`, or drop the price from the option (each slot already shows its own).

### UX-027 · Admin charts in shades of grey, one series almost invisible

- **Screen and profile:** admin dashboard. **Viewport:** both. **Severity:** minor. **Effort:** S.
- **What is seen:** in the registrations chart, the `Donos` (Owners) series uses `--chart-1` (`#d4d4d4`) on white, and the `Donos` legend almost disappears; players and owners are told apart only by greys.
- **Why it matters:** WCAG 1.4.11 (3:1 for graphical elements). The `sr-only` tables with the same numbers exist and are fine.
- **Fix:** `--chart-*` tokens with enough contrast and colour, and value labels on the small bars.

### UX-028 · Different widths between areas and misaligned header in the admin

- **Screen and profile:** admin (and, by comparison, the other areas). **Viewport:** desktop. **Severity:** polish. **Effort:** S.
- **What is seen:** the header uses `max-w-3xl` (768 px) and the admin content `max-w-6xl` (1152 px): at 1440 px the brand starts about 190 px further right than the page title. Player and owner use 768 px everywhere, even where there are wide tables.
- **Fix:** one width per area (or per page) shared by the header and the `PageShell`.

### UX-029 · Owners table: links that do not look like links and small headers

- **Screen and profile:** admin dashboard. **Viewport:** both. **Severity:** minor. **Effort:** S.
- **What is seen:** the owners' names lead to the owner's page but have no underline or colour; the sortable headers (`Nome ↑` (Name ↑), `Email`) are 36–52 × 20 px on mobile.
- **Fix:** link style on the names (underline on hover and on focus) and headers with a larger touch area (`py-2`, the whole cell as a button).

### UX-030 · Neutral visual identity and a home page without an introduction

- **Screen and profile:** all, especially the home page. **Viewport:** both. **Severity:** minor. **Effort:** M–L.
- **What is seen:** the theme is shadcn's neutral default (black, white and greys), with no brand colour, icons or image; the home page opens with `Encontra onde jogar` (Find where to play) and a list, without saying what the platform is, who it is for, or that an owner can also register. The green of `Há horários livres` and of confirmed bookings is the only colour.
- **Why it matters:** for a portfolio and for a first-time visitor, the application looks like a prototype. It is not an accessibility problem.
- **Fix:** decide a visual direction (section 6): brand colour in the tokens (`--primary`, `--ring`, `--chart-*`), a short introduction on the home page with the search below it, and a footer with `És dono de um clube? Cria a tua conta` (Do you own a club? Create your account).

### Summary table

| Severity | Count | Findings |
| --- | --- | --- |
| blocker | 0 | — |
| major | 12 | UX-001, UX-002, UX-003, UX-004, UX-005, UX-006, UX-007, UX-008, UX-009, UX-010, UX-012, UX-013 |
| minor | 12 | UX-011, UX-014, UX-015, UX-016, UX-018, UX-019, UX-021, UX-022, UX-023, UX-027, UX-029, UX-030 |
| polish | 6 | UX-017, UX-020, UX-024, UX-025, UX-026, UX-028 |
| **Total** | **30** | |

## 3. Cross-cutting patterns

Fixed once, in the shared components and tokens, and they resolve several findings.

1. **Focus management** (UX-002, UX-003, UX-004): no component moves the focus after replacing the focused element. A shared hook and a rule in AGENTS.md ("whoever replaces the element with focus decides where the focus goes") cover the player, the owner and the admin.
2. **Colour tokens** (UX-001, UX-008, UX-027, UX-030): `--ring`, `--muted-foreground`, destructive text on light backgrounds and `--chart-*` come from the default neutral theme and fail contrast. Changing the tokens in `globals.css` fixes focus, contrast and charts at once, and it is also where a brand colour would go in.
3. **Notices far from the action** (UX-003, UX-004, UX-019): notices and details are written in a fixed place on the page (top of the list, bottom of the calendar). The rule: the response appears next to what the user just touched, and receives the focus.
4. **One width for everything** (UX-005, UX-006, UX-028): `AreaHeader`, `PageShell` and the player layout fix 768 px. One width per page (narrow for forms, wide for the calendar and tables) resolves the space lost on desktop and prepares the tables for mobile.
5. **Wide tables and grids on mobile** (UX-005, UX-006, UX-029): scroll containers with no cue or focus. A `ScrollRegion` component (shadow at the ends, `tabIndex={0}`, `role="region"`, `aria-label`) serves the calendar, the statistics and the admin's tables.
6. **Page titles and identity** (UX-011, UX-012, UX-017): generic titles in the tab and in the `h1`. A pattern for `generateMetadata` and for the `PageShell` (entity name + page type).
7. **Targets and links** (UX-009, UX-021, UX-029): links only on the text, 18–21 px tall, in cards and tables that look clickable as a whole.

## 4. Automatic results (axe)

102 captures (54 different screens, each at both widths when applicable); 59 with no violation. No page has whole-page horizontal scroll at 390 px, and the largest CLS measured is 0.022.

| Rule (axe) | Impact | Nodes | Screens | Where | Finding |
| --- | --- | --- | --- | --- | --- |
| `label-content-name-mismatch` | serious | 264 | 7 | Calendar (day, week, new booking, details, no-show, cancel) | UX-016 |
| `color-contrast` | serious | 48 | 9 | Error alerts (429, recovery, taken slot, reason), `Reservado` in the preview, states of past bookings, club list on hover, help text of the reasons | UX-008 |
| `heading-order` | moderate | 14 | 8 | Search, `As minhas reservas` | UX-017 |
| `region` | moderate | 4 | 1 | Global 404 | UX-007 |
| `landmark-one-main` | moderate | 2 | 1 | Global 404 | UX-007 |
| `scrollable-region-focusable` | serious | 1 | 1 | Club statistics (mobile) | UX-006 |

axe does not detect the barely visible focus (UX-001), the lost focus (UX-002) or the notices out of view (UX-003, UX-004): they came from the measurements and the captures.

## 5. Proposed order of work

Four batches, each in a small branch, with manual verification in the browser at the end (the repository's practice) and, in the last one, a repetition of this audit.

### Batch 1 · Foundations

- **Findings:** UX-001, UX-002 (the hook and the rule), UX-007, UX-008, UX-011, UX-014, UX-017, UX-019, UX-020, UX-024, UX-028.
- **Effort:** M (2 to 3 days). The tokens, the 404, the skip link, the titles and the Toaster are S each; the focus hook and the skeletons are the bulk.
- **Verify:** axe without `color-contrast`, `heading-order`, `region` and `landmark-one-main`; visible focus on buttons, fields, lists, links and slots (images of the focused elements, as in this audit); the 404 in pt-PT with a header; distinct titles in each tab; a 500 and a network failure in a mutation show the toast; component tests for the focus hook.

### Batch 2 · Player flow

- **Findings:** UX-003, UX-009, UX-010, UX-015, UX-018, UX-021, UX-022, UX-025, UX-026 and the application of UX-002 to the player's components.
- **Effort:** M–L (3 to 4 days). The reorganization of the club page and the fixed bar on mobile (UX-010) and the compact list (UX-022) are the largest work.
- **Verify:** book and cancel with the keyboard only, without the focus falling on `<body>`; the cancellation notice visible without scrolling, at both widths; the whole card in the results opens the club; at 390 px, the main button visible right after choosing a slot; 429 with a wait time; the component tests of `ClubBooking`, `SlotList` and `MyBookings` updated.

### Batch 3 · Owner area

- **Findings:** UX-004, UX-005, UX-006, UX-012, UX-013, UX-016, UX-023 and the application of UX-002 to the owner's components.
- **Effort:** L (4 to 6 days), mostly the calendar (width, columns, details panel, mobile model) and the navigation.
- **Verify:** a club with five pitches fits at 1440 px without scroll; at 390 px, the model chosen for mobile works with touch and keyboard; the details open in view and Escape closes and returns the focus; axe without `label-content-name-mismatch` and `scrollable-region-focusable`; the club's name in the calendar and the statistics; from `/manage` to a club's calendar in one step; the calendar layout on clock-change days (25 and 23 h) is still right (`layout.test.ts`).

### Batch 4 · Admin and final accessibility pass

- **Findings:** UX-027, UX-029, UX-030 (if the visual direction is decided) and the repetition of the whole audit.
- **Effort:** M (2 to 3 days) without UX-030; with the visual direction, 2 to 4 days more.
- **Verify:** charts with 3:1 contrast between series and background; recognizable owner links; the audit repeated with the same scripts (axe on all screens, keyboard, mobile) without serious violations; if possible, a pass with NVDA or VoiceOver on the main flows and on a real phone (the `tap()` point in section 1).

## 6. Decisions requested

1. **Visual direction (UX-030).** Stay with the neutral theme, with only the tokens fixed (batch 1), or define a brand colour, a tone and a home page with an introduction? It changes the tokens, batch 4 and the look of all the README's captures.
2. **Owner and admin area navigation (UX-013).** Actions per club in the `/manage` list, a header menu, or both? Open on today's calendar when the owner has a single club?
3. **Calendar on mobile and width (UX-005).** One pitch at a time with a selector (like the weekly view) or the grid with scroll and a cue? And on desktop: a width of its own only for the calendar or for the whole owner area?
4. **Club page (UX-010).** Accept the new order (times first, contacts alongside or at the end) and the fixed bar on mobile.
5. **Toast or notice in place (UX-019, UX-003).** AGENTS.md says that unexpected mutation errors go to a toast; the code shows them in a notice. Proposal: expected response next to the action, unexpected in the toast; if the notice is preferred, the documentation is fixed.
6. **API changes.** No proposed fix needs an API change: `Retry-After` (UX-018) is read on the same origin and the names of the clubs and owners (UX-011, UX-012) already come in the responses the pages read.

## 7. Count and what is already good

**Final count:** 30 findings: 0 blocker, 12 major (UX-001 to UX-010, UX-012, UX-013), 12 minor (UX-011, UX-014, UX-015, UX-016, UX-018, UX-019, UX-021, UX-022, UX-023, UX-027, UX-029, UX-030), 6 polish (UX-017, UX-020, UX-024, UX-025, UX-026, UX-028).

**What is already good** (do not touch without a reason):

- Consistent, direct and friendly pt-PT text on all screens (except the global 404 and the toast region); clear and specific error messages by `code` (`Este horário acabou de ser reservado. Escolhe outro.` (This time was just booked. Choose another.)).
- Correct and consistent formatting of money, dates and times (`46,00 €`, "sexta-feira, 30 de outubro", 24 h in Lisbon), with each slot's price and the cancellation deadline spelled out (`Podes cancelar até 30/10/2026 08:00` (You can cancel until 30/10/2026 08:00), `Já não é possível cancelar: o prazo terminou a …` (It is no longer possible to cancel: the deadline ended on …)).
- The race for the same slot is handled as it should be: message in view, choice cleared, slots reloaded.
- Forms with labels, per-field errors, `noValidate` and rules equal to the API's; axe found no unlabelled field.
- Destructive confirmations in place, with text that explains the consequence (`O horário volta a ficar livre para outros jogadores.` (The time becomes free again for other players.)), and empty states with a clear action (`Criar o primeiro clube` (Create the first club)).
- Live regions (`role="status"`/`alert`) on confirmations and notices, `aria-sort` on sortable tables, `sr-only` tables with the charts' numbers, `aria-expanded` on the pitch panels.
- No page with horizontal scroll at 390 px; low layout shift everywhere (CLS ≤ 0.022).
- State in the address works: going back, reloading and entering in the middle of a booking return the same choice.

## 8. Decisions

Taken on 2026-10-30, after section 6, and valid for the following batches.

1. **Visual direction (UX-030).** The neutral theme stays and only what fails contrast is fixed (in the tokens); no brand colour for now. A short introduction is added to the home page, in batch 4 (UX-030). A brand colour can come in later without redoing anything, and it is not what harms the experience: the invisible focus and the feedback off screen weigh more.
2. **Unexpected errors (UX-019).** They go to a toast, as AGENTS.md says: the code is fixed, not the documentation. It is a real error, done in batch 1 (`isExpectedApiError`, `unexpectedErrorMessage`), with a note in the backlog.
3. **Owner and admin navigation (UX-013), for batch 3.** Both: shortcuts per club (`Calendário`, `Estatísticas`) in `/manage`, to get there in one click, and a header menu for navigation between areas.
4. **Calendar (UX-005), for batch 3.** On mobile, one pitch at a time with a selector (the complete grid only confuses at 390 px). On desktop, a wider page only for the calendar, not for the whole owner area, so as not to touch forms that look fine narrow (`PAGE_WIDTH` already has the `wide` width for that).
5. **Club page (UX-010), for batch 2.** The slots move above the contacts. The fixed booking bar on mobile only comes in if it is cheap (effort S or M); otherwise it is left out.
6. **Booking list on mobile (UX-022), after batch 2.** The 215 px height per booking is accepted: with 20 bookings the page dropped to 4,600 px, the player rarely has that many, and densifying touches a component that is already clear, with little gain.
7. **Contacts on the club page on desktop (UX-010), after batch 2.** No side column: the column is 768 px and contacts at the end are the usual place. It stays as it is.
8. **Owner's calendar, after batch 3.** The header stays narrow and the calendar page wide: aligning them would make the page worse. The test on a real phone (the three buttons of each pitch, the virtual keyboard in `Nova reserva` and the `Conta` (Account) menu) is done when the deploy exists. The fixed hours column and the club name in the titles move to batch 4.

## 9. Status of the batches

### Batch 1 · Foundations (done)

Measured on the running app, with the same scripts and the same demo account as in the audit (desktop and mobile, keyboard and axe).

| Finding | Status | Notes |
| --- | --- | --- |
| UX-001 invisible focus | **Done** | A single rule in `globals.css` (2 px ring at 2 px distance, dark `--ring`) and no `outline-none` on controls; tested in the tokens (3:1) and seen on the dark buttons. The button stopped animating the ring (`transition-all`). |
| UX-002 lost focus | **Partial** | Rule and helpers done (`lib/focus.ts`: `useFocusOn`, `useRestoreFocus`; `ResultNotice`). Applied to: booking and series confirmation, booking refusal, asking and keeping the player's cancellation and its result, a booking's details (opening, cancel form, no-show, result), the owner status form (admin), login refusal, error page. **Left for batches 2 and 3:** closing the details (returning the focus to the booking box), saving the club, the pitch forms (schedule, blocks, bands, pitch), `Nova reserva`. |
| UX-003 notice out of view | **Partial** | The result receives the focus and comes into the screen wherever the booking was. Putting it inside the card is left for batch 2. |
| UX-004 details far from the grid | **Partial** | The details receive the focus when opening and are now in view (with the keyboard, from 1,396 px to 771 px from the top of the screen). The side panel and Escape are left for batch 3. |
| UX-007 404 in English | **Done** | `app/not-found.tsx`, `app/error.tsx` and `app/global-error.tsx` in pt-PT, with the site header and the way back. |
| UX-008 contrast | **Done** | `--muted-foreground` and `--destructive` in the tokens; axe: 48 nodes to 0. A test reads `globals.css` and checks the pairs in both themes. |
| UX-011 titles | **Done** | A single suffix (`title.template`), titles of their own on all pages and the club's name on the club page. The club's name in the calendar and the statistics comes with UX-012 (batch 3). |
| UX-014 skip to content | **Done** | `Saltar para o conteúdo` link, one `<main id>` per page, `<nav aria-label="Conta">`, site header on the account pages. The areas' navigation stays with UX-013 (batch 3). |
| UX-017 heading hierarchy | **Done** | `h2` in the search results and in the booking list; axe `heading-order`: 14 nodes to 0. |
| UX-019 unexpected errors | **Done** | 500, network failure and non-API errors go to the toast in all forms and actions (the network failure stays inside the form on the four screens that describe it: login, recovery, new password, booking). Tested in `As minhas reservas`, in the calendar and on login, and seen in the app. |
| UX-020 loading | **Partial** | `Skeleton`, `LoadingRegion` and `EmptyState`, used in `As minhas reservas`, in the routes, in the search, in the times and in the admin's totals, charts and tables. **Left for batch 3:** the calendar, the statistics and the pitch panels (still text only). |
| UX-024 toast region | **Done** | `Notificações`. |
| UX-028 widths | **Done in the admin** | `PAGE_WIDTH`: the admin's header and pages use the same width; owner and player already used the same one. The wider calendar belongs to batch 3. |

**axe results, before and after** (same screens, both widths):

| Rule | Before (nodes) | After (nodes) |
| --- | --- | --- |
| `color-contrast` | 48 | 0 |
| `heading-order` | 14 | 0 |
| `landmark-one-main` | 2 | 0 |
| `region` | 4 | 0 |
| `label-content-name-mismatch` | 264 | 264 (UX-016, batch 3) |
| `scrollable-region-focusable` | 1 | 1 (UX-006, batch 3) |

Captures with no violation: 59 of 102 before, 78 of 93 after (final run; the two series do not have exactly the same captures, so what counts are the per-rule counts). No CSP violation or console error in any of the flows.

**Found during the batch:** the button's `transition-all` made the focus ring appear fading in (fixed); saving the club leaves the focus on `<body>` (stays with UX-002, batch 3). From batch 2 UX-009, UX-010, UX-015, UX-018, UX-021, UX-022, UX-025, UX-026 also remain.

### Batch 2 · Player flow (done)

Measured on the running app, as in batch 1 (desktop and mobile, keyboard and axe, the demo accounts), on the visitor and player screens: search, club, times, booking, weekly repeat, `As minhas reservas`, cancelling, signing in, creating an account and recovering the password.

| Finding | Status | Notes |
| --- | --- | --- |
| UX-003 notice out of view | **Done** | A cancellation refusal appears on the row of the booking it is about, with focus; success appears above the list, in the same region, with focus (the booking has already left the list). |
| UX-009 results | **Done** | The link is the club's name and its `::after` covers the card; the focus ring is drawn around the card; card with `hover`. The paginator buttons are 44 px on mobile. |
| UX-010 club page | **Done** | The order is the back link, the name and address, the times, the pitches and the contacts. One line says how to book. **Fixed booking bar on mobile: done**, effort **M** (half a day to a day, only `position: sticky`, no dependencies, without touching the API): the buttons are a sibling of the summary card, not a child, and stick to the bottom of the screen while the section is in view, with what they book above them; from `sm` up they are the row of buttons below the summary. Measured at 390 px: after choosing a time the bar is in the last 97 px of the screen. The side column of contacts on desktop was **not** done (the column is 768 px). |
| UX-015 tabs | **Done** | Two links in a `<nav>` with `aria-current`; a normal click swaps through the address, a Ctrl or Cmd click opens in a new tab. |
| UX-018 wait time | **Done** | `Retry-After` in `ApiError`; the four forms with a limit count the seconds (a `timer`, not announced every second), disable the button and say when it is possible to try again. Seen in the app: "60 segundos" (60 seconds), button disabled, focus on the message. |
| UX-021 header on mobile | **Done** | The brand on one line and a `Conta` button that opens the same content (greeting, `As minhas reservas` for players only, `Sair`); keyboard: Enter opens, Tab goes through, Escape closes and returns the focus, leaving with Tab closes. The header went from two lines (73 px) to one (69 px, the button is 44 px). The visitor keeps `Entrar` (Sign in) and `Criar conta` (Create account) (they fit). |
| UX-022 booking list | **Done** | One list per day, with the day as the title (`Hoje · …` (Today · …), `Amanhã · …` (Tomorrow · …), the year only if it is not this one) and a compact row per booking. With 20 bookings, the page went from about 6,900 px to about 4,600 px on mobile, and from about 4,800 px to 3,500 px on desktop. On mobile each row still takes about 215 px because the state, the button and the deadline stay on lines of their own; a denser version is left to decide. |
| UX-025 unavailable times | **Done** | Struck through, without the transparency, and a legend (`Livre`, `Indisponível (riscado)` (Unavailable (struck through))), which does not say why. |
| UX-026 price on the pitch choice | **Done** | A pitch with bands no longer shows a price in the choice (each time shows its own). |
| UX-002 (player screens) | **Done** | Booking, a booking refusal, the series, asking and keeping the cancellation, the result, the account pages and the login refusal (batch 1) leave the focus in the right place; no player flow drops the focus on `<body>`. |
| UX-011 / UX-020 (player screens) | **Done** | All the player's titles have a name of their own (the club carries its name). The times skeleton has the shape of the list (the two lines above and about a day of times, in the same grid), because with the times first the page below jumped (shift 0.14). |

Also done: an introduction on the home page (two sentences, and the link to create an owner account), the confirmation saying what happens next (the email and where to cancel), main action buttons at 44 px on mobile (`Button size="cta"`), and the review of the text: `por horário` (per time) instead of `por slot` where the page already says `horário`, and `tentar novamente` (try again) in all the retry messages.

**axe on the visitor and player screens** (both widths): 31 of 52 captures with no violations in the audit, 51 of 51 in batch 1 and 51 of 51 now. The audit's rules, in nodes: `color-contrast` 17 to 0, `heading-order` 14 to 0, `landmark-one-main` 2 to 0, `region` 4 to 0. axe sees almost nothing of what changed in this batch (page order, target size, the menu, the list by day): that was checked with the measurements and captures above and with the tests. No console error or CSP violation; the maximum layout shift is 0.037 (only changes requested by the user).

**Keyboard, end to end on desktop:** Tab to the first time went from 13 to 11 stops; after booking the focus is on the confirmation's title, with the ring visible; the two booking buttons are reached in screen order.

**Left for other batches**
- Batch 3 (owner): `slot` and "per slot" on the owner's screens (UX-023 and the text of `PitchSection`), and everything the report assigns to it.
- Batch 4: UX-027, UX-029, UX-030 (brand colour and the rest of the identity).
- More density on mobile in the booking list (about 215 px per booking); the side column of contacts on desktop; the focus when closing the calendar's details (UX-002, owner).
- The `/manage` and `/admin` tabs still have no menu (UX-013, batch 3).

### Batch 3 · Owner area (done)

Measured on the running app (`docker compose` with the demo data, the club "Padel Foz Clube" with six pitches), desktop 1440 px and mobile 390 px (touch emulation), keyboard and axe, as in the previous batches. The owner screens: list, club edit, pitches, schedule, blocks, price bands, daily and weekly calendar, new booking, details, no-show, cancellation, general and club statistics, and the owner without clubs.

| Finding | Status | Notes |
| --- | --- | --- |
| UX-013 navigation | **Done for the owner** | The same `Conta` menu as the player's (`AccountMenu`, shared) in the owner's header, with `Os meus clubes` (My clubs) and `Estatísticas`; in `/manage` each club has `Calendário`, `Estatísticas` and `Editar`  (from `/manage` to the calendar in one tap; the accessible names start with the visible word and say the club). The admin uses the same menu, without links of its own (it only has one page): it stays as it is. |
| UX-005 calendar | **Done** | A wider page of its own (`PAGE_WIDTH.calendar`, `max-w-7xl`, only on the calendar; the header stays narrow like the rest of the area). Six pitches fit at 1440 px without scroll (before: scroll with three pitches). On mobile, one pitch at a time with the `Campo` (Pitch) selector, kept in the address (`?pitch=`); the weekly view was already one pitch. The fixed hours column and the scroll shadow were not done. |
| UX-004 details | **Done** | On desktop the details sit beside the grid (`lg:sticky`), in view (16 px from the top); on mobile they sit above the grid (393 px from the top, before 1,370 px). Escape and `Fechar` (Close) close and return the focus to the booking box (`data-booking-id`). |
| UX-016 names | **Done** | The booking box no longer has an `aria-label`: the name is the visible text (who, times, state, `Criada pelo clube` (Created by the club)). axe `label-content-name-mismatch`: 264 nodes in 14 captures to 0. |
| UX-006 statistics | **Done** | On mobile each pitch (or club) is a card with the six numbers and the name of each column beside it, revenue included (`StatisticsTable`, with explicit table roles); from `sm` up it is the table. The container is a region with a name and `tabIndex=0` (`ScrollRegion`, also on the calendar grids). axe `scrollable-region-focusable`: 1 to 0. |
| UX-012 club name | **Done** | The club's name appears on the edit page and on the calendar (`ClubName`, the same query as the club page) and was already on the statistics. The tab titles do not carry the name (the calendar API does not return it and the title comes from the server): it stays as it is. |
| UX-023 club edit | **Partial** | `Dados do clube` and `Campos` are sections with a title; the five buttons of each pitch wrap onto lines and are 44 px on mobile; `Desativar` has the destructive style. The more-actions menu and the anchors were not done. |
| UX-002 (owner screens) | **Done** | The pitch forms open with the focus and return it to `Adicionar campo` (Add pitch) or to the pitch's `Editar`; the save buttons recover the focus after saving (club, pitch, schedule, block, band); cancelling and the no-show already had the focus on the result. |
| UX-011 / UX-020 (owner) | **Done** | Skeletons in the club list, in the edit page, in the pitches, in the calendar and in the statistics; empty states with an action. `slot` became `horário` (time/schedule) in all the text of the owner's screens (price per time, duration of the time, generated times, messages). |

**axe on the owner screens** (both widths): 36 captures, 36 with no violation at all. In the audit, the rules that belonged to the owner were `label-content-name-mismatch` (264 nodes in 14 captures) and `scrollable-region-focusable` (1 node); both are gone. No console error or CSP violation; layout shift 0 on the owner's captures.

**Touch on each pitch's buttons:** in the audit, the `tap()` of `Horário`, `Bloqueios` and `Faixas de preço` failed in the mobile emulation ("intercepts pointer events"). The cause was the row of five buttons, which did not wrap and ran off the screen; now it wraps, the buttons are 44 px and the tap opens each panel (`aria-expanded`). The emulation does not replace a phone: it remains to confirm with fingers, the virtual keyboard in the `Nova reserva` form and the `Conta` menu, on a real phone (batch 4).

**Keyboard:** in the owner's header the order is `Área do dono` (Owner area), `Os meus clubes`, `Estatísticas`, `Sair`; on mobile Enter opens `Conta`, Tab goes to `Os meus clubes` and Escape closes and returns the focus to the button. Opening a booking takes the focus to the title `Reserva`; Escape returns it to the box.

**Left for other batches**
- Batch 4: UX-027, UX-029, UX-030, and the repetition of the audit on the admin.
- UX-023: more-actions menu per pitch and anchors between sections; fixed hours column on the calendar.
- The tab title with the club's name on the owner pages.

### Batch 4 · Admin and final pass (done)

Measured on the running app (`docker compose`, demo data, production build), on the three profiles and the global pages, at 1440 px and 390 px, plus 200 % zoom (720 px of usable width) and reflow at 320 px on the 23 main pages, with keyboard and axe.

| Finding | Status | Notes |
| --- | --- | --- |
| UX-027 admin charts | **Done** | `--chart-1` went from 1.5:1 to 3:1 against the page and the two series are told apart by lightness (≥ 2:1 between them), tested in both themes (`theme-contrast.test.ts`). Each bar says its number (up to 12 periods), the legend has dark text and each chart's data is in a table anyone can open (`Ver os números numa tabela` (See the numbers in a table), a `<details>`), no longer only for screen readers. |
| UX-029 owners table | **Done** | The names are underlined links (also the table of clubs with the highest occupation); the sortable headers have a larger area; on mobile the owners and the clubs with the highest occupation are cards with each number labelled (before, the table hid the occupation on the right) and the owners' order is chosen in an `Ordenar por` (Sort by) list. The totals go to two columns on mobile. |
| Owner page and deactivation | **Done** | Skeletons and empty state; the deactivate button is 44 px on mobile; the result of deactivation and reactivation receives the focus and is in view (measured at 419 px from the top on desktop and 236 px on mobile), with the history updated. |
| UX-005 (deferred from batch 3) | **Done** | The hours column stays fixed (`sticky`) when the pitches slide; the pitch and day columns share the width with a minimum, so the text of the bookings is no longer cut off (`Criada pelo clube` in full). The pitch header does not stay fixed on vertical scroll: the grid grows with the page and has no vertical scroll of its own. |
| UX-012 (deferred from batch 3) | **Done** | The tab title carries the club's name on the calendar, the club statistics and the edit page, and the owner's name on the owner's page (`useDocumentTitle`, after the name is known; the server page keeps the generic title). |
| UX-028 widths | **Done** | The narrow header and the wide calendar page stay as they are (decision 8). |
| UX-030 visual identity | **Partial, by decision** | The neutral theme stays (decision 1). There has been an introduction on the home page since batch 2, with the link to register a club; no brand colour, icons or image. |
| UX-023 club edit | **Partial** | See batch 3: the more-actions menu and the anchors between sections are missing. |

**Final accessibility pass, all profiles.**
- **axe:** 90 captures (51 screens, at both widths), 90 with no violation at all; plus the 23 main pages (visitor, player, owner and admin, including an owner's page) at 1440 px, at 200 % zoom and at 320 px: 69 of 69 with no violations. No page with whole-page horizontal scroll, not even at 320 px; the largest layout shift is 0.008. The first run of the batch found, and the batch fixed, one axe violation (`landmark-unique`: the region of the top-clubs table repeated its section's name) and two reflow points that axe does not see (the public header and the button rows of some forms went past 320 px, WCAG 1.4.10).
- **Before and after, across the whole project:** original audit, 59 of 102 captures with no violations (6 rules: `color-contrast` 48 nodes, `label-content-name-mismatch` 264, `heading-order` 14, `region` 4, `landmark-one-main` 2, `scrollable-region-focusable` 1); now, 90 of 90, no rule.
- **Keyboard:** walked through with Tab the home page, the club page, login, the owner's club list, the club edit page, the calendar, the statistics and the admin dashboard (45 to 60 stops each): no stop without a visible focus ring, none off screen, no trap (the stops on `<body>` are the loop back to the browser bar).
- **Titles and landmarks:** each page has a `<title>` of its own, an `h1` and a `main`.
- **Reduced motion:** with `prefers-reduced-motion: reduce`, the 18 skeletons of a page have no animation (without the preference, they do); there are no other looping animations.
- **Zoom and reflow:** at 200 % zoom and at 320 px nothing is cut off or requires horizontal page scroll (the calendar and the wide tables slide inside a region with a name and focus).
- **Dark theme:** it does not exist. `globals.css` has the `.dark` tokens, but nothing applies the class or `prefers-color-scheme` and there is no switch, so the application is always light. It was not added; it is left for decision.
- **Live regions:** the results the user causes receive the focus (`ResultNotice`) and are not live regions; the messages that appear without changing the focus (field errors, times that disappear) keep the live region.

**Final status of the 30 findings:** 28 done and 2 partial (UX-023, UX-030); none left to do. What was left out: the more-actions menu and the anchors of the club edit page (UX-023), the fixed pitch header on the calendar, brand colour (UX-030) and dark theme.

**What still needs a real device or screen reader**
- A real phone, when the deploy exists: the touch on each pitch's three buttons, the virtual keyboard in the `Nova reserva` form and the `Conta` menu (decision 8). The browser emulation covers size and touch, not the virtual keyboard or the address bar that shrinks.
- NVDA and VoiceOver on the main flows (booking, cancelling, the owner's calendar, deactivating an owner): axe and the measurements do not say how the tables as cards, the charts' `<details>` and the regions with focus are announced.
- Other engines: everything was measured in Chromium; Firefox and Safari are missing (focus ring, `position: sticky` on the calendar columns, `<details>`, `type=date` fields in pt-PT).
- Windows high-contrast mode (`forced-colors`) and system magnification.
