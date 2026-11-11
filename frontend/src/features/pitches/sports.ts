import { useCallback } from "react";
import { useTranslations } from "next-intl";

/** The fixed list of sports (US07): the API's stable codes. Their names are in the catalogs (`sports.*`). */
export const SPORTS = [{ code: "futsal" }, { code: "football7" }, { code: "padel" }, { code: "tennis" }] as const;

export type SportCode = (typeof SPORTS)[number]["code"];

export function isSportCode(value: string): value is SportCode {
  return SPORTS.some((sport) => sport.code === value);
}

/** The name of a sport code in the page's language; an unknown code (a newer API) is shown as it is. */
export function useSportLabel() {
  const t = useTranslations("sports");
  return useCallback((code: string) => (isSportCode(code) ? t(code) : code), [t]);
}
