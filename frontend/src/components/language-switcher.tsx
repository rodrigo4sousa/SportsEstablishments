"use client";

import { useLocale, useTranslations } from "next-intl";
import { useState } from "react";
import { localeCookie, type AppLocale } from "@/i18n/locales";

const OPTIONS = [
  { locale: "pt-PT", short: "ptShort", name: "ptName" },
  { locale: "en", short: "enShort", name: "enName" },
] as const satisfies ReadonlyArray<{ locale: AppLocale; short: string; name: string }>;

/** Keeps the choice in the `NEXT_LOCALE` cookie (`Secure` on HTTPS) and loads the page again in that language. */
function switchTo(locale: AppLocale) {
  document.cookie = localeCookie(locale, window.location.protocol === "https:");
  window.location.reload();
}

/**
 * T09: «PT | EN», in the header and the footer. Picking a language is the only thing that writes the `NEXT_LOCALE` cookie (a functional
 * preference, one year; the Privacy Policy lists it), then the page is loaded again, with the same address, so everything the server
 * rendered (the text, `<html lang>`, the title) comes back in that language. Two buttons in a labelled group: each is named by its
 * visible letters plus the language's own name («PT – Português»), and the language in use has `aria-current`.
 */
export function LanguageSwitcher({ className = "" }: { className?: string }) {
  const t = useTranslations("languageSwitcher");
  const current = useLocale();
  const [pending, setPending] = useState(false);

  function choose(locale: AppLocale) {
    if (locale === current) return;
    setPending(true);
    switchTo(locale);
  }

  return (
    <div role="group" aria-label={t("label")} aria-busy={pending || undefined} className={`flex items-center text-sm ${className}`}>
      {OPTIONS.map((option, index) => {
        const active = option.locale === current;
        return (
          <span key={option.locale} className="flex items-center">
            {index > 0 ? (
              <span aria-hidden="true" className="px-1 text-muted-foreground">
                |
              </span>
            ) : null}
            <button
              type="button"
              lang={option.locale}
              aria-current={active ? "true" : undefined}
              onClick={() => choose(option.locale)}
              className={`inline-flex min-h-11 min-w-11 items-center justify-center rounded-md px-1 sm:min-h-8 sm:min-w-8 ${
                active ? "font-semibold underline underline-offset-4" : "text-muted-foreground hover:text-foreground hover:underline underline-offset-4"
              }`}
            >
              {t(option.short)}{" "}
              <span className="sr-only">– {t(option.name)}</span>
            </button>
          </span>
        );
      })}
    </div>
  );
}
