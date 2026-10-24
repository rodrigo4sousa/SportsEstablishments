"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import { ClubNotFound } from "@/features/clubs/components/club-not-found";
import { sportLabel } from "@/features/pitches/sports";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { formatDateKeyLong, formatLisbonDate, lisbonToday, startOfWeekDateKey } from "@/lib/datetime";
import { calendarRangeQueryKey, fetchCalendarRange } from "../api";
import {
  CALENDAR_MAX_DATE,
  CALENDAR_MIN_DATE,
  isCalendarDate,
  neighbourDate,
  rangeOf,
  viewFromParams,
  viewHref,
  type CalendarMode,
  type CalendarView,
} from "../calendar-url";
import { layoutDay, layoutWeek } from "../layout";
import { BookingDetails } from "./booking-details";
import { DayGrid } from "./day-grid";
import { ClockChangeNote, WeekGrid } from "./week-grid";

const MODES: { mode: CalendarMode; label: string }[] = [
  { mode: "day", label: "Vista diária" },
  { mode: "week", label: "Vista semanal" },
];

/**
 * US11: the owner's calendar of one club. The daily view has a column per pitch and a row per hour; the weekly view shows
 * one pitch, chosen from a list, in seven columns (Monday first). Both show the bookings (player, time and status) and the
 * blocks (with their reason), placed by real elapsed time. The view lives in the address (`?view=week&date=&pitch=&cancelled=1&booking=`),
 * so it can be shared and the back button works. Cancelled bookings are hidden until asked for. Opening a booking shows its
 * details and, if it is confirmed and has not started, the way to cancel it (US12). A club that is not the owner's is the
 * not-found page.
 */
export function ClubCalendar({ clubId }: { clubId: string }) {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const [today] = useState(() => lisbonToday());
  const view = viewFromParams(params, today);
  const weekly = view.mode === "week";
  const range = rangeOf(view);

  const calendar = useQuery({
    queryKey: calendarRangeQueryKey(clubId, range.from, range.to, view.includeCancelled),
    queryFn: () => fetchCalendarRange(clubId, range.from, range.to, view.includeCancelled),
    retry: false,
  });

  const data = calendar.data && calendar.data.from === range.from && calendar.data.to === range.to ? calendar.data : null;
  const monday = startOfWeekDateKey(view.date);
  const selectedPitch = data ? (data.pitches.find((pitch) => pitch.id === view.pitch) ?? data.pitches[0]) : undefined;
  const dayLayout = data && !weekly ? layoutDay(view.date, data) : null;
  const weekLayout = data && weekly && selectedPitch ? layoutWeek(monday, data, selectedPitch.id) : null;

  const show = (patch: Partial<CalendarView>) => router.replace(viewHref(pathname, { ...view, ...patch }, today), { scroll: false });
  const goToDate = (date: string | null) => {
    if (date !== null && isCalendarDate(date)) show({ date, booking: null });
  };
  const step = weekly ? 7 : 1;

  if (calendar.isError && calendar.error instanceof ApiError && calendar.error.status === 404) {
    return <ClubNotFound />;
  }

  const previous = neighbourDate(view.date, -step);
  const next = neighbourDate(view.date, step);

  return (
    <div className="flex flex-col gap-6">
      <p>
        <Link href={`/manage/clubs/${clubId}`} className="text-sm text-muted-foreground underline underline-offset-4">
          ← Voltar ao clube
        </Link>
      </p>

      <div className="flex flex-wrap items-end gap-x-6 gap-y-4">
        <div role="group" aria-label="Tipo de vista" className="flex items-center gap-2">
          {MODES.map(({ mode, label }) => (
            <Button
              key={mode}
              type="button"
              variant={view.mode === mode ? "default" : "outline"}
              aria-pressed={view.mode === mode}
              onClick={() => show({ mode, booking: null })}
            >
              {label}
            </Button>
          ))}
        </div>

        <div role="group" aria-label={weekly ? "Escolher a semana" : "Escolher o dia"} className="flex flex-wrap items-center gap-2">
          <Button type="button" variant="outline" disabled={previous === null} onClick={() => goToDate(previous)}>
            {weekly ? "Semana anterior" : "Dia anterior"}
          </Button>
          <Button type="button" variant="outline" onClick={() => goToDate(today)}>
            Hoje
          </Button>
          <Button type="button" variant="outline" disabled={next === null} onClick={() => goToDate(next)}>
            {weekly ? "Semana seguinte" : "Dia seguinte"}
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

        {weekly && data && data.pitches.length > 0 ? (
          <div className="flex flex-col gap-1">
            <Label htmlFor="calendar-pitch" className="text-sm">
              Campo
            </Label>
            <NativeSelect id="calendar-pitch" className="w-56" value={selectedPitch?.id ?? ""} onChange={(event) => show({ pitch: event.target.value, booking: null })}>
              {data.pitches.map((pitch) => (
                <option key={pitch.id} value={pitch.id}>
                  {pitch.name} · {sportLabel(pitch.sport)}
                  {pitch.status === "inactive" ? " · Inativo" : ""}
                </option>
              ))}
            </NativeSelect>
          </div>
        ) : null}

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
        {weekly
          ? `${formatDateKeyLong(monday)} – ${formatDateKeyLong(range.to)} (${formatLisbonDate(`${monday}T12:00:00Z`)} – ${formatLisbonDate(`${range.to}T12:00:00Z`)})`
          : `${formatDateKeyLong(view.date)} (${formatLisbonDate(`${view.date}T12:00:00Z`)})`}
      </h2>

      {calendar.isPending ? (
        <p role="status" className="text-muted-foreground">
          A carregar o calendário…
        </p>
      ) : calendar.isError ? (
        <div role="alert" className="flex flex-col items-start gap-3">
          <p>{errorMessage(calendar.error instanceof ApiError ? calendar.error.code : "unknown_error")}</p>
          <Button type="button" variant="outline" onClick={() => void calendar.refetch()}>
            Tentar novamente
          </Button>
        </div>
      ) : calendar.data.pitches.length === 0 ? (
        <p role="status" className="rounded-md border border-dashed px-4 py-6 text-muted-foreground">
          Este clube ainda não tem campos ativos.
        </p>
      ) : weekly ? (
        weekLayout === null ? (
          <p role="status" className="rounded-md border border-dashed px-4 py-6 text-muted-foreground">
            Este campo não abre nesta semana e não tem reservas nem bloqueios.
          </p>
        ) : (
          <>
            <ClockChangeNote layout={weekLayout} />
            <WeekGrid layout={weekLayout} selectedId={view.booking} onOpenBooking={(booking) => show({ booking })} />
          </>
        )
      ) : dayLayout === null ? (
        <p role="status" className="rounded-md border border-dashed px-4 py-6 text-muted-foreground">
          Nenhum campo abre neste dia e não há reservas nem bloqueios.
        </p>
      ) : (
        <DayGrid layout={dayLayout} selectedId={view.booking} onOpenBooking={(booking) => show({ booking })} />
      )}

      {view.booking ? <BookingDetails key={view.booking} clubId={clubId} bookingId={view.booking} onClose={() => show({ booking: null })} /> : null}
    </div>
  );
}
