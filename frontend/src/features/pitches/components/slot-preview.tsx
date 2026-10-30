"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { formatLisbonLongDate, formatLisbonSlotTime, isDateKey } from "@/lib/datetime";
import { fetchSlots, slotsQueryKey } from "../api";
import { formatPrice } from "../price";
import { DATE_OUT_OF_WINDOW, previewDateRange } from "../schedule";

/**
 * US08: the slots a pitch's hours generate on a date, today to 30 days ahead, in Lisbon time. A read-only preview:
 * a slot taken by a confirmed booking is marked "Reservado" and one that overlaps a block "Bloqueado" (it can be both).
 * The repeated October hour is labelled "(verão)" and "(inverno)". Each slot shows the price a booking costs now (US10).
 */
export function SlotPreview({ clubId, pitchId }: { clubId: string; pitchId: string }) {
  const [range] = useState(() => previewDateRange());
  const [date, setDate] = useState(range.min);
  const inWindow = isDateKey(date) && date >= range.min && date <= range.max;

  const slots = useQuery({
    queryKey: slotsQueryKey(clubId, pitchId, date),
    queryFn: () => fetchSlots(clubId, pitchId, date),
    enabled: inWindow,
  });

  const inputId = `preview-date-${pitchId}`;
  let result;
  if (!inWindow) {
    result = (
      <p id={`${inputId}-error`} role="alert" className="text-sm text-destructive">
        {date === "" ? "Escolhe uma data." : DATE_OUT_OF_WINDOW}
      </p>
    );
  } else if (slots.isPending) {
    result = (
      <p role="status" className="text-muted-foreground">
        A carregar os horários…
      </p>
    );
  } else if (slots.isError) {
    const code = slots.error instanceof ApiError ? slots.error.code : "unknown_error";
    result = (
      <div role="alert" className="flex flex-col items-start gap-3">
        <p>{errorMessage(code)}</p>
        <Button type="button" variant="outline" onClick={() => void slots.refetch()}>
          Tentar novamente
        </Button>
      </div>
    );
  } else if (slots.data.slots.length === 0) {
    result = <p className="text-muted-foreground">Sem horários neste dia: o campo está fechado ou as horas de abertura não chegam para uma reserva.</p>;
  } else {
    result = (
      <>
        <p className="text-sm text-muted-foreground">
          {formatLisbonLongDate(slots.data.slots[0].startsAt)} · {slots.data.slots.length} horários de {slots.data.slotDurationMinutes} minutos
        </p>
        <ul aria-label="Horários gerados" className="flex flex-wrap gap-2">
          {slots.data.slots.map((slot) => (
            <li
              key={slot.startsAt}
              data-blocked={slot.blocked || undefined}
              data-booked={slot.booked || undefined}
              className={`rounded-md border px-3 py-1 text-sm tabular-nums ${slot.blocked || slot.booked ? "border-dashed bg-muted text-muted-foreground" : ""}`}
            >
              {formatLisbonSlotTime(slot.startsAt)} – {formatLisbonSlotTime(slot.endsAt)} · {formatPrice(slot.price)}
              {slot.booked ? (
                <>
                  {" "}
                  <span className="ml-1 font-medium">Reservado</span>
                </>
              ) : null}
              {slot.blocked ? (
                <>
                  {" "}
                  <span className="ml-1 font-medium">Bloqueado</span>
                </>
              ) : null}
            </li>
          ))}
        </ul>
      </>
    );
  }

  return (
    <section aria-label="Pré-visualização dos horários" className="flex flex-col gap-3">
      <h4 className="font-medium">Pré-visualização dos horários</h4>
      <div className="flex flex-col gap-2">
        <Label htmlFor={inputId}>Data</Label>
        <Input
          id={inputId}
          type="date"
          min={range.min}
          max={range.max}
          value={date}
          className="sm:max-w-48"
          aria-invalid={inWindow ? undefined : true}
          aria-describedby={inWindow ? undefined : `${inputId}-error`}
          onChange={(event) => setDate(event.target.value)}
        />
      </div>
      {result}
      <p className="text-sm text-muted-foreground">Mostra os horários que as horas de abertura geram; os bloqueios e as reservas aparecem marcados.</p>
    </section>
  );
}
