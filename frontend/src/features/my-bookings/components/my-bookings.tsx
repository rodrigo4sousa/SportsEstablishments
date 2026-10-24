"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { catalogQueryKey } from "@/features/catalog/api";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { loginUrl } from "@/lib/auth/redirects";
import { cancelBooking, fetchMyBookings, myBookingsListQueryKey, myBookingsQueryKey } from "../api";
import type { BookingScope, MyBooking } from "../types";
import { MyBookingItem } from "./my-booking-item";

const TABS: { scope: BookingScope; label: string }[] = [
  { scope: "upcoming", label: "Próximas" },
  { scope: "past", label: "Anteriores" },
];

const EMPTY_TEXT: Record<BookingScope, string> = {
  upcoming: "Não tens reservas próximas. Pesquisa um clube para reservar um campo.",
  past: "Ainda não tens reservas anteriores.",
};

type Notice = { kind: "success" | "error"; text: string };

/** The page lives in the URL (`?tab=past&page=2`), so it survives a reload and the back button works. */
function pageFromParams(value: string | null): number {
  const page = value !== null && /^\d{1,5}$/.test(value) ? Number(value) : 1;
  return page >= 1 ? page : 1;
}

function hrefFor(pathname: string, scope: BookingScope, page: number): string {
  const params = new URLSearchParams();
  if (scope === "past") params.set("tab", "past");
  if (page > 1) params.set("page", String(page));
  const query = params.toString();
  return query ? `${pathname}?${query}` : pathname;
}

/**
 * US19: "As minhas reservas". Two tabs, «Próximas» and «Anteriores», 20 bookings to a page, and cancelling a confirmed
 * booking after a confirmation. The API decides whether a booking can still be cancelled (the club's window now); the
 * button only follows `canCancel`. After a cancellation, or a refusal, the lists (and, for a cancellation, the free
 * slots of the public pages) are loaded again.
 */
