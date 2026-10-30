"use client";

import { useEffect } from "react";

/** The site's title suffix, the same as the root layout's `title.template`. */
export const SITE_TITLE = "Reservas de Campos";

/** The title of a page whose name is only known in the browser: «Calendário · Padel Foz Clube · Reservas de Campos». */
export function pageTitle(...parts: string[]): string {
  return [...parts, SITE_TITLE].join(" · ");
}

/**
 * The browser tab's title for an owner or admin page that names what it shows (UX-011, UX-012). The server renders a generic
 * title («Calendário») because the club's name comes from the API with the owner's session, which the page learns in the browser;
 * this adds the name once it is known. Nothing happens while `name` is missing, and the effect only runs while the page is shown.
 */
export function useDocumentTitle(page: string, name: string | null | undefined) {
  useEffect(() => {
    if (name) {
      document.title = pageTitle(page, name);
    }
  }, [page, name]);
}
