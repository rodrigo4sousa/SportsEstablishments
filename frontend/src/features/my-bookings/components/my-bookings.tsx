"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useState, type MouseEvent } from "react";
import { toast } from "sonner";
import { EmptyState } from "@/components/empty-state";
import { LoadingRegion } from "@/components/loading-region";
import { ResultNotice } from "@/components/result-notice";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { catalogQueryKey } from "@/features/catalog/api";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { isExpectedApiError, unexpectedErrorMessage } from "@/lib/api/expected-error";
import { loginUrl } from "@/lib/auth/redirects";
import { formatLisbonDayHeading, lisbonDateKey } from "@/lib/datetime";
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

/** What the last cancellation said. A refusal belongs to the booking it is about, and is shown in its row while that row is there. */
type Notice = { kind: "success" | "error"; text: string; bookingId: string | null };

/** The bookings of the page in groups of consecutive ones on the same Lisbon day, in the order the API sent them. */
function groupByDay(items: MyBooking[]): { key: string; heading: (now: Date) => string; items: MyBooking[] }[] {
  const groups: { key: string; heading: (now: Date) => string; items: MyBooking[] }[] = [];
  for (const booking of items) {
    const key = lisbonDateKey(booking.startsAt);
    const last = groups.at(-1);
    if (last?.key === key) {
      last.items.push(booking);
    } else {
      groups.push({ key, heading: (now) => formatLisbonDayHeading(booking.startsAt, now), items: [booking] });
    }
  }
  return groups;
}

/** A plain click on a link is ours (the address and the state change together); a click with a modifier key opens a new tab as usual. */
function isPlainClick(event: MouseEvent): boolean {
  return event.button === 0 && !event.metaKey && !event.ctrlKey && !event.shiftKey && !event.altKey;
}

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
  // The day headings say «Hoje» and «Amanhã» for the day the page was opened.
  const [now] = useState(() => new Date());

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
      setNotice({ kind: "success", text: "Reserva cancelada. O horário voltou a ficar livre.", bookingId: null });
      // The slot is free again on the club's page, and so the search's "free slots" may have changed.
      void queryClient.invalidateQueries({ queryKey: catalogQueryKey });
    },
    onError: (error, booking) => {
      if (!isExpectedApiError(error)) {
        toast.error(unexpectedErrorMessage(error));
        return;
      }
      if (error.status === 401) {
        router.replace(loginUrl(hrefFor(pathname, scope, page)));
        return;
      }
      setNotice({ kind: "error", text: errorMessage(error.code), bookingId: booking.id });
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

  // A refusal is shown in the row it is about, while that row is in the list; anything else (the cancellation worked, or the booking is
  // gone) is shown above the list, in the same region. Either way it takes focus, which also brings it into view (UX-003).
  const items = bookings.data?.items ?? [];
  const refusalRow = notice?.kind === "error" && notice.bookingId !== null && items.some((booking) => booking.id === notice.bookingId) ? notice : null;
  const listNotice = notice !== null && refusalRow === null ? notice : null;

  return (
    <div className="flex flex-col gap-4">
      <nav aria-label="Próximas ou anteriores" className="flex gap-1 border-b">
        {TABS.map((tab) => (
          <Link
            key={tab.scope}
            href={hrefFor(pathname, tab.scope, 1)}
            replace
            scroll={false}
            aria-current={scope === tab.scope ? "page" : undefined}
            onClick={(event) => {
              if (isPlainClick(event)) {
                event.preventDefault();
                go(tab.scope, 1);
              }
            }}
            className={`-mb-px inline-flex min-h-11 items-center border-b-2 px-4 text-sm ${
              scope === tab.scope ? "border-foreground font-medium" : "border-transparent text-muted-foreground hover:text-foreground"
            }`}
          >
            {tab.label}
          </Link>
        ))}
      </nav>

      <section aria-labelledby="bookings-heading" className="flex flex-col gap-4">
        <h2 id="bookings-heading" className="sr-only">
          {scope === "upcoming" ? "Reservas próximas" : "Reservas anteriores"}
        </h2>
        {listNotice ? <ResultNotice kind={listNotice.kind}>{listNotice.text}</ResultNotice> : null}
        {bookings.isPending || sessionEnded ? (
          <LoadingRegion label="A carregar as reservas…" className="flex flex-col gap-3">
            {[0, 1, 2].map((key) => (
              <Skeleton key={key} className="h-24 w-full rounded-lg" />
            ))}
          </LoadingRegion>
        ) : bookings.isError ? (
          <div role="alert" className="flex flex-col items-start gap-3">
            <p>{errorMessage(bookings.error instanceof ApiError ? bookings.error.code : "unknown_error")}</p>
            <Button type="button" size="cta" variant="outline" onClick={() => void bookings.refetch()}>
              Tentar novamente
            </Button>
          </div>
        ) : bookings.data.totalCount === 0 ? (
          <EmptyState title={EMPTY_TEXT[scope]} />
        ) : bookings.data.items.length === 0 ? (
          <div role="status" className="flex flex-col items-start gap-3">
            <p className="text-muted-foreground">Esta página não tem reservas.</p>
            <Button type="button" size="cta" variant="outline" onClick={() => go(scope, 1)}>
              Voltar à primeira página
            </Button>
          </div>
        ) : (
          <List
            data={bookings.data}
            now={now}
            cancellable={scope === "upcoming"}
            confirmingId={confirmingId}
            cancellingId={cancel.isPending ? (cancel.variables?.id ?? null) : null}
            refusal={refusalRow}
            onAskToCancel={(booking) => {
              setNotice(null);
              setConfirmingId(booking.id);
            }}
            onKeep={() => setConfirmingId(null)}
            onConfirmCancel={(booking) => cancel.mutate(booking)}
            onPage={(next) => go(scope, next)}
          />
        )}
      </section>
    </div>
  );
}

