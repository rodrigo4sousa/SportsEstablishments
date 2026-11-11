# 0012 · Portuguese and English, with no language in the URL

- **Status:** Accepted on 2026-11-11 (T09), with the choices the brief left open marked as provisional in the [backlog](../../backlog.md).
- **Date:** 2026-11-11.
- **Elements:** WEB-I18N, WEB-UX, WEB-API-CLIENT, WEB-DATETIME, WEB-F-ACCOUNT, WEB-PROXY, API-NOTIF, API-NOTIF-COMPOSE, API-AUTH-PRIVACY, DB-USERS, DEV-E2E.

## Context

The site was written in Portuguese only (pt-PT). T09 adds English, decided by the project owner before starting:

- **The URLs do not change.** No `/en/...` prefix: the 404 logic of `proxy.ts`, the E2E tests, the CI script that checks the 404s and every link already shared keep working unchanged.
- **Which language a request gets:** the `NEXT_LOCALE` cookie, written only when the person picks a language, then the browser's `Accept-Language` (any `en…` is English, any `pt…` is Portuguese), then pt-PT.
- **Formats:** `Intl` in pt-PT or en-GB; the times of the clubs stay in Lisbon whatever the language, and the pages say so.
- **Emails** in the recipient's language, chosen at sign-up from the site's language and changed on the account page; existing accounts read as pt-PT.

What the repository imposes: Next.js 16 with `cacheComponents` on (static shells, streaming), Server Components by default, the dependency rule (exact versions, at least two weeks old), and API errors shown by their stable `code`.

Alternatives for the library and the routing:

1. **next-intl with i18n routing and `localePrefix: "never"`**: the middleware rewrites every page to an internal `/[locale]/...` route, which keeps a static shell per language. It needs every route under `app/[locale]/`, a middleware on every page (today `proxy.ts` only matches a few paths and owns the 404 rewrite) and its own cookie logic, which writes the cookie on detection unless told not to.
2. **next-intl without i18n routing**: the request config (`src/i18n/request.ts`) reads the cookie and the header on each render. No route moves and `proxy.ts` does not change. Server Components use `useTranslations`/`getTranslations`, Client Components get the messages from `NextIntlClientProvider` in the root layout.
3. **Home-made dictionaries** (the Next.js guide's `getDictionary`): no dependency, but no ICU plurals, no rich text and no typed keys.

## Decision

Option 2, **next-intl 4.14.9** (the newest release published at least two weeks before the work, installed with `--before` so its transitive packages are as old; it declares Next 12 to 16 and React 16.8 to 19 as peers, and builds and runs with this Next.js 16.4 and React 19.3).

- **Locale resolution** is a pure function (`src/i18n/locales.ts`, `resolveLocale`) used by `src/i18n/request.ts` and by `global-error.tsx` (which has no provider). The cookie is `NEXT_LOCALE`: `Path=/`, one year, `SameSite=Lax`, `Secure` on HTTPS, not httpOnly because the switcher writes it. It is a functional preference written only on an explicit choice, so there is still no cookie banner; the Privacy Policy lists it.
- **Every page is request-bound.** The root layout reads the language for `<html lang>` and the metadata, and a static shell would be in one language only, so it exports `instant = false` (the route may block on the request). Pages still stream below their `loading.tsx` and Suspense boundaries. The language switcher (`PT | EN`, header and footer) writes the cookie and reloads the page, so the server renders everything again in that language with the same address.
- **Catalogs:** `frontend/messages/pt-PT.json` and `en.json`, the same keys, ICU plurals and rich-text tags, typed keys (`src/i18n/next-intl.d.ts`, a missing key fails `npm run typecheck`). Zod schemas are factories that take a translator; display formats come from `useFormats` (`src/i18n/formats.ts`). `src/i18n/messages.test.ts` checks the two catalogs have the same keys, no empty value, the same placeholders and tags, and that every message formats.
- **API errors:** the message is chosen by `code` (`apiErrors.*`); a code the catalogs do not have gets the general message. Field errors (`errors` of a 400) are free pt-PT text from FluentValidation, so the page shows the catalog's words for the refused field instead (`useApiFieldMessage`), never the server's text.
- **Emails:** `users.preferred_language` (`pt-PT`, `en` or null, with a check constraint; migration `AddPreferredLanguage`, compatible with the previous API). Registration sends the site's language, `PUT /api/me/preferred-language` changes it, the export includes it and the deletion clears it. Each email keeps both texts side by side and is written at send time in the recipient's current language.
- **Lint:** `react/jsx-no-literals` and a `no-restricted-syntax` selector on `placeholder`, `aria-label`, `title` and `alt` stop new hard-coded text in `.tsx` files.
- **E2E:** the Playwright browser asks for pt-PT, so the four flows read the site as before; a fifth flow switches to English, reloads and books (an addition to [ADR 0010](0010-playwright-end-to-end-tests.md)'s rules on purpose: it is the only check that the cookie, the server render and the API work together in English).

## Consequences

- No page has a prerendered shell any more: each page load renders on the server (on Vercel, a function call per page). The build lists every route as dynamic. Accepted: the pages were already reading cookies for the session, and the site is small.
- The messages of a page are sent to the browser with it (one language at a time). `global-error.tsx` bundles both catalogs, because it has nothing else to read them from.
- Changing the language reloads the page: what was typed in a form and not sent is lost.
- Two languages to keep in step: every new string goes into both catalogs (AGENTS.md), and the catalog test and the lint rule catch the usual slips.
- Data written by people (club names, block and cancellation reasons, the demo seed's ten reasons) is shown as written, in whatever language it was typed.
- The English legal pages are a courtesy translation; the Portuguese text prevails (`legal.languageNote`). The legal version went to 1.4; nobody has to accept again.
