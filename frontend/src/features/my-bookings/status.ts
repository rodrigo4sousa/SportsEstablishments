import { useCallback } from "react";
import { useTranslations } from "next-intl";

const STATUS_CODES = ["confirmed", "cancelled_by_player", "cancelled_by_club", "no_show"] as const;

type StatusCode = (typeof STATUS_CODES)[number];

function isStatusCode(code: string): code is StatusCode {
  return (STATUS_CODES as readonly string[]).includes(code);
}

/**
 * The label of each booking status code (backlog: Confirmada, Cancelada pelo jogador, Cancelada pelo clube, Falta), in the page's
 * language (`bookingStatus.*`). An unknown code (a newer API) is shown as it is.
 */
export function useStatusLabel() {
  const t = useTranslations("bookingStatus");
  return useCallback((code: string) => (isStatusCode(code) ? t(code) : code), [t]);
}
