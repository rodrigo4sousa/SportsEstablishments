import { useCallback } from "react";
import { useTranslations } from "next-intl";
import type { Formats } from "@/i18n/formats";
import { ApiError } from "@/lib/api/client";
import { UNKNOWN_ERROR, useApiErrors } from "@/lib/api/error-messages";
import type { Granularity } from "./types";

type Period = { start: string; end: string };

/** The short axis label of a period: the day, the Monday of the week, the month. */
export function axisLabel(formats: Formats, granularity: Granularity, period: Period): string {
  return granularity === "month" ? formats.dateKeyMonth(period.start) : formats.dateKeyShort(period.start);
}

/** The full name of a period, for the tooltip and the table: a day, "de … a …" for a week (`weekRange`), a month. */
export function periodLabel(formats: Formats, weekRange: (from: string, to: string) => string, granularity: Granularity, period: Period): string {
  if (granularity === "month") return formats.dateKeyMonth(period.start);
  if (granularity === "week") return weekRange(formats.dateKey(period.start), formats.dateKey(period.end));
  return formats.dateKey(period.start);
}

/**
 * The words for a refused admin request: a refused range or search (field errors) gets the catalog's own words, never the API's text
 * (T09); anything else is the message of its code.
 */
export function useAdminFailureMessage() {
  const t = useTranslations("fieldErrors.period");
  const apiErrors = useApiErrors();
  return useCallback(
    (error: unknown) => {
      if (!(error instanceof ApiError)) return apiErrors.message(UNKNOWN_ERROR);
      if (error.fieldErrors.search?.length) return t("search");
      return Object.keys(error.fieldErrors).length > 0 ? t("admin") : apiErrors.message(error.code);
    },
    [t, apiErrors],
  );
}
