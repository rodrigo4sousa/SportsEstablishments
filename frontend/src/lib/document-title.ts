"use client";

import { useEffect } from "react";
import { useTranslations } from "next-intl";

/** The title of a page whose name is only known in the browser: «Calendário · Padel Foz Clube · Reservas de Campos». */
export function pageTitle(siteName: string, ...parts: string[]): string {
  return [...parts, siteName].join(" · ");
}

/**
 * The browser tab's title for an owner or admin page that names what it shows (UX-011, UX-012). The server renders a generic
 * title («Calendário») because the club's name comes from the API with the owner's session, which the page learns in the browser;
 * this adds the name once it is known. Nothing happens while `name` is missing, and the effect only runs while the page is shown.
 * `page` is already in the page's language; the suffix is the site's name from the catalog (the same as the root layout's template).
 */
export function useDocumentTitle(page: string, name: string | null | undefined) {
  const siteName = useTranslations("metadata")("siteName");
  useEffect(() => {
    if (name) {
      document.title = pageTitle(siteName, page, name);
    }
  }, [siteName, page, name]);
}
