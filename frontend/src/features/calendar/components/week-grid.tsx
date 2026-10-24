import { WEEKDAYS } from "@/features/pitches/schedule";
import { formatLisbonDate, formatLisbonSlotTime, weekdayIndex } from "@/lib/datetime";
import { HOUR_HEIGHT_PX, heightOf, type WeekLayout } from "../layout";
import { BlockBox, BookingBox } from "./day-grid";

type WeekGridProps = {
  layout: WeekLayout;
  /** The booking whose details are open. */
  selectedId: string | null;
  onOpenBooking: (bookingId: string) => void;
};

/** "Segunda-feira" for a "YYYY-MM-DD" date, Monday first. */
export function weekdayName(date: string): string {
  return WEEKDAYS[weekdayIndex(date)].label;
}

/**
 * US11: the weekly view of one pitch. Seven columns, Monday first, with the same boxes as the daily view (a booking is a
 * button, a block is hatched with its reason) and the same rule: everything is placed by real elapsed minutes from the start
 * of its own Lisbon day. The hours on the left are the clock of an ordinary day, so on a day the clocks change the boxes
 * after the change sit an hour off that scale; each box says its own time, and the page says so (see `ClockChangeNote`).
 */
export function WeekGrid({ layout, selectedId, onOpenBooking }: WeekGridProps) {
  const totalHeight = heightOf(layout.gridEndMinute - layout.gridStartMinute);

  return (
    <div className="overflow-x-auto rounded-lg border">
      <div className="flex min-w-max">
        <div className="w-20 shrink-0 border-r">
          <div className="h-14 border-b" aria-hidden="true" />
          <ol aria-label="Horas" style={{ height: totalHeight }}>
            {layout.rows.map((row) => (
              <li
                key={row.minute}
                data-minute={row.minute}
                className="border-b px-2 pt-1 text-xs tabular-nums text-muted-foreground"
                style={{ height: HOUR_HEIGHT_PX }}
              >
                {formatLisbonSlotTime(row.startsAt)}
              </li>
            ))}
          </ol>
        </div>

        {layout.days.map((day) => {
          const name = weekdayName(day.date);
          const dateText = formatLisbonDate(`${day.date}T12:00:00Z`);
          return (
            <div key={day.date} role="group" aria-label={`${name}, ${dateText}`} data-date={day.date} className="w-36 shrink-0 border-r last:border-r-0">
              <div className="flex h-14 flex-col justify-center border-b px-2">
                <p className="truncate text-sm font-medium">{name}</p>
                <p className="truncate text-xs tabular-nums text-muted-foreground">
                  {dateText}
                  {day.dayMinutes !== 1440 ? ` · ${day.dayMinutes / 60} h` : ""}
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
                {layout.rows.map((row) => (
                  <div
                    key={row.minute}
                    aria-hidden="true"
                    className="absolute inset-x-0 border-b"
                    style={{ top: heightOf(row.minute - layout.gridStartMinute), height: HOUR_HEIGHT_PX }}
                  />
                ))}
                {!day.open ? (
                  <p className="absolute inset-x-0 top-2 text-center text-xs text-muted-foreground" aria-label={`${name}: fechado`}>
                    Fechado
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
    </div>
  );
}

/** Said above the weekly grid when the clocks change in the week, so that the shifted scale is not a surprise. */
export function ClockChangeNote({ layout }: { layout: WeekLayout }) {
  if (layout.clockChangeDays.length === 0) {
    return null;
  }
  const days = layout.days.filter((day) => layout.clockChangeDays.includes(day.date));
  return (
    <p role="note" className="rounded-md border px-3 py-2 text-sm text-muted-foreground">
      Nesta semana o relógio muda (
      {days.map((day) => `${formatLisbonDate(`${day.date}T12:00:00Z`)}, com ${day.dayMinutes / 60} horas`).join("; ")}). Cada dia está desenhado
      pelo tempo realmente decorrido desde o seu início, por isso, nesse dia, depois da mudança as reservas ficam uma hora desfasadas da escala
      das horas; a hora escrita em cada reserva é a certa.
    </p>
  );
}
