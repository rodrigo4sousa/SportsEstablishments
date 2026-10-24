import { isDateKey } from "@/lib/datetime";

/**
 * What the player has chosen on a club page: a pitch, a date and a slot. It lives in the URL
 * (`?pitch=<id>&date=YYYY-MM-DD&slot=<instant>`), not in a store: that is what lets someone who is sent to the login
 * page come back to the same pitch, date and slot, and makes the choice shareable.
 */
export type BookingSelection = {
  /** The pitch in the URL if the club has it, otherwise the club's first pitch (null only if it has none). */
  pitchId: string | null;
  /** "YYYY-MM-DD": the date in the URL, or today. */
  date: string;
  /** False when the URL has a date that is not a real date or is outside today..+30 days: nothing is asked of the API. */
  dateInWindow: boolean;
  /** The chosen slot's start as an instant with its zone, as the API gave it; null when none. */
  slot: string | null;
};

// An instant with its zone, as the API writes slot times ("2026-10-25T00:00:00Z"); the zone is what keeps the
// October hour that happens twice unambiguous. Anything else in the URL is ignored.
const ZONED_INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})$/;

export function resolveSelection(params: URLSearchParams, pitches: readonly { id: string }[], range: { min: string; max: string }): BookingSelection {
  const wanted = params.get("pitch");
  const pitchId = pitches.find((pitch) => pitch.id === wanted)?.id ?? pitches[0]?.id ?? null;

  const date = params.get("date") ?? range.min;
  const dateInWindow = isDateKey(date) && date >= range.min && date <= range.max;

  const slot = params.get("slot");
  return { pitchId, date, dateInWindow, slot: slot !== null && ZONED_INSTANT.test(slot) ? slot : null };
}

export type SelectionPatch = { pitch?: string | null; date?: string | null; slot?: string | null };

/** The address of the same page with some choices changed; `null` removes one. Other parameters are kept. */
export function selectionHref(pathname: string, params: URLSearchParams, patch: SelectionPatch): string {
  const next = new URLSearchParams(params);
  for (const [name, value] of Object.entries(patch)) {
    if (value === null || value === undefined) {
      next.delete(name);
    } else {
      next.set(name, value);
    }
  }
  const query = next.toString();
  return query ? `${pathname}?${query}` : pathname;
}

/** Two instants written differently ("…T10:00:00Z", "…T11:00:00+01:00") are the same moment. */
export function sameInstant(first: string, second: string): boolean {
  return new Date(first).getTime() === new Date(second).getTime();
}
