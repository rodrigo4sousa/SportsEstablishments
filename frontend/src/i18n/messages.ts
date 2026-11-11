import type { AppLocale } from "./locales";

/** Both catalogs live in `frontend/messages/`; a new string always goes into both (AGENTS.md, `messages.test.ts`). */
export async function loadMessages(locale: AppLocale) {
  return locale === "en"
    ? (await import("../../messages/en.json")).default
    : (await import("../../messages/pt-PT.json")).default;
}
