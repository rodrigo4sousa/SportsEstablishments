"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, type ReactNode } from "react";
import { EmptyState } from "@/components/empty-state";
import { LoadingRegion } from "@/components/loading-region";
import { PeriodFilter } from "@/components/period-filter";
import { ScrollRegion } from "@/components/scroll-region";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
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
        <StatisticsSkeleton />
      ) : data!.clubs.length === 0 ? (
        <EmptyState
          title="Ainda não tens clubes."
          description="Cria o primeiro para veres as estatísticas."
          action={
            <Button asChild variant="outline">
              <Link href="/manage/clubs/new">Criar o primeiro clube</Link>
            </Button>
          }
        />
      ) : (
        <>
          <p role="status" className="text-sm text-muted-foreground">
            De {formatDateKey(data!.from)} a {formatDateKey(data!.to)}.
          </p>
          <StatisticsTable
            caption="Ocupação e receita por clube"
            firstColumn="Clube"
            rows={data!.clubs.map((club) => ({
              key: club.clubId,
              head: (
                <Link href={statisticsHref(`/manage/clubs/${club.clubId}/statistics`, range)} className="underline-offset-4 hover:underline">
                  {club.clubName}
                </Link>
              ),
              figures: club,
            }))}
            total={data!.total}
          />
        </>
      )}
    </div>
  );
}

type Figures = { bookedHours: number; openHours: number; occupation: number; revenue: number; confirmedBookings: number; noShows: number };

/** The six numbers of a row, in the order of the columns; each cell carries its column's name for the phone layout. */
export function Numbers({ figures }: { figures: Figures }) {
  const cell =
    "px-3 py-2 text-right tabular-nums max-sm:flex max-sm:justify-between max-sm:px-0 max-sm:py-0.5 max-sm:text-left max-sm:before:text-muted-foreground max-sm:before:content-[attr(data-label)]";
  return (
    <>
      <td role="cell" data-label="Horas reservadas" className={cell}>{formatHours(figures.bookedHours)}</td>
      <td role="cell" data-label="Horas abertas" className={cell}>{formatHours(figures.openHours)}</td>
      <td role="cell" data-label="Ocupação" className={cell}>{formatOccupation(figures.occupation)}</td>
      <td role="cell" data-label="Receita" className={cell}>{formatPrice(figures.revenue)}</td>
      <td role="cell" data-label="Confirmadas" className={cell}>{formatCount(figures.confirmedBookings)}</td>
      <td role="cell" data-label="Faltas" className={cell}>{formatCount(figures.noShows)}</td>
    </>
  );
}

type StatisticsTableProps = {
  caption: string;
  /** The header of the first column: «Clube» or «Campo». */
  firstColumn: string;
  rows: { key: string; head: ReactNode; figures: Figures }[];
  total: Figures;
};

/**
 * The table of both statistics screens. From `sm` up it is a table. On a phone seven columns do not fit, and a table that scrolls
 * sideways hides the revenue (UX-023), so each row becomes a card with the name on top and the six figures under it, each with its
 * column's name beside it. It is the same elements, restyled, with explicit table roles so assistive technology still reads a table
 * when the display is no longer a table.
 */
export function StatisticsTable({ caption, firstColumn, rows, total }: StatisticsTableProps) {
  const headers = ["Horas reservadas", "Horas abertas", "Ocupação", "Receita", "Confirmadas", "Faltas"];
  const row = "max-sm:mb-3 max-sm:block max-sm:rounded-md max-sm:border max-sm:p-3";
  const head = "px-3 py-2 text-left max-sm:block max-sm:px-0 max-sm:pt-0 max-sm:text-base";
  return (
    <ScrollRegion label={caption}>
      <table role="table" className="w-full text-sm max-sm:block">
        <caption className="sr-only">{caption}</caption>
        <thead role="rowgroup" className="border-b bg-muted/50 text-left max-sm:sr-only">
          <tr role="row">
            <th role="columnheader" scope="col" className="px-3 py-2">{firstColumn}</th>
            {headers.map((header) => (
              <th key={header} role="columnheader" scope="col" className="px-3 py-2 text-right">{header}</th>
            ))}
          </tr>
        </thead>
        <tbody role="rowgroup" className="max-sm:block max-sm:p-3 max-sm:pb-0">
          {rows.map((item) => (
            <tr key={item.key} role="row" className={`border-b ${row}`}>
              <th role="rowheader" scope="row" className={`${head} font-medium`}>{item.head}</th>
              <Numbers figures={item.figures} />
            </tr>
          ))}
        </tbody>
        <tfoot role="rowgroup" className="max-sm:block max-sm:p-3 max-sm:pt-0">
          <tr role="row" className={`bg-muted/30 font-medium ${row}`}>
            <th role="rowheader" scope="row" className={head}>Total</th>
            <Numbers figures={total} />
          </tr>
        </tfoot>
      </table>
    </ScrollRegion>
  );
}

/** The shape of the loaded statistics (a note and a block of rows), so the page does not jump when it arrives. */
export function StatisticsSkeleton() {
  return (
    <LoadingRegion label="A carregar as estatísticas…" className="flex flex-col gap-4">
      <Skeleton className="h-4 w-56" />
      <Skeleton className="h-64 w-full" />
    </LoadingRegion>
  );
}
