import type { PublicSlot } from "@/features/catalog/types";
import { formatPrice } from "@/features/pitches/price";
import { formatLisbonSlotTime } from "@/lib/datetime";
import { sameInstant } from "../selection";

type SlotListProps = {
  slots: PublicSlot[];
  /** The chosen slot's start, if any. */
  selected: string | null;
  /** True when a slot can be picked (visitors and players); owners and admins only look. */
  interactive: boolean;
  onSelect: (slot: PublicSlot) => void;
};

/**
 * US17: the slots of the day in Lisbon time, each with its price. A slot that is not available is shown disabled and
 * with no reason: the API does not say why (booked, blocked, closed or started), and neither does the page.
 */
export function SlotList({ slots, selected, interactive, onSelect }: SlotListProps) {
  return (
    <ul aria-label="Horários" className="grid grid-cols-2 gap-2 sm:grid-cols-3 md:grid-cols-4">
      {slots.map((slot) => {
        const isSelected = selected !== null && sameInstant(slot.startsAt, selected);
        const content = (
          <>
            <span className="block font-medium tabular-nums">
              {formatLisbonSlotTime(slot.startsAt)} – {formatLisbonSlotTime(slot.endsAt)}
            </span>{" "}
            <span className="block text-sm tabular-nums">{formatPrice(slot.price)}</span>
          </>
        );

        return (
          <li key={slot.startsAt}>
            {interactive ? (
              <button
                type="button"
                disabled={!slot.available}
                aria-pressed={slot.available ? isSelected : undefined}
                onClick={() => onSelect(slot)}
                className={`w-full rounded-md border px-3 py-2 text-left transition-colors focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-none ${
                  isSelected ? "border-primary bg-primary text-primary-foreground" : "hover:bg-accent"
                } disabled:cursor-not-allowed disabled:border-dashed disabled:bg-muted disabled:text-muted-foreground disabled:opacity-60 disabled:hover:bg-muted`}
              >
                {content}
              </button>
            ) : (
              <div
                data-available={slot.available}
                className={`rounded-md border px-3 py-2 ${slot.available ? "" : "border-dashed bg-muted text-muted-foreground opacity-60"}`}
              >
                {content}
              </div>
            )}
          </li>
        );
      })}
    </ul>
  );
}
