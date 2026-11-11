"use client";

import { useEffect, useState } from "react";
import en from "../../messages/en.json";
import pt from "../../messages/pt-PT.json";
import { DEFAULT_LOCALE, LOCALE_COOKIE, resolveLocale, type AppLocale } from "@/i18n/locales";
import "./globals.css";

const MESSAGES = { "pt-PT": pt, en } as const;

/** The language of the browser that sees this page: the switcher's cookie, then the browser's languages, then pt-PT (T09). */
function browserLocale(): AppLocale {
  const cookie = document.cookie
    .split(";")
    .map((part) => part.trim().split("="))
    .find(([name]) => name === LOCALE_COOKIE)?.[1];
  return resolveLocale({ cookie, acceptLanguage: navigator.languages.join(",") });
}

/**
 * The last resort: an error in the root layout itself. It replaces the whole document, so it brings its own `<html>` and
 * `<body>`, and uses nothing from the layout (no providers, no header), only the stylesheet. Without the layout there is no
 * request language, so the page starts in pt-PT and switches once mounted.
 */
export default function GlobalError({ error, retry }: { error: unknown; retry: () => void }) {
  const [locale, setLocale] = useState<AppLocale>(DEFAULT_LOCALE);

  useEffect(() => {
    console.error(error);
  }, [error]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- the browser's language is only known after mounting.
    setLocale(browserLocale());
  }, []);

  const t = MESSAGES[locale].globalError;
  const footer = MESSAGES[locale].footer;

  return (
    <html lang={locale}>
      <body>
        <title>{t.documentTitle}</title>
        <main className="mx-auto flex min-h-screen w-full max-w-3xl flex-col items-start justify-center gap-4 px-4 py-12">
          <h1 className="text-2xl font-semibold tracking-tight">{t.title}</h1>
          <p className="text-muted-foreground">{t.body}</p>
          <div className="flex flex-wrap items-center gap-3">
            <button
              type="button"
              onClick={() => retry()}
              className="inline-flex h-9 items-center rounded-md bg-primary px-4 text-sm font-medium text-primary-foreground"
            >
              {t.retry}
            </button>
            {/* A plain link on purpose: the root layout failed, so there may be no router, and a full page load starts clean. */}
            {/* eslint-disable-next-line @next/next/no-html-link-for-pages */}
            <a href="/" className="inline-flex h-9 items-center rounded-md border px-4 text-sm font-medium">
              {t.home}
            </a>
          </div>
        </main>
        <footer className="border-t">
          <nav aria-label={footer.legalLabel} className="mx-auto flex w-full max-w-3xl flex-wrap gap-x-6 gap-y-1 px-4 py-3 text-sm">
            <a href="/terms" className="inline-block py-1 underline underline-offset-4">
              {footer.terms}
            </a>
            <a href="/privacy" className="inline-block py-1 underline underline-offset-4">
              {footer.privacy}
            </a>
          </nav>
        </footer>
      </body>
    </html>
  );
}
