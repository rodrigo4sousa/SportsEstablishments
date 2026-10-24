"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useMemo, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ClubNotFound } from "@/features/clubs/components/club-not-found";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { formatDateKeyLong, formatLisbonDate, lisbonToday } from "@/lib/datetime";
import { calendarDayQueryKey, fetchCalendarDay } from "../api";
import { CALENDAR_MAX_DATE, CALENDAR_MIN_DATE, isCalendarDate, neighbourDate, viewFromParams, viewHref, type CalendarView } from "../calendar-url";
import { layoutDay } from "../layout";
import { BookingDetails } from "./booking-details";
import { DayGrid } from "./day-grid";

/**
 * US11: the owner's daily calendar of one club: a column per pitch, a row per hour, with the bookings (player, time and
 * status) and the blocks (with their reason). The view lives in the address (`?date=&cancelled=1&booking=`), so it can be
 * shared and the back button works. Cancelled bookings are hidden until asked for. Opening a booking shows its details and,
 * if it is confirmed and has not started, the way to cancel it (US12). A club that is not the owner's is the not-found page.
 */
export function ClubCalendar({ clubId }: { clubId: string }) {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const [today] = useState(() => lisbonToday());
  const view = viewFromParams(params, today);

  const day = useQuery({
    queryKey: calendarDayQueryKey(clubId, view.date, view.includeCancelled),
    queryFn: () => fetchCalendarDay(clubId, view.date, view.includeCancelled),
    retry: false,
  });
  const layout = useMemo(() => (day.data && day.data.from === view.date ? layoutDay(view.date, day.data) : null), [day.data, view.date]);

  const show = (patch: Partial<CalendarView>) => router.replace(viewHref(pathname, { ...view, ...patch }, today), { scroll: false });
  const goToDate = (date: string | null) => {
    if (date !== null && isCalendarDate(date)) show({ date, booking: null });
  };

  if (day.isError && day.error instanceof ApiError && day.error.status === 404) {
    return <ClubNotFound />;
  }

  const previous = neighbourDate(view.date, -1);
  const next = neighbourDate(view.date, 1);

  return (
    <div className="flex flex-col gap-6">
      <p>
        <Link href={`/manage/clubs/${clubId}`} className="text-sm text-muted-foreground underline underline-offset-4">
          ← Voltar ao clube
        </Link>
      </p>

      <div className="flex flex-wrap items-end gap-x-6 gap-y-4">
        <div role="group" aria-label="Escolher o dia" className="flex flex-wrap items-center gap-2">
          <Button type="button" variant="outline" disabled={previous === null} onClick={() => goToDate(previous)}>
            Dia anterior
          </Button>
          <Button type="button" variant="outline" onClick={() => goToDate(today)}>
            Hoje
          </Button>
          <Button type="button" variant="outline" disabled={next === null} onClick={() => goToDate(next)}>
            Dia seguinte
          </Button>
          <Input
            type="date"
            aria-label="Data"
            className="w-44"
            min={CALENDAR_MIN_DATE}
            max={CALENDAR_MAX_DATE}
            value={view.date}
            onChange={(event) => goToDate(event.target.value)}
          />
        </div>

        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            className="size-4 accent-foreground"
            checked={view.includeCancelled}
            onChange={(event) => show({ includeCancelled: event.target.checked })}
          />
          Mostrar reservas canceladas
        </label>
      </div>

      <h2 className="text-lg font-semibold first-letter:uppercase">
        {formatDateKeyLong(view.date)} ({formatLisbonDate(`${view.date}T12:00:00Z`)})
      </h2>

      {day.isPending ? (
        <p role="status" className="text-muted-foreground">
          A carregar o calendário…
        </p>
      ) : day.isError ? (
        <div role="alert" className="flex flex-col items-start gap-3">
          <p>{errorMessage(day.error instanceof ApiError ? day.error.code : "unknown_error")}</p>
          <Button type="button" variant="outline" onClick={() => void day.refetch()}>
            Tentar novamente
          </Button>
        </div>
      ) : day.data.pitches.length === 0 ? (
        <p role="status" className="rounded-md border border-dashed px-4 py-6 text-muted-foreground">
          Este clube ainda não tem campos ativos.
        </p>
      ) : layout === null ? (
        <p role="status" className="rounded-md border border-dashed px-4 py-6 text-muted-foreground">
          Nenhum campo abre neste dia e não há reservas nem bloqueios.
        </p>
      ) : (
        <DayGrid layout={layout} selectedId={view.booking} onOpenBooking={(booking) => show({ booking })} />
      )}

      {view.booking ? <BookingDetails key={view.booking} clubId={clubId} bookingId={view.booking} onClose={() => show({ booking: null })} /> : null}
    </div>
  );
}
