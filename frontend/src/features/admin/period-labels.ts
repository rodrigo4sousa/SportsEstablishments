import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { formatDateKey, formatDateKeyMonth, formatDateKeyShort } from "@/lib/datetime";
import type { Granularity } from "./types";

type Period = { start: string; end: string };

/** The short axis label of a period: the day, the Monday of the week, the month. */
export function axisLabel(granularity: Granularity, period: Period): string {
  return granularity === "month" ? formatDateKeyMonth(period.start) : formatDateKeyShort(period.start);
}

/** The full name of a period, for the tooltip and the table: a day, "de … a …" for a week, a month. */
export function periodLabel(granularity: Granularity, period: Period): string {
  if (granularity === "month") return formatDateKeyMonth(period.start);
  if (granularity === "week") return `${formatDateKey(period.start)} a ${formatDateKey(period.end)}`;
  return formatDateKey(period.start);
}

/** The API's own message for a rejected range (a field error in Portuguese), else the one for its code. */
export function failureMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const field = Object.values(error.fieldErrors).flat()[0];
    return field ?? errorMessage(error.code);
  }
  return errorMessage("unknown_error");
}
