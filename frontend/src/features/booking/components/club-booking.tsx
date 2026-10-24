"use client";

import { useQuery } from "@tanstack/react-query";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import { fetchPublicSlots, publicSlotsQueryKey } from "@/features/catalog/api";
import { publicErrorMessage } from "@/features/catalog/messages";
import type { PublicClub } from "@/features/catalog/types";
import { formatPrice } from "@/features/pitches/price";
import { DATE_OUT_OF_WINDOW, previewDateRange } from "@/features/pitches/schedule";
import { sportLabel } from "@/features/pitches/sports";
import { ApiError } from "@/lib/api/client";
import { formatDateKeyLong } from "@/lib/datetime";
import { resolveSelection, selectionHref, type SelectionPatch } from "../selection";
import { SlotList } from "./slot-list";

/**
 * US17: the free slots of a club's pitches. The player picks a pitch and a date (today to 30 days ahead, Lisbon) and
 * sees the day's slots; the choice is kept in the URL.
 */
export function ClubBooking({ club }: { club: PublicClub }) {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const [range] = useState(() => previewDateRange());
  const selection = resolveSelection(params, club.pitches, range);
  const pitch = club.pitches.find((candidate) => candidate.id === selection.pitchId);

  const slots = useQuery({
    queryKey: publicSlotsQueryKey(club.slug, selection.pitchId ?? "", selection.date),
    queryFn: () => fetchPublicSlots(club.slug, selection.pitchId ?? "", selection.date),
    enabled: pitch !== undefined && selection.dateInWindow,
  });

  const choose = (patch: SelectionPatch) => router.replace(selectionHref(pathname, params, patch), { scroll: false });

  if (!pitch) {
    return null;
  }

  return (
    <section aria-labelledby="club-slots" className="flex flex-col gap-4">
      <h2 id="club-slots" className="text-lg font-semibold">
        Horários
      </h2>

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="flex flex-col gap-2">
          <Label htmlFor="booking-pitch">Campo</Label>
          <NativeSelect id="booking-pitch" value={pitch.id} onChange={(event) => choose({ pitch: event.target.value, slot: null })}>
            {club.pitches.map((candidate) => (
              <option key={candidate.id} value={candidate.id}>
                {candidate.name} · {sportLabel(candidate.sport)} · {formatPrice(candidate.pricePerSlot)}
              </option>
            ))}
          </NativeSelect>
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor="booking-date">Data</Label>
          <Input
            id="booking-date"
            type="date"
            min={range.min}
            max={range.max}
            value={selection.date}
            aria-invalid={selection.dateInWindow ? undefined : true}
            aria-describedby={selection.dateInWindow ? undefined : "booking-date-error"}
            onChange={(event) => choose({ date: event.target.value, slot: null })}
          />
        </div>
      </div>

      {!selection.dateInWindow ? (
        <p id="booking-date-error" role="alert" className="text-sm text-destructive">
          {selection.date === "" ? "Escolhe uma data." : DATE_OUT_OF_WINDOW}
        </p>
      ) : slots.isPending ? (
        <p role="status" className="text-muted-foreground">
          A carregar os horários…
        </p>
      ) : slots.isError ? (
        <div role="alert" className="flex flex-col items-start gap-3">
          <p>{publicErrorMessage(slots.error instanceof ApiError ? slots.error.code : "unknown_error")}</p>
          <Button type="button" variant="outline" onClick={() => void slots.refetch()}>
            Tentar novamente
          </Button>
        </div>
      ) : slots.data.slots.length === 0 ? (
        <p className="text-muted-foreground">Sem horários neste dia.</p>
      ) : (
        <>
          <p className="text-sm text-muted-foreground">{formatDateKeyLong(slots.data.date)}</p>
          <SlotList slots={slots.data.slots} selected={selection.slot} interactive={false} onSelect={() => undefined} />
        </>
      )}
    </section>
  );
}
