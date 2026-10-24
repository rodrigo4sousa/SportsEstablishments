import { lisbonDayMinutes, lisbonInstantAtMinute, lisbonLocalMinutes, minutesFromLisbonDayStart } from "@/lib/datetime";
import type { CalendarBlock, CalendarBooking, CalendarPitch, DayCalendar } from "./types";

/** How tall one hour of the daily view is, in pixels: every position is minutes times this over 60. */
export const HOUR_HEIGHT_PX = 64;

const MINUTES_PER_HOUR = 60;

export type PlacedBooking = { kind: "booking"; id: string; booking: CalendarBooking; startMinute: number; endMinute: number; lane: number; lanes: number };

export type PlacedBlock = { kind: "block"; id: string; block: CalendarBlock; startMinute: number; endMinute: number; lane: number; lanes: number };

export type PlacedItem = PlacedBooking | PlacedBlock;

export type OpenRange = { fromMinute: number; toMinute: number };

export type PitchColumn = {
  pitch: CalendarPitch;
  /** When the pitch is open on this day, in minutes from the start of the day; null when it is closed. */
  open: OpenRange | null;
  items: PlacedItem[];
};

export type GridRow = {
  /** Real elapsed minutes from the start of the Lisbon day to the start of the row. */
  minute: number;
  /** The instant the row starts, for its label (`formatLisbonSlotTime`). */
  startsAt: string;
};

export type DayLayout = {
  date: string;
  /** Minutes from the start of the day to the top of the grid, and to its bottom: whole hours. */
  gridStartMinute: number;
  gridEndMinute: number;
  rows: GridRow[];
  columns: PitchColumn[];
};

/** The height in pixels of a stretch of minutes. */
export function heightOf(minutes: number): number {
  return (minutes * HOUR_HEIGHT_PX) / MINUTES_PER_HOUR;
}

function openRangeOf(pitch: CalendarPitch, date: string, dayMinutes: number): OpenRange | null {
  const day = pitch.days.find((candidate) => candidate.date === date);
  if (!day || day.opens === null || day.closes === null) {
    return null;
  }
  const fromMinute = lisbonLocalMinutes(date, day.opens);
  // A closing time of 00:00 is midnight: the end of the day, not its start.
  const toMinute = day.closes === "00:00" ? dayMinutes : lisbonLocalMinutes(date, day.closes);
  return toMinute > fromMinute ? { fromMinute, toMinute } : null;
}

/** Gives each item of one column a lane and the number of lanes of its cluster, so items that overlap in time sit side by side. */
function assignLanes(items: Array<Omit<PlacedItem, "lane" | "lanes">>): PlacedItem[] {
  const sorted = [...items].sort(
    (a, b) => a.startMinute - b.startMinute || a.endMinute - b.endMinute || (a.kind === b.kind ? 0 : a.kind === "booking" ? -1 : 1) || a.id.localeCompare(b.id),
  );
  const placed: PlacedItem[] = [];
  let cluster: PlacedItem[] = [];
  let laneEnds: number[] = [];
  let clusterEnd = Number.NEGATIVE_INFINITY;

  const closeCluster = () => {
    for (const item of cluster) {
      item.lanes = laneEnds.length;
    }
    cluster = [];
    laneEnds = [];
  };

  for (const item of sorted) {
    if (item.startMinute >= clusterEnd) {
      closeCluster();
    }
    let lane = laneEnds.findIndex((end) => end <= item.startMinute);
    if (lane === -1) {
      lane = laneEnds.length;
      laneEnds.push(item.endMinute);
    } else {
      laneEnds[lane] = item.endMinute;
    }
    clusterEnd = cluster.length === 0 ? item.endMinute : Math.max(clusterEnd, item.endMinute);
    const withLane = { ...item, lane, lanes: 1 } as PlacedItem;
    cluster.push(withLane);
    placed.push(withLane);
  }
  closeCluster();
  return placed;
}

/**
 * The daily view of one Lisbon date: one column per pitch, one row per hour from the earliest opening to the latest
 * closing of that day (widened to whole hours to take in a booking or a block outside the opening hours, kept to the day).
 * Everything is positioned by real elapsed minutes from the start of the Lisbon day, never by the clock's hours, so the
 * 25-hour day (2026-10-25) has 25 rows when it is open all day and the 23-hour day (2026-03-29) has 23, and a booking at
 * 01:30 WEST and one at 01:30 WET land on different rows. Returns null when nothing is open and nothing is booked or
 * blocked that day.
 */
export function layoutDay(date: string, calendar: DayCalendar): DayLayout | null {
  const dayMinutes = lisbonDayMinutes(date);
  const clamp = (minute: number) => Math.min(Math.max(minute, 0), dayMinutes);

  const opens = new Map<string, OpenRange | null>(calendar.pitches.map((pitch) => [pitch.id, openRangeOf(pitch, date, dayMinutes)]));

  const bookings = calendar.bookings.flatMap((booking) => {
    const startMinute = clamp(minutesFromLisbonDayStart(booking.startsAt, date));
    const endMinute = clamp(minutesFromLisbonDayStart(booking.endsAt, date));
    return endMinute > startMinute ? [{ kind: "booking" as const, id: booking.id, booking, pitchId: booking.pitchId, startMinute, endMinute }] : [];
  });
  const blocks = calendar.blocks.flatMap((block) => {
    const startMinute = clamp(minutesFromLisbonDayStart(block.startsAt, date));
    const endMinute = clamp(minutesFromLisbonDayStart(block.endsAt, date));
    return endMinute > startMinute ? [{ kind: "block" as const, id: block.id, block, pitchId: block.pitchId, startMinute, endMinute }] : [];
  });

  const extents = [
    ...[...opens.values()].flatMap((range) => (range ? [range.fromMinute, range.toMinute] : [])),
    ...[...bookings, ...blocks].flatMap((item) => [item.startMinute, item.endMinute]),
  ];
  if (extents.length === 0) {
    return null;
  }

  const gridStartMinute = Math.floor(Math.min(...extents) / MINUTES_PER_HOUR) * MINUTES_PER_HOUR;
  const gridEndMinute = Math.max(Math.ceil(Math.max(...extents) / MINUTES_PER_HOUR) * MINUTES_PER_HOUR, gridStartMinute + MINUTES_PER_HOUR);

  const rows: GridRow[] = [];
  for (let minute = gridStartMinute; minute < gridEndMinute; minute += MINUTES_PER_HOUR) {
    rows.push({ minute, startsAt: lisbonInstantAtMinute(date, minute).toISOString() });
  }

  const columns = calendar.pitches.map((pitch): PitchColumn => {
    const own = [...bookings, ...blocks].filter((item) => item.pitchId === pitch.id);
    return { pitch, open: opens.get(pitch.id) ?? null, items: assignLanes(own) };
  });

  return { date, gridStartMinute, gridEndMinute, rows, columns };
}
