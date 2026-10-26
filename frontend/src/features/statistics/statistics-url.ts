import { isDateKey } from "@/lib/datetime";
import { isGuid } from "@/lib/guid";
import type { StatisticsRange } from "./api";

/**
 * The owner's statistics keep their view in the URL (`from`, `to`, and on a club's page `pitch`), so a reload or a shared link shows
 * the same numbers. Without dates the period is the last 30 days; a value that is not a date (or not a pitch id) is ignored.
 */
export function rangeFromParams(params: URLSearchParams): StatisticsRange {
  const from = params.get("from");
  const to = params.get("to");
  return { from: from !== null && isDateKey(from) ? from : null, to: to !== null && isDateKey(to) ? to : null };
}

export function pitchFromParams(params: URLSearchParams): string | null {
  const pitch = params.get("pitch");
  return pitch !== null && isGuid(pitch) ? pitch : null;
}

export function statisticsHref(pathname: string, range: StatisticsRange, pitchId: string | null = null): string {
  const params = new URLSearchParams();
  if (range.from) params.set("from", range.from);
  if (range.to) params.set("to", range.to);
  if (pitchId) params.set("pitch", pitchId);
  const text = params.toString();
  return text ? `${pathname}?${text}` : pathname;
}
