"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import { catalogQueryKey, clubSlotsQueryKey, fetchPublicSlots, publicSlotsQueryKey } from "@/features/catalog/api";
import { publicErrorMessage } from "@/features/catalog/messages";
import type { PublicClub } from "@/features/catalog/types";
import { useCurrentUser } from "@/features/auth/hooks/use-current-user";
import { myBookingsQueryKey } from "@/features/my-bookings/api";
import { formatPrice } from "@/features/pitches/price";
import { DATE_OUT_OF_WINDOW, previewDateRange } from "@/features/pitches/schedule";
import { sportLabel } from "@/features/pitches/sports";
import { ApiError, NETWORK_ERROR } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { loginUrl } from "@/lib/auth/redirects";
import { formatDateKeyLong } from "@/lib/datetime";
import { createBooking, type Booking } from "../api";
import { resolveSelection, sameInstant, selectionHref, type SelectionPatch } from "../selection";
import { BookingConfirmation, BookingSummary } from "./booking-summary";
import { SlotList } from "./slot-list";

const SLOT_GONE = "Este horário já não está disponível. Escolhe outro.";

/**
 * US17 and US18: the free slots of a club's pitches, and booking one. The player picks a pitch and a date (today to 30
 * days ahead, Lisbon), then a free slot, and sees a summary to confirm. The choice is kept in the URL, so a visitor who
 * is sent to sign in comes back to the same pitch, date and slot. Only players can book: owners and admins see the
 * slots but no booking button. If the slot was taken meanwhile the API answers 409 and the slots are loaded again.
 */
export function ClubBooking({ club }: { club: PublicClub }) {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const queryClient = useQueryClient();
  const [range] = useState(() => previewDateRange());
  const [failure, setFailure] = useState<string | null>(null);
  const [confirmed, setConfirmed] = useState<Booking | null>(null);

  const selection = resolveSelection(params, club.pitches, range);
  const pitch = club.pitches.find((candidate) => candidate.id === selection.pitchId);

  const account = useCurrentUser();
  const session = account.isPending ? "checking" : (account.data?.profile ?? "anonymous");
  const isPlayer = session === "Player";
  const cannotBook = session === "Owner" || session === "Admin";

  const slots = useQuery({
    queryKey: publicSlotsQueryKey(club.slug, selection.pitchId ?? "", selection.date),
    queryFn: () => fetchPublicSlots(club.slug, selection.pitchId ?? "", selection.date),
    enabled: pitch !== undefined && selection.dateInWindow,
  });

  const chosen = selection.slot === null ? undefined : slots.data?.slots.find((slot) => sameInstant(slot.startsAt, selection.slot ?? ""));
  const chosenFree = chosen?.available ? chosen : undefined;
  const slotGone = selection.slot !== null && slots.isSuccess && chosenFree === undefined;

  // Where to come back to after signing in: this page with the pitch, date and slot written out.
  const returnPath = selectionHref(pathname, params, { pitch: selection.pitchId, date: selection.date, slot: selection.slot });
  const choose = (patch: SelectionPatch) => router.replace(selectionHref(pathname, params, patch), { scroll: false });

  const book = useMutation({
    mutationFn: createBooking,
    onSuccess: (booking) => {
      setFailure(null);
      setConfirmed(booking);
      choose({ slot: null });
      // The slot is taken now, and so the search's "free slots" may have changed.
      void queryClient.invalidateQueries({ queryKey: catalogQueryKey });
      // The new booking belongs in "As minhas reservas".
      void queryClient.invalidateQueries({ queryKey: myBookingsQueryKey });
    },
    onError: (error) => {
      if (!(error instanceof ApiError)) {
        toast.error(errorMessage("unknown_error"));
        return;
      }
      if (error.status === 401) {
        // The session ended while the player was deciding: sign in again and come back to this slot.
        router.push(loginUrl(returnPath));
        return;
      }
      setFailure(publicErrorMessage(error.code));
      if (error.code !== NETWORK_ERROR) {
        // Taken, blocked, started, out of reach: the choice is gone, so show what is free now.
        choose({ slot: null });
        void queryClient.invalidateQueries({ queryKey: clubSlotsQueryKey(club.slug) });
      }
    },
  });

  if (!pitch) {
    return null;
  }

  const pick = (patch: SelectionPatch) => {
    setFailure(null);
    setConfirmed(null);
    choose(patch);
  };

  return (
    <section aria-labelledby="club-slots" className="flex flex-col gap-4">
      <h2 id="club-slots" className="text-lg font-semibold">
        Horários
      </h2>

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="flex flex-col gap-2">
          <Label htmlFor="booking-pitch">Campo</Label>
          <NativeSelect id="booking-pitch" value={pitch.id} onChange={(event) => pick({ pitch: event.target.value, slot: null })}>
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
            onChange={(event) => pick({ date: event.target.value, slot: null })}
          />
        </div>
      </div>

      {failure ? (
        <p role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 px-3 py-2 text-sm text-destructive">
          {failure}
        </p>
      ) : slotGone ? (
        <p role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 px-3 py-2 text-sm text-destructive">
          {SLOT_GONE}
        </p>
      ) : null}

      {confirmed ? (
        <BookingConfirmation
          details={{
            clubName: club.name,
            pitchName: club.pitches.find((candidate) => candidate.id === confirmed.pitchId)?.name ?? pitch.name,
            startsAt: confirmed.startsAt,
            endsAt: confirmed.endsAt,
            price: confirmed.price,
          }}
          dismissLabel="Ver outros horários"
          onDismiss={() => setConfirmed(null)}
        />
      ) : null}

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
          <SlotList
            slots={slots.data.slots}
            selected={chosenFree?.startsAt ?? null}
            interactive={!cannotBook}
            onSelect={(slot) => pick({ pitch: pitch.id, date: selection.date, slot: slot.startsAt })}
          />
        </>
      )}

      {cannotBook ? (
        <p className="text-sm text-muted-foreground">Só os jogadores podem fazer reservas. Entra com uma conta de jogador para reservar.</p>
      ) : chosenFree ? (
        <BookingSummary
          details={{ clubName: club.name, pitchName: pitch.name, startsAt: chosenFree.startsAt, endsAt: chosenFree.endsAt, price: chosenFree.price }}
        >
          {isPlayer ? (
            <>
              <Button
                type="button"
                disabled={book.isPending}
                onClick={() => {
                  setFailure(null);
                  book.mutate({ pitchId: pitch.id, startsAt: chosenFree.startsAt });
                }}
              >
                {book.isPending ? "A reservar…" : "Confirmar reserva"}
              </Button>
              <Button type="button" variant="outline" disabled={book.isPending} onClick={() => pick({ slot: null })}>
                Cancelar
              </Button>
            </>
          ) : session === "checking" ? (
            <Button type="button" disabled>
              A verificar a sessão…
            </Button>
          ) : (
            <>
              <Button asChild>
                <Link href={loginUrl(returnPath)}>Entrar para reservar</Link>
              </Button>
              <p className="text-sm text-muted-foreground">Depois de entrares, voltas a este horário.</p>
            </>
          )}
        </BookingSummary>
      ) : null}
    </section>
  );
}
