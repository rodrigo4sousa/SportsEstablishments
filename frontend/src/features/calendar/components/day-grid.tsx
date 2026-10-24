import { statusLabel } from "@/features/my-bookings/status";
import { sportLabel } from "@/features/pitches/sports";
import { formatLisbonSlotTime } from "@/lib/datetime";
import { HOUR_HEIGHT_PX, heightOf, type DayLayout, type PlacedItem } from "../layout";

const STATUS_STYLES: Record<string, string> = {
  confirmed: "border-emerald-600/50 bg-emerald-100 text-emerald-950 dark:bg-emerald-950 dark:text-emerald-100",
  no_show: "border-amber-600/50 bg-amber-100 text-amber-950 dark:bg-amber-950 dark:text-amber-100",
  cancelled_by_player: "border-dashed border-muted-foreground/50 bg-muted text-muted-foreground line-through",
  cancelled_by_club: "border-dashed border-muted-foreground/50 bg-muted text-muted-foreground line-through",
};

const BLOCK_STYLE =
  "border-slate-500/60 bg-slate-200 text-slate-900 bg-[repeating-linear-gradient(135deg,transparent,transparent_6px,rgba(100,116,139,0.28)_6px,rgba(100,116,139,0.28)_12px)] dark:bg-slate-800 dark:text-slate-100";

type DayGridProps = {
  layout: DayLayout;
  /** The booking whose details are open. */
  selectedId: string | null;
  onOpenBooking: (bookingId: string) => void;
};

/** The position of an item inside its column: by real elapsed minutes, and side by side with what overlaps it. */
function positionOf(item: PlacedItem, gridStartMinute: number) {
  return {
    top: heightOf(item.startMinute - gridStartMinute),
    height: heightOf(item.endMinute - item.startMinute),
    left: `calc(${(item.lane / item.lanes) * 100}% + 2px)`,
    width: `calc(${100 / item.lanes}% - 4px)`,
  };
}

/**
 * US11: the daily view. One column per pitch and one row per hour; a booking is a button, a block is a hatched box with
 * its reason. Times go through `formatLisbonSlotTime`, so the repeated hour of 25 October reads «01:00 (verão)» and
 * «01:00 (inverno)».
 */
export function DayGrid({ layout, selectedId, onOpenBooking }: DayGridProps) {
  const totalHeight = heightOf(layout.gridEndMinute - layout.gridStartMinute);

  return (
    <div className="overflow-x-auto rounded-lg border">
      <div className="flex min-w-max">
        <div className="w-24 shrink-0 border-r">
          <div className="h-14 border-b" aria-hidden="true" />
          <ol aria-label="Horas" className="relative" style={{ height: totalHeight }}>
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

        {layout.columns.map((column) => (
          <div key={column.pitch.id} role="group" aria-label={column.pitch.name} className="w-44 shrink-0 border-r last:border-r-0">
            <div className="flex h-14 flex-col justify-center border-b px-2">
              <p className="truncate text-sm font-medium">{column.pitch.name}</p>
              <p className="truncate text-xs text-muted-foreground">
                {sportLabel(column.pitch.sport)}
                {column.pitch.status === "inactive" ? " · Inativo" : ""}
              </p>
            </div>
            <div className="relative bg-muted/60" style={{ height: totalHeight }}>
              {column.open ? (
                <div
                  aria-hidden="true"
                  data-open-range=""
                  className="absolute inset-x-0 bg-background"
                  style={{
                    top: heightOf(column.open.fromMinute - layout.gridStartMinute),
                    height: heightOf(column.open.toMinute - column.open.fromMinute),
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
              {!column.open ? (
                <p className="absolute inset-x-0 top-2 text-center text-xs text-muted-foreground" aria-label={`${column.pitch.name}: fechado`}>
                  Fechado
                </p>
              ) : null}
              {column.items.map((item) =>
                item.kind === "booking" ? (
                  <BookingBox key={item.id} item={item} layout={layout} selected={selectedId === item.id} onOpen={() => onOpenBooking(item.id)} />
                ) : (
                  <BlockBox key={item.id} item={item} layout={layout} />
                ),
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function times(startsAt: string, endsAt: string): string {
  return `${formatLisbonSlotTime(startsAt)} – ${formatLisbonSlotTime(endsAt)}`;
}

function BookingBox({
  item,
  layout,
  selected,
  onOpen,
}: {
  item: Extract<PlacedItem, { kind: "booking" }>;
  layout: DayLayout;
  selected: boolean;
  onOpen: () => void;
}) {
  const { booking } = item;
  const label = statusLabel(booking.status);
  return (
    <button
      type="button"
      data-kind="booking"
      data-minute-start={item.startMinute}
      data-minute-end={item.endMinute}
      aria-label={`${booking.playerName}, ${times(booking.startsAt, booking.endsAt)}, ${label}`}
      aria-pressed={selected}
      onClick={onOpen}
      className={`absolute flex flex-col overflow-hidden rounded-md border px-1.5 py-1 text-left text-xs leading-tight outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50 ${STATUS_STYLES[booking.status] ?? STATUS_STYLES.confirmed} ${selected ? "ring-2 ring-foreground" : ""}`}
      style={positionOf(item, layout.gridStartMinute)}
    >
      <span className="truncate font-medium">{booking.playerName}</span>
      <span className="truncate tabular-nums">{times(booking.startsAt, booking.endsAt)}</span>
      <span className="truncate">{label}</span>
    </button>
  );
}

function BlockBox({ item, layout }: { item: Extract<PlacedItem, { kind: "block" }>; layout: DayLayout }) {
  const { block } = item;
  return (
    <div
      role="group"
      data-kind="block"
      data-minute-start={item.startMinute}
      data-minute-end={item.endMinute}
      aria-label={`Bloqueio, ${times(block.startsAt, block.endsAt)}${block.reason ? `, ${block.reason}` : ""}`}
      className={`absolute flex flex-col overflow-hidden rounded-md border border-dashed px-1.5 py-1 text-xs leading-tight ${BLOCK_STYLE}`}
      style={positionOf(item, layout.gridStartMinute)}
    >
      <span className="truncate font-medium">Bloqueio</span>
      <span className="truncate tabular-nums">{times(block.startsAt, block.endsAt)}</span>
      {block.reason ? <span className="truncate">{block.reason}</span> : null}
    </div>
  );
}