export function MyBookings() {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const queryClient = useQueryClient();
  const scope: BookingScope = params.get("tab") === "past" ? "past" : "upcoming";
  const page = pageFromParams(params.get("page"));
  const [confirmingId, setConfirmingId] = useState<string | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);

  const bookings = useQuery({
    queryKey: myBookingsListQueryKey(scope, page),
    queryFn: () => fetchMyBookings(scope, page),
    placeholderData: keepPreviousData,
  });

  // The session ended and could not be renewed: sign in again and come back to this list.
  const sessionEnded = bookings.error instanceof ApiError && bookings.error.status === 401;
  useEffect(() => {
    if (sessionEnded) {
      router.replace(loginUrl(hrefFor(pathname, scope, page)));
    }
  }, [sessionEnded, router, pathname, scope, page]);

  const cancel = useMutation({
    mutationFn: (booking: MyBooking) => cancelBooking(booking.id),
    onSuccess: () => {
      setNotice({ kind: "success", text: "Reserva cancelada. O horário voltou a ficar livre." });
      // The slot is free again on the club's page, and so the search's "free slots" may have changed.
      void queryClient.invalidateQueries({ queryKey: catalogQueryKey });
    },
    onError: (error) => {
      if (!(error instanceof ApiError)) {
        toast.error(errorMessage("unknown_error"));
        return;
      }
      if (error.status === 401) {
        router.replace(loginUrl(hrefFor(pathname, scope, page)));
        return;
      }
      setNotice({ kind: "error", text: errorMessage(error.code) });
    },
    onSettled: () => {
      setConfirmingId(null);
      // Cancelled or refused, what the list says may be out of date: the window, the status.
      void queryClient.invalidateQueries({ queryKey: myBookingsQueryKey });
    },
  });

  const go = (nextScope: BookingScope, nextPage: number) => {
    setConfirmingId(null);
    setNotice(null);
    router.replace(hrefFor(pathname, nextScope, nextPage), { scroll: false });
  };

  return (
    <div className="flex flex-col gap-4">
      <div role="tablist" aria-label="Reservas" className="flex gap-1 border-b">
        {TABS.map((tab) => (
          <button
            key={tab.scope}
            type="button"
            role="tab"
            id={`bookings-tab-${tab.scope}`}
            aria-selected={scope === tab.scope}
            aria-controls="bookings-panel"
            onClick={() => go(tab.scope, 1)}
            className={
              scope === tab.scope
                ? "-mb-px border-b-2 border-foreground px-4 py-2 text-sm font-medium"
                : "-mb-px border-b-2 border-transparent px-4 py-2 text-sm text-muted-foreground hover:text-foreground"
            }
          >
            {tab.label}
          </button>
        ))}
      </div>

      {notice ? (
        <p
          role={notice.kind === "error" ? "alert" : "status"}
          className={
            notice.kind === "error"
              ? "rounded-md border border-destructive/40 bg-destructive/10 px-3 py-2 text-sm text-destructive"
              : "rounded-md border border-emerald-600/40 bg-emerald-50 px-3 py-2 text-sm dark:bg-emerald-950/30"
          }
        >
          {notice.text}
        </p>
      ) : null}

      <div role="tabpanel" id="bookings-panel" aria-labelledby={`bookings-tab-${scope}`} className="flex flex-col gap-4">
        {bookings.isPending || sessionEnded ? (
          <p role="status" className="text-muted-foreground">
            A carregar as reservas…
          </p>
        ) : bookings.isError ? (
          <div role="alert" className="flex flex-col items-start gap-3">
            <p>{errorMessage(bookings.error instanceof ApiError ? bookings.error.code : "unknown_error")}</p>
            <Button type="button" variant="outline" onClick={() => void bookings.refetch()}>
              Tentar novamente
            </Button>
          </div>
        ) : bookings.data.totalCount === 0 ? (
          <p role="status" className="rounded-md border border-dashed px-4 py-6 text-muted-foreground">
            {EMPTY_TEXT[scope]}
          </p>
        ) : bookings.data.items.length === 0 ? (
          <div role="status" className="flex flex-col items-start gap-3">
            <p className="text-muted-foreground">Esta página não tem reservas.</p>
            <Button type="button" variant="outline" onClick={() => go(scope, 1)}>
              Voltar à primeira página
            </Button>
          </div>
        ) : (
          <List
            data={bookings.data}
            cancellable={scope === "upcoming"}
            confirmingId={confirmingId}
            cancellingId={cancel.isPending ? (cancel.variables?.id ?? null) : null}
            onAskToCancel={(booking) => {
              setNotice(null);
              setConfirmingId(booking.id);
            }}
            onKeep={() => setConfirmingId(null)}
            onConfirmCancel={(booking) => cancel.mutate(booking)}
            onPage={(next) => go(scope, next)}
          />
        )}
      </div>
    </div>
  );
}

type ListProps = {
  data: { items: MyBooking[]; page: number; pageSize: number; totalCount: number };
  cancellable: boolean;
  confirmingId: string | null;
  cancellingId: string | null;
  onAskToCancel: (booking: MyBooking) => void;
  onKeep: () => void;
  onConfirmCancel: (booking: MyBooking) => void;
  onPage: (page: number) => void;
};

function List({ data, cancellable, confirmingId, cancellingId, onAskToCancel, onKeep, onConfirmCancel, onPage }: ListProps) {
  const { items, page, pageSize, totalCount } = data;
  const pages = Math.max(1, Math.ceil(totalCount / pageSize));

  return (
    <>
      <p role="status" className="text-sm text-muted-foreground">
        {totalCount === 1 ? "1 reserva" : `${totalCount} reservas`}
      </p>
      <ul aria-label="Reservas" className="flex flex-col gap-3">
        {items.map((booking) => (
          <MyBookingItem
            key={booking.id}
            booking={booking}
            cancellable={cancellable}
            confirming={confirmingId === booking.id}
            cancelling={cancellingId === booking.id}
            onAskToCancel={() => onAskToCancel(booking)}
            onKeep={onKeep}
            onConfirmCancel={() => onConfirmCancel(booking)}
          />
        ))}
      </ul>
      {pages > 1 ? (
        <nav aria-label="Páginas" className="flex items-center justify-between gap-3">
          <Button type="button" variant="outline" disabled={page <= 1} onClick={() => onPage(page - 1)}>
            Anterior
          </Button>
          <p className="text-sm text-muted-foreground">
            Página {page} de {pages}
          </p>
          <Button type="button" variant="outline" disabled={page >= pages} onClick={() => onPage(page + 1)}>
            Seguinte
          </Button>
        </nav>
      ) : null}
    </>
  );
}
