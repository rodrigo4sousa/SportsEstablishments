import { cookies, headers } from "next/headers";
import { getRequestConfig } from "next-intl/server";
import { LISBON_TIME_ZONE } from "@/lib/datetime";
import { LOCALE_COOKIE, resolveLocale } from "./locales";
import { loadMessages } from "./messages";

/**
 * next-intl without i18n routing (ADR 0012): the URL never carries the language, so it is read from the request on every render. That
 * makes every page request-bound (no prerendered shell in one language), which is the decided trade-off.
 */
export default getRequestConfig(async () => {
  const locale = resolveLocale({
    cookie: (await cookies()).get(LOCALE_COOKIE)?.value,
    acceptLanguage: (await headers()).get("accept-language"),
  });

  return {
    locale,
    messages: await loadMessages(locale),
    // Venue times are always Lisbon's (T04), whatever the language.
    timeZone: LISBON_TIME_ZONE,
  };
});
