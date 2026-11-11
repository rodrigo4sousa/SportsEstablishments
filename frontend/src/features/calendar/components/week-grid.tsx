import { useTranslations } from "next-intl";
import { ScrollRegion } from "@/components/scroll-region";
import { WEEKDAYS, type WeekdayKey } from "@/features/pitches/schedule";
import { useFormats } from "@/i18n/formats";
import { formatLisbonDate, lisbonInstantAtMinute, weekdayIndex } from "@/lib/datetime";
import { HOUR_HEIGHT_PX, heightOf, type WeekLayout } from "../layout";
import { BlockBox, BookingBox, HourCell, type NewBookingAction } from "./day-grid";

type WeekGridProps = {
  layout: WeekLayout;
  /** The booking whose details are open. */
  selectedId: string | null;
  onOpenBooking: (bookingId: string) => void;
  /** Without it the grid is read-only. */
  newBooking?: NewBookingAction;
};

/** The weekday key of a "YYYY-MM-DD" date ("monday"), Monday first; its name is `weekdays.<key>` in the catalogs. */
export function weekdayKey(date: string): WeekdayKey {
  return WEEKDAYS[weekdayIndex(date)].key;
}

/**
 * US11: the weekly view of one pitch. Seven columns, Monday first, with the same boxes as the daily view (a booking is a
 * button, a block is hatched with its reason) and the same rule: everything is placed by real elapsed minutes from the start
 * of its own Lisbon day. The hours on the left are the clock of an ordinary day, so on a day the clocks change the boxes
 * after the change sit an hour off that scale; each box says its own time, and the page says so (see `ClockChangeNote`).
 */
export function WeekGrid({ layout, selectedId, onOpenBooking, newBooking }: WeekGridProps) {
  const t = useTranslations("calendar.grid");
  const weekdays = useTranslations("weekdays");
  const formats = useFormats();
  const totalHeight = heightOf(layout.gridEndMinute - layout.gridStartMinute);

  return (
    <ScrollRegion label={t("weekCalendar")}>
      <div className="flex min-w-max">
        {/* The hours stay in view when the days scroll sideways (UX-005). */}
        <div className="sticky left-0 z-20 w-20 shrink-0 border-r bg-background">
          <div className="h-14 border-b" aria-hidden="true" />
          <ol aria-label={t("hours")} style={{ height: totalHeight }}>
            {layout.rows.map((row) => (
              <li
                key={row.minute}
                data-minute={row.minute}
                className="border-b px-2 pt-1 text-xs tabular-nums text-muted-foreground"
                style={{ height: HOUR_HEIGHT_PX }}
              >
                {formats.slotTime(row.startsAt)}
              </li>
            ))}
          </ol>
        </div>

        {layout.days.map((day) => {
          const name = weekdays(weekdayKey(day.date));
          const dateText = formatLisbonDate(`${day.date}T12:00:00Z`);
          return (
            <div key={day.date} role="group" aria-label={t("dayLabel", { name, date: dateText })} data-date={day.date} className="min-w-36 flex-1 border-r last:border-r-0">
              <div className="flex h-14 flex-col justify-center border-b px-2">
                <p className="truncate text-sm font-medium">{name}</p>
                <p className="truncate text-xs tabular-nums text-muted-foreground">
                  {dateText}
                  {day.dayMinutes !== 1440 ? t("hoursLong", { hours: day.dayMinutes / 60 }) : ""}
                </p>
              </div>
              <div className="relative bg-muted/60" style={{ height: totalHeight }}>
                {day.open ? (
                  <div
                    aria-hidden="true"
                    data-open-range=""
                    className="absolute inset-x-0 bg-background"
                    style={{
                      top: heightOf(day.open.fromMinute - layout.gridStartMinute),
                      height: heightOf(day.open.toMinute - day.open.fromMinute),
                    }}
                  />
                ) : null}
                {layout.rows.map((row) => {
                  // The hour of this day that starts `row.minute` minutes after its start; a 23-hour day has no such hour at its end.
                  const startsAt = row.minute < day.dayMinutes ? lisbonInstantAtMinute(day.date, row.minute).toISOString() : null;
                  const bookable =
                    newBooking &&
                    startsAt !== null &&
                    layout.pitch.status === "active" &&
                    day.open &&
                    row.minute < day.open.toMinute &&
                    row.minute + 60 > day.open.fromMinute &&
                    newBooking.canBookAt(startsAt);
                  return (
                    <HourCell
                      key={row.minute}
                      top={heightOf(row.minute - layout.gridStartMinute)}
                      label={bookable && startsAt ? t("newBookingAtDay", { day: name, date: dateText, time: formats.slotTime(startsAt) }) : null}
                      onClick={() => startsAt && newBooking?.onEmptyTimeClick(layout.pitch.id, startsAt)}
                    />
                  );
                })}
                {!day.open ? (
                  <p className="absolute inset-x-0 top-2 text-center text-xs text-muted-foreground" aria-label={t("closedOf", { name })}>
                    {t("closed")}
                  </p>
                ) : null}
                {day.items.map((item) =>
                  item.kind === "booking" ? (
                    <BookingBox
                      key={item.id}
                      item={item}
                      gridStartMinute={layout.gridStartMinute}
                      selected={selectedId === item.id}
                      onOpen={() => onOpenBooking(item.id)}
                    />
                  ) : (
                    <BlockBox key={item.id} item={item} gridStartMinute={layout.gridStartMinute} />
                  ),
                )}
              </div>
            </div>
          );
        })}
      </div>
    </ScrollRegion>
  );
}

/** Said above the weekly grid when the clocks change in the week, so that the shifted scale is not a surprise. */
export function ClockChangeNote({ layout }: { layout: WeekLayout }) {
  const t = useTranslations("calendar.grid");
  if (layout.clockChangeDays.length === 0) {
    return null;
  }
  const days = layout.days.filter((day) => layout.clockChangeDays.includes(day.date));
  return (
    <p role="note" className="rounded-md border px-3 py-2 text-sm text-muted-foreground">
      {t("clockChange", {
        days: days.map((day) => t("clockChangeDay", { date: formatLisbonDate(`${day.date}T12:00:00Z`), hours: day.dayMinutes / 60 })).join("; "),
      })}
    </p>
  );
}
