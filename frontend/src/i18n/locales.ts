/**
 * The site's languages (T09, ADR 0012). There is no locale in the URL: the language of a request comes from the `NEXT_LOCALE` cookie,
 * which only the language switcher writes, then from the browser's `Accept-Language`, then the default. Pure functions, so the rules
 * are tested without a request.
 */
export const LOCALES = ["pt-PT", "en"] as const;

export type AppLocale = (typeof LOCALES)[number];

export const DEFAULT_LOCALE: AppLocale = "pt-PT";

/** Set only when the person picks a language in the switcher; a functional preference cookie (the Privacy Policy lists it). */
export const LOCALE_COOKIE = "NEXT_LOCALE";

/** One year, in seconds. */
export const LOCALE_COOKIE_MAX_AGE = 60 * 60 * 24 * 365;

export function isAppLocale(value: unknown): value is AppLocale {
  return typeof value === "string" && (LOCALES as readonly string[]).includes(value);
}

/**
 * The `Intl` locale that formats dates, numbers and prices for a site language: Portuguese as in Portugal, and British English
 * (day before month, 24-hour clock, like the rest of Europe).
 */
export function intlLocale(locale: AppLocale): "pt-PT" | "en-GB" {
  return locale === "en" ? "en-GB" : "pt-PT";
}

/** The `lang` attribute of a page in this language. */
export function htmlLang(locale: AppLocale): string {
  return locale;
}

/**
 * The first language of an `Accept-Language` header that the site has, in the order of the browser's weights: any `en…` is English,
 * any `pt…` (Brazil included) is Portuguese. Null when nothing matches (or there is no header).
 */
export function localeFromAcceptLanguage(header: string | null | undefined): AppLocale | null {
  if (!header) {
    return null;
  }

  const ranges = header
    .split(",")
    .map((part, index) => {
      const [tag, ...params] = part.trim().split(";");
      const q = params.map((param) => /^\s*q\s*=\s*([0-9.]+)\s*$/i.exec(param)).find(Boolean);
      const weight = q ? Number(q[1]) : 1;
      return { tag: tag.trim().toLowerCase(), weight: Number.isFinite(weight) ? weight : 0, index };
    })
    .filter(({ tag, weight }) => tag.length > 0 && weight > 0)
    // Highest weight first; the header's own order breaks ties.
    .sort((a, b) => b.weight - a.weight || a.index - b.index);

  for (const { tag } of ranges) {
    const language = tag.split("-")[0];
    if (language === "en") return "en";
    if (language === "pt") return "pt-PT";
  }
  return null;
}

/** Cookie (a choice the person made) > `Accept-Language` > pt-PT. A cookie with any other value is ignored. */
export function resolveLocale({ cookie, acceptLanguage }: { cookie?: string | null; acceptLanguage?: string | null }): AppLocale {
  if (isAppLocale(cookie)) {
    return cookie;
  }
  return localeFromAcceptLanguage(acceptLanguage) ?? DEFAULT_LOCALE;
}

/** The `Set-Cookie`-style string the switcher writes with `document.cookie`. `Secure` on an HTTPS page. */
export function localeCookie(locale: AppLocale, secure: boolean): string {
  return `${LOCALE_COOKIE}=${locale}; Path=/; Max-Age=${LOCALE_COOKIE_MAX_AGE}; SameSite=Lax${secure ? "; Secure" : ""}`;
}
