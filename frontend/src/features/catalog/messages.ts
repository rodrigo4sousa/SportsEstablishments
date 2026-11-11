import { useCallback } from "react";
import { useTranslations } from "next-intl";
import { useApiErrors } from "@/lib/api/error-messages";

/**
 * The message for an API error on the public pages. The shared text for `club_not_found` talks to an owner ("não é
 * teu"); a visitor is told that the club is no longer available, which also does not say why (it may have been hidden).
 */
export function usePublicErrorMessage() {
  const t = useTranslations("catalog");
  const apiErrors = useApiErrors();
  return useCallback((code: string) => (code === "club_not_found" ? t("publicClubNotFound") : apiErrors.message(code)), [t, apiErrors]);
}
