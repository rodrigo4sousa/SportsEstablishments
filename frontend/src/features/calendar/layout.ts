import { addDaysToDateKey, lisbonDayMinutes, lisbonInstantAtMinute, lisbonLocalMinutes, minutesFromLisbonDayStart } from "@/lib/datetime";
import type { CalendarBlock, CalendarBooking, CalendarPitch, CalendarRange } from "./types";

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
export function layoutDay(date: string, calendar: CalendarRange): DayLayout | null {
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

export type WeekDay = {
  date: string;
  /** How long the Lisbon day lasts, in minutes: 1440, except on the days the clocks change. */
  dayMinutes: number;
  /** When the pitch is open that day, in minutes from the start of that day; null when it is closed. */
  open: OpenRange | null;
  items: PlacedItem[];
};

export type WeekLayout = {
  pitch: CalendarPitch;
  /** Seven days from the Monday given, in order. */
  days: WeekDay[];
  /** The grid is shared by the seven columns: whole hours of elapsed time, from the earliest to the latest of the week. */
  gridStartMinute: number;
  gridEndMinute: number;
  /** The shared hour scale, labelled with the clock of the first ordinary (24-hour) day of the week. */
  rows: GridRow[];
  /** The days of this week on which the clocks change: their items are placed by elapsed time, so they sit an hour off the scale after the change. */
  clockChangeDays: string[];
};

/**
 * The weekly view of one pitch: seven columns, Monday first, with the same rules as the daily view. Each day is laid out by
 * `layoutDay` (so by real elapsed minutes from the start of its own Lisbon day), and the seven share one vertical scale from
 * the earliest to the latest hour of the week. Returns null when the pitch is not in the calendar, or when nothing is open
 * and nothing is booked or blocked all week.
 */
export function layoutWeek(monday: string, calendar: CalendarRange, pitchId: string): WeekLayout | null {
  const pitch = calendar.pitches.find((candidate) => candidate.id === pitchId);
  if (!pitch) {
    return null;
  }

  const dates = Array.from({ length: 7 }, (_, index) => addDaysToDateKey(monday, index));
  const bookings = calendar.bookings.filter((booking) => booking.pitchId === pitchId);
  const blocks = calendar.blocks.filter((block) => block.pitchId === pitchId);
  const perDay = dates.map((date) => ({
    date,
    dayMinutes: lisbonDayMinutes(date),
    layout: layoutDay(date, { from: date, to: date, pitches: [pitch], bookings, blocks }),
  }));

  const layouts = perDay.flatMap((day) => (day.layout ? [day.layout] : []));
  if (layouts.length === 0) {
    return null;
  }
  const gridStartMinute = Math.min(...layouts.map((layout) => layout.gridStartMinute));
  const gridEndMinute = Math.max(...layouts.map((layout) => layout.gridEndMinute));

  const referenceDay = (perDay.find((day) => day.dayMinutes === 1440) ?? perDay[0]).date;
  const rows: GridRow[] = [];
  for (let minute = gridStartMinute; minute < gridEndMinute; minute += MINUTES_PER_HOUR) {
    rows.push({ minute, startsAt: lisbonInstantAtMinute(referenceDay, minute).toISOString() });
  }

  return {
    pitch,
    days: perDay.map(({ date, dayMinutes, layout }) => ({
      date,
      dayMinutes,
      open: layout?.columns[0].open ?? null,
      items: layout?.columns[0].items ?? [],
    })),
    gridStartMinute,
    gridEndMinute,
    rows,
    clockChangeDays: perDay.filter((day) => day.dayMinutes !== 1440).map((day) => day.date),
  };
}
