"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { EmptyState } from "@/components/empty-state";
import { LoadingRegion } from "@/components/loading-region";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import { ClubName } from "@/features/clubs/components/club-name";
import { ClubNotFound } from "@/features/clubs/components/club-not-found";
import { sportLabel } from "@/features/pitches/sports";
import { previewDateRange } from "@/features/pitches/schedule";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import {
  formatDateKeyLong,
  formatLisbonDate,
  lisbonDateKey,
  lisbonToday,
  startOfWeekDateKey,
} from "@/lib/datetime";
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
import type { ClubBooking } from "../types";
import { layoutDay, layoutWeek } from "../layout";
import { BookingDetails } from "./booking-details";
import { DayGrid, type NewBookingAction } from "./day-grid";
import { NewBookingForm, type NewBookingPrefill } from "./new-booking-form";
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
  // When the page was opened: enough to not offer an hour that is over; the API decides in the end.
  const [openedAt] = useState(() => Date.now());
  const [bookingWindow] = useState(() => previewDateRange());
  // US13: the form to book a slot for a customer. Ephemeral, so it is not in the address; the key restarts it for another click.
  const [newBooking, setNewBooking] = useState<{
    key: number;
    prefill: NewBookingPrefill;
  } | null>(null);
  const [created, setCreated] = useState<string | null>(null);
  const view = viewFromParams(params, today);
  const weekly = view.mode === "week";
  const range = rangeOf(view);

  const calendar = useQuery({
    queryKey: calendarRangeQueryKey(
      clubId,
      range.from,
      range.to,
      view.includeCancelled,
    ),
    queryFn: () =>
      fetchCalendarRange(clubId, range.from, range.to, view.includeCancelled),
    retry: false,
  });

  const data =
    calendar.data &&
    calendar.data.from === range.from &&
    calendar.data.to === range.to
      ? calendar.data
      : null;
  const monday = startOfWeekDateKey(view.date);
  const selectedPitch = data
    ? (data.pitches.find((pitch) => pitch.id === view.pitch) ?? data.pitches[0])
    : undefined;
  const dayLayout = data && !weekly ? layoutDay(view.date, data) : null;
  const weekLayout =
    data && weekly && selectedPitch
      ? layoutWeek(monday, data, selectedPitch.id)
      : null;

  const show = (patch: Partial<CalendarView>) =>
    router.replace(viewHref(pathname, { ...view, ...patch }, today), {
      scroll: false,
    });
  // UX-004: closing the details gives focus back to the booking that was open (the grid stays on screen, so the box is still there).
  const closeBooking = () => {
    const closed = view.booking;
    show({ booking: null });
    if (closed) {
      document
        .querySelector<HTMLElement>(`[data-booking-id="${closed}"]`)
        ?.focus();
    }
  };
  const goToDate = (date: string | null) => {
    if (date !== null && isCalendarDate(date)) show({ date, booking: null });
  };
  const step = weekly ? 7 : 1;

  const activePitches = data
    ? data.pitches.filter((pitch) => pitch.status === "active")
    : [];
  const openNewBooking = (prefill: NewBookingPrefill) => {
    setCreated(null);
    setNewBooking((current) => ({ key: (current?.key ?? 0) + 1, prefill }));
    if (view.booking) show({ booking: null });
  };
  const newBookingAction: NewBookingAction = {
    // The hour of the cell the owner clicked: the date is the Lisbon day it is in.
    onEmptyTimeClick: (pitchId, startsAt) =>
      openNewBooking({ pitchId, date: lisbonDateKey(startsAt), startsAt }),
    // Not over yet, and not past the last day a booking can be made.
    canBookAt: (startsAt) =>
      Date.parse(startsAt) + 3_600_000 > openedAt &&
      lisbonDateKey(startsAt) >= bookingWindow.min &&
      lisbonDateKey(startsAt) <= bookingWindow.max,
  };
  const onCreated = (booking: ClubBooking) => {
    setNewBooking(null);
    setCreated(`Reserva criada para ${booking.bookerName}.`);
    // Show it: its day (and its pitch, in the weekly view) with its details open.
    show({
      date: lisbonDateKey(booking.startsAt),
      pitch: booking.pitchId,
      booking: booking.id,
    });
  };

  if (
    calendar.isError &&
    calendar.error instanceof ApiError &&
    calendar.error.status === 404
  ) {
    return <ClubNotFound />;
  }

  const previous = neighbourDate(view.date, -step);
  const next = neighbourDate(view.date, step);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link
          href={`/manage/clubs/${clubId}`}
          className="text-sm text-muted-foreground underline underline-offset-4"
        >
          ← Voltar ao clube
        </Link>
        <ClubName clubId={clubId} />
      </div>

      <div className="flex flex-wrap items-end gap-x-6 gap-y-4">
        <div
          role="group"
          aria-label="Tipo de vista"
          className="flex items-center gap-2"
        >
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

        <div
          role="group"
          aria-label={weekly ? "Escolher a semana" : "Escolher o dia"}
          className="flex flex-wrap items-center gap-2"
        >
          <Button
            type="button"
            variant="outline"
            disabled={previous === null}
            onClick={() => goToDate(previous)}
          >
            {weekly ? "Semana anterior" : "Dia anterior"}
          </Button>
          <Button
            type="button"
            variant="outline"
            onClick={() => goToDate(today)}
          >
            Hoje
          </Button>
          <Button
            type="button"
            variant="outline"
            disabled={next === null}
            onClick={() => goToDate(next)}
          >
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

        {data && data.pitches.length > 0 ? (
          // The week is one pitch, and on a phone so is the day (UX-006); a wider screen shows every pitch of the day, so no choice is needed there.
          <div className={`flex flex-col gap-1 ${weekly ? "" : "sm:hidden"}`}>
            <Label htmlFor="calendar-pitch" className="text-sm">
              Campo
            </Label>
            <NativeSelect
              id="calendar-pitch"
              className="w-full sm:w-56"
              value={selectedPitch?.id ?? ""}
              onChange={(event) =>
                show({ pitch: event.target.value, booking: null })
              }
            >
              {data.pitches.map((pitch) => (
                <option key={pitch.id} value={pitch.id}>
                  {pitch.name} · {sportLabel(pitch.sport)}
                  {pitch.status === "inactive" ? " · Inativo" : ""}
                </option>
              ))}
            </NativeSelect>
          </div>
        ) : null}

        <Button
          type="button"
          disabled={activePitches.length === 0}
          onClick={() =>
            openNewBooking({
              pitchId: weekly ? selectedPitch?.id : undefined,
              date: view.date,
            })
          }
        >
          Nova reserva
        </Button>

        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            className="size-4 accent-foreground"
            checked={view.includeCancelled}
            onChange={(event) =>
              show({ includeCancelled: event.target.checked })
            }
          />
          Mostrar reservas canceladas
        </label>
      </div>

      <h2 className="text-lg font-semibold first-letter:uppercase">
        {weekly
          ? `${formatDateKeyLong(monday)} – ${formatDateKeyLong(range.to)} (${formatLisbonDate(`${monday}T12:00:00Z`)} – ${formatLisbonDate(`${range.to}T12:00:00Z`)})`
          : `${formatDateKeyLong(view.date)} (${formatLisbonDate(`${view.date}T12:00:00Z`)})`}
      </h2>

      {created ? (
        <p
          role="status"
          className="rounded-md border border-emerald-600/40 bg-emerald-50 px-3 py-2 text-sm dark:bg-emerald-950/30"
        >
          {created}
        </p>
      ) : null}

      {newBooking && activePitches.length > 0 ? (
        <NewBookingForm
          key={newBooking.key}
          clubId={clubId}
          pitches={activePitches}
          prefill={newBooking.prefill}
          onClose={() => setNewBooking(null)}
          onCreated={onCreated}
        />
      ) : null}

      {/* UX-004: from `lg` up the details sit beside the grid and stay in view while it scrolls; on a phone they come first, above the one-pitch grid. */}
      <div className="flex flex-col gap-6 lg:flex-row lg:items-start">
        <div className="flex min-w-0 flex-1 flex-col gap-6">
          {calendar.isPending ? (
            <LoadingRegion label="A carregar o calendário…">
              <Skeleton className="h-96 w-full" />
            </LoadingRegion>
          ) : calendar.isError ? (
            <div role="alert" className="flex flex-col items-start gap-3">
              <p>
                {errorMessage(
                  calendar.error instanceof ApiError
                    ? calendar.error.code
                    : "unknown_error",
                )}
              </p>
              <Button
                type="button"
                variant="outline"
                onClick={() => void calendar.refetch()}
              >
                Tentar novamente
              </Button>
            </div>
          ) : calendar.data.pitches.length === 0 ? (
            <EmptyState
              title="Este clube ainda não tem campos ativos."
              description="O calendário aparece quando houver um campo ativo."
              action={
                <Button asChild variant="outline">
                  <Link href={`/manage/clubs/${clubId}`}>
                    Ir para os campos do clube
                  </Link>
                </Button>
              }
            />
          ) : weekly ? (
            weekLayout === null ? (
              <EmptyState title="Este campo não abre nesta semana e não tem reservas nem bloqueios." />
            ) : (
              <>
                <ClockChangeNote layout={weekLayout} />
                <WeekGrid
                  layout={weekLayout}
                  selectedId={view.booking}
                  onOpenBooking={(booking) => show({ booking })}
                  newBooking={newBookingAction}
                />
              </>
            )
          ) : dayLayout === null ? (
            <EmptyState title="Nenhum campo abre neste dia e não há reservas nem bloqueios." />
          ) : (
            <DayGrid
              layout={dayLayout}
              selectedId={view.booking}
              onOpenBooking={(booking) => show({ booking })}
              newBooking={newBookingAction}
              selectedPitchId={selectedPitch?.id}
            />
          )}
        </div>
        {view.booking ? (
          <div className="order-first lg:sticky lg:top-4 lg:order-none lg:w-96 lg:shrink-0">
            <BookingDetails
              key={view.booking}
              clubId={clubId}
              bookingId={view.booking}
              onClose={closeBooking}
            />
          </div>
        ) : null}
      </div>
    </div>
  );
}
