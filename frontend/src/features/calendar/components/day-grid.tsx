import { useTranslations } from "next-intl";
import { ScrollRegion } from "@/components/scroll-region";
import { useStatusLabel } from "@/features/my-bookings/status";
import { useSportLabel } from "@/features/pitches/sports";
import { useFormats, type Formats } from "@/i18n/formats";
import { HOUR_HEIGHT_PX, heightOf, type DayLayout, type PlacedItem } from "../layout";

const STATUS_STYLES: Record<string, string> = {
  confirmed: "border-emerald-600/50 bg-emerald-100 text-emerald-950 dark:bg-emerald-950 dark:text-emerald-100",
  no_show: "border-amber-600/50 bg-amber-100 text-amber-950 dark:bg-amber-950 dark:text-amber-100",
  cancelled_by_player: "border-dashed border-muted-foreground/50 bg-muted text-muted-foreground line-through",
  cancelled_by_club: "border-dashed border-muted-foreground/50 bg-muted text-muted-foreground line-through",
};

// The mark on a booking the club's owner made for a customer without an account (US13) is `calendar.grid.createdByClub`.

/** What the grids need to offer a new booking where the owner clicks an empty time (US13). */
export type NewBookingAction = {
  /** Called with the pitch and the instant the clicked hour starts. */
  onEmptyTimeClick: (pitchId: string, startsAt: string) => void;
  /** Whether a new booking can be made in the hour starting at this instant (it is not over, and within the booking window). */
  canBookAt: (startsAt: string) => boolean;
};

const BLOCK_STYLE =
  "border-slate-500/60 bg-slate-200 text-slate-900 bg-[repeating-linear-gradient(135deg,transparent,transparent_6px,rgba(100,116,139,0.28)_6px,rgba(100,116,139,0.28)_12px)] dark:bg-slate-800 dark:text-slate-100";

type DayGridProps = {
  layout: DayLayout;
  /** The booking whose details are open. */
  selectedId: string | null;
  onOpenBooking: (bookingId: string) => void;
  /** Without it the grid is read-only. */
  newBooking?: NewBookingAction;
  /** The pitch a phone shows (UX-005): there the grid is one column wide, from `sm` up every pitch is shown. Default: the first. */
  selectedPitchId?: string | null;
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
export function DayGrid({ layout, selectedId, onOpenBooking, newBooking, selectedPitchId }: DayGridProps) {
  const t = useTranslations("calendar.grid");
  const formats = useFormats();
  const sportLabel = useSportLabel();
  const totalHeight = heightOf(layout.gridEndMinute - layout.gridStartMinute);
  const phonePitchId = layout.columns.some((column) => column.pitch.id === selectedPitchId) ? selectedPitchId : layout.columns[0]?.pitch.id;

  return (
    <ScrollRegion label={t("dayCalendar")}>
      <div className="flex sm:min-w-max">
        {/* The hours stay in view when the pitches scroll sideways (UX-005). */}
        <div className="sticky left-0 z-20 w-16 shrink-0 border-r bg-background sm:w-24">
          <div className="h-14 border-b" aria-hidden="true" />
          <ol aria-label={t("hours")} className="relative" style={{ height: totalHeight }}>
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

        {layout.columns.map((column) => (
          <div key={column.pitch.id} role="group" aria-label={column.pitch.name} className={`${column.pitch.id === phonePitchId ? "min-w-0 flex-1" : "hidden"} border-r sm:block sm:min-w-44 sm:flex-1 last:border-r-0`}>
            <div className="flex h-14 flex-col justify-center border-b px-2">
              <p className="truncate text-sm font-medium">{column.pitch.name}</p>
              <p className="truncate text-xs text-muted-foreground">
                {sportLabel(column.pitch.sport)}
                {column.pitch.status === "inactive" ? t("inactive") : ""}
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
                <HourCell
                  key={row.minute}
                  top={heightOf(row.minute - layout.gridStartMinute)}
                  label={newBooking && column.pitch.status === "active" && column.open && row.minute < column.open.toMinute && row.minute + 60 > column.open.fromMinute && newBooking.canBookAt(row.startsAt) ? t("newBookingAt", { pitch: column.pitch.name, time: formats.slotTime(row.startsAt) }) : null}
                  onClick={() => newBooking?.onEmptyTimeClick(column.pitch.id, row.startsAt)}
                />
              ))}
              {!column.open ? (
                <p className="absolute inset-x-0 top-2 text-center text-xs text-muted-foreground" aria-label={t("closedOf", { name: column.pitch.name })}>
                  {t("closed")}
                </p>
              ) : null}
              {column.items.map((item) =>
                item.kind === "booking" ? (
                  <BookingBox key={item.id} item={item} gridStartMinute={layout.gridStartMinute} selected={selectedId === item.id} onOpen={() => onOpenBooking(item.id)} />
                ) : (
                  <BlockBox key={item.id} item={item} gridStartMinute={layout.gridStartMinute} />
                ),
              )}
            </div>
          </div>
        ))}
      </div>
    </ScrollRegion>
  );
}

