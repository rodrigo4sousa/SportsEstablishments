"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect } from "react";
import { PeriodFilter } from "@/components/period-filter";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { loginUrl } from "@/lib/auth/redirects";
import { addDaysToDateKey, formatDateKey, lisbonToday } from "@/lib/datetime";
import { formatPrice } from "@/features/pitches/price";
import { fetchOwnerStatistics, ownerStatisticsQueryKey, type StatisticsRange } from "../api";
import { formatCount, formatHours, formatOccupation } from "../format";
import { rangeFromParams, statisticsHref } from "../statistics-url";

/** The API's own message for a refused period (a field error in Portuguese), else the one for its code. */
export function statisticsFailureMessage(error: unknown): string {
  if (error instanceof ApiError) {
    return Object.values(error.fieldErrors).flat()[0] ?? errorMessage(error.code);
  }
  return errorMessage("unknown_error");
}

/** The last 30 days in Lisbon: what the fields show while the URL has no dates. */
export function defaultRange(now: Date = new Date()): { from: string; to: string } {
  const to = lisbonToday(now);
  return { from: addDaysToDateKey(to, -29), to };
}

/**
 * US14: «Estatísticas», every club of the owner over a period, one row each and the total: booked hours (confirmed bookings and
 * no-shows), open hours (the bookable time of the schedule now), occupation (booked over open), revenue (bookings that are not
 * cancelled) and the counts. The period is in the URL (`?from=&to=`, the last 30 days without it) and a club's name opens its own
 * statistics for the same period.
 */
export function OwnerStatistics() {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const range = rangeFromParams(params);
  const fallback = defaultRange();
  const statistics = useQuery({
    queryKey: ownerStatisticsQueryKey(range),
    queryFn: () => fetchOwnerStatistics(range),
    placeholderData: keepPreviousData,
  });

  const sessionEnded = statistics.error instanceof ApiError && statistics.error.status === 401;
  useEffect(() => {
    if (sessionEnded) {
      router.replace(loginUrl(statisticsHref(pathname, range)));
    }
  }, [sessionEnded, router, pathname, range]);

  const change = (next: StatisticsRange) => router.replace(statisticsHref(pathname, next), { scroll: false });
  const data = statistics.data;

  return (
    <div className="flex flex-col gap-6">
      <PeriodFilter idPrefix="stats" from={range.from} to={range.to} defaultFrom={data?.from ?? fallback.from} defaultTo={data?.to ?? fallback.to} onChange={change} />

      {statistics.isError && !sessionEnded ? (
        <div role="alert" className="flex flex-col items-start gap-3">
          <p>{statisticsFailureMessage(statistics.error)}</p>
          <Button type="button" variant="outline" onClick={() => void statistics.refetch()}>
            Tentar novamente
          </Button>
        </div>
      ) : statistics.isPending || sessionEnded ? (
        <p role="status" className="text-muted-foreground">
          A carregar as estatísticas…
        </p>
      ) : data!.clubs.length === 0 ? (
        <p role="status" className="rounded-md border border-dashed px-4 py-6 text-muted-foreground">
          Ainda não tens clubes. <Link href="/manage/clubs/new" className="underline underline-offset-4">Cria o primeiro</Link> para veres as estatísticas.
        </p>
      ) : (
        <>
          <p role="status" className="text-sm text-muted-foreground">
            De {formatDateKey(data!.from)} a {formatDateKey(data!.to)}.
          </p>
          <div className="overflow-x-auto rounded-lg border">
            <table className="w-full text-sm">
              <caption className="sr-only">Ocupação e receita por clube</caption>
              <thead className="border-b bg-muted/50 text-left">
                <tr>
                  <th scope="col" className="px-3 py-2">Clube</th>
                  <th scope="col" className="px-3 py-2 text-right">Horas reservadas</th>
                  <th scope="col" className="px-3 py-2 text-right">Horas abertas</th>
                  <th scope="col" className="px-3 py-2 text-right">Ocupação</th>
                  <th scope="col" className="px-3 py-2 text-right">Receita</th>
                  <th scope="col" className="px-3 py-2 text-right">Confirmadas</th>
                  <th scope="col" className="px-3 py-2 text-right">Faltas</th>
                </tr>
              </thead>
              <tbody>
                {data!.clubs.map((club) => (
                  <tr key={club.clubId} className="border-b">
                    <th scope="row" className="px-3 py-2 text-left font-medium">
                      <Link href={statisticsHref(`/manage/clubs/${club.clubId}/statistics`, range)} className="underline-offset-4 hover:underline">
                        {club.clubName}
                      </Link>
                    </th>
                    <Numbers figures={club} />
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="bg-muted/30 font-medium">
                  <th scope="row" className="px-3 py-2 text-left">Total</th>
                  <Numbers figures={data!.total} />
                </tr>
              </tfoot>
            </table>
          </div>
        </>
      )}
    </div>
  );
}

/** The six numbers of a row, in the order of the columns. */
export function Numbers({ figures }: { figures: { bookedHours: number; openHours: number; occupation: number; revenue: number; confirmedBookings: number; noShows: number } }) {
  return (
    <>
      <td className="px-3 py-2 text-right tabular-nums">{formatHours(figures.bookedHours)}</td>
      <td className="px-3 py-2 text-right tabular-nums">{formatHours(figures.openHours)}</td>
      <td className="px-3 py-2 text-right tabular-nums">{formatOccupation(figures.occupation)}</td>
      <td className="px-3 py-2 text-right tabular-nums">{formatPrice(figures.revenue)}</td>
      <td className="px-3 py-2 text-right tabular-nums">{formatCount(figures.confirmedBookings)}</td>
      <td className="px-3 py-2 text-right tabular-nums">{formatCount(figures.noShows)}</td>
    </>
  );
}