type ListProps = {
  data: { items: MyBooking[]; page: number; pageSize: number; totalCount: number };
  now: Date;
  cancellable: boolean;
  confirmingId: string | null;
  cancellingId: string | null;
  refusal: Notice | null;
  onAskToCancel: (booking: MyBooking) => void;
  onKeep: () => void;
  onConfirmCancel: (booking: MyBooking) => void;
  onPage: (page: number) => void;
};

function List({ data, now, cancellable, confirmingId, cancellingId, refusal, onAskToCancel, onKeep, onConfirmCancel, onPage }: ListProps) {
  const { items, page, pageSize, totalCount } = data;
  const pages = Math.max(1, Math.ceil(totalCount / pageSize));

  return (
    <>
      <p role="status" className="text-sm text-muted-foreground">
        {totalCount === 1 ? "1 reserva" : `${totalCount} reservas`}
      </p>
      {/* UX-022: one short list per day, headed by the day, instead of one tall card per booking repeating it. */}
      <div className="flex flex-col gap-5">
        {groupByDay(items).map((group) => {
          const headingId = `bookings-day-${group.key}`;
          return (
            <div key={group.key} className="flex flex-col gap-2">
              <h3 id={headingId} className="text-sm font-semibold">
                {group.heading(now)}
              </h3>
              <ul aria-labelledby={headingId} className="flex flex-col gap-2">
                {group.items.map((booking) => (
                  <MyBookingItem
                    key={booking.id}
                    booking={booking}
                    cancellable={cancellable}
                    confirming={confirmingId === booking.id}
                    cancelling={cancellingId === booking.id}
                    refusal={refusal?.bookingId === booking.id ? refusal.text : null}
                    onAskToCancel={() => onAskToCancel(booking)}
                    onKeep={onKeep}
                    onConfirmCancel={() => onConfirmCancel(booking)}
                  />
                ))}
              </ul>
            </div>
          );
        })}
      </div>
      {pages > 1 ? (
        <nav aria-label="Páginas" className="flex items-center justify-between gap-3">
          <Button type="button" size="cta" variant="outline" disabled={page <= 1} onClick={() => onPage(page - 1)}>
            Anterior
          </Button>
          <p className="text-sm text-muted-foreground">
            Página {page} de {pages}
          </p>
          <Button type="button" size="cta" variant="outline" disabled={page >= pages} onClick={() => onPage(page + 1)}>
            Seguinte
          </Button>
        </nav>
      ) : null}
    </>
  );
}