/**
 * One hour of a column: a line, and, where the owner can book (US13), a button that starts a new booking at that time.
 * The buttons sit under the bookings and are left out of the tab order (the "Nova reserva" button is the keyboard way in).
 */
export function HourCell({ top, label, onClick }: { top: number; label: string | null; onClick: () => void }) {
  if (label === null) {
    return <div aria-hidden="true" className="absolute inset-x-0 border-b" style={{ top, height: HOUR_HEIGHT_PX }} />;
  }
  return (
    <button
      type="button"
      tabIndex={-1}
      aria-label={label}
      data-new-booking=""
      onClick={onClick}
      className="absolute inset-x-0 border-b outline-none hover:bg-sky-100/60 dark:hover:bg-sky-950/40"
      style={{ top, height: HOUR_HEIGHT_PX }}
    />
  );
}

export function times(formats: Formats, startsAt: string, endsAt: string): string {
  return `${formats.slotTime(startsAt)} – ${formats.slotTime(endsAt)}`;
}

/**
 * A booking in a column: a button that opens its details. Shared by the daily and the weekly view. Its name is its visible text
 * (booker, times, status, origin), with no `aria-label` of its own, so what is read is what is seen (UX-016).
 */
export function BookingBox({
  item,
  gridStartMinute,
  selected,
  onOpen,
}: {
  item: Extract<PlacedItem, { kind: "booking" }>;
  gridStartMinute: number;
  selected: boolean;
  onOpen: () => void;
}) {
  const t = useTranslations("calendar.grid");
  const formats = useFormats();
  const statusLabel = useStatusLabel();
  const { booking } = item;
  const label = statusLabel(booking.status);
  // US13: a booking the club made for a customer says so, in the box and for screen readers.
  const origin = booking.createdByClub ? t("createdByClub") : null;
  return (
    <button
      type="button"
      data-kind="booking"
      data-minute-start={item.startMinute}
      data-minute-end={item.endMinute}
      data-booking-id={item.id}
      data-created-by-club={booking.createdByClub || undefined}
      aria-pressed={selected}
      onClick={onOpen}
      className={`absolute flex flex-col overflow-hidden rounded-md border px-1.5 py-1 text-left text-xs leading-tight focus-visible:z-10 focus-visible:-outline-offset-2 ${STATUS_STYLES[booking.status] ?? STATUS_STYLES.confirmed} ${booking.createdByClub ? "border-l-4 border-l-sky-600" : ""} ${selected ? "ring-2 ring-foreground" : ""}`}
      style={positionOf(item, gridStartMinute)}
    >
      <span className="truncate font-medium">{booking.bookerName}</span>{" "}
      <span className="truncate tabular-nums">{times(formats, booking.startsAt, booking.endsAt)}</span>{" "}
      <span className="truncate">
        {label}
        {origin ? ` · ${origin}` : ""}
      </span>
    </button>
  );
}

/** A block in a column: hatched, with its reason, and nothing to open. Shared by the daily and the weekly view. */
export function BlockBox({ item, gridStartMinute }: { item: Extract<PlacedItem, { kind: "block" }>; gridStartMinute: number }) {
  const t = useTranslations("calendar.grid");
  const formats = useFormats();
  const { block } = item;
  const span = times(formats, block.startsAt, block.endsAt);
  return (
    <div
      role="group"
      data-kind="block"
      data-minute-start={item.startMinute}
      data-minute-end={item.endMinute}
      aria-label={block.reason ? t("blockLabelWithReason", { times: span, reason: block.reason }) : t("blockLabel", { times: span })}
      className={`absolute flex flex-col overflow-hidden rounded-md border border-dashed px-1.5 py-1 text-xs leading-tight ${BLOCK_STYLE}`}
      style={positionOf(item, gridStartMinute)}
    >
      <span className="truncate font-medium">{t("block")}</span>
      <span className="truncate tabular-nums">{span}</span>
      {block.reason ? <span className="truncate">{block.reason}</span> : null}
    </div>
  );
}
