"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { formatDateKey, formatDateKeyMonth, formatDateKeyShort } from "@/lib/datetime";
import { bookingsOverTimeQueryKey, fetchBookingsOverTime, type ChartQuery } from "../api";
import type { Granularity, PeriodBucket } from "../types";

const GRANULARITIES: { value: Granularity; label: string }[] = [
  { value: "day", label: "Dia" },
  { value: "week", label: "Semana" },
  { value: "month", label: "Mês" },
];

const number = new Intl.NumberFormat("pt-PT");

/** The short axis label of a period: the day, the Monday of the week, the month. */
function axisLabel(granularity: Granularity, bucket: PeriodBucket): string {
  return granularity === "month" ? formatDateKeyMonth(bucket.start) : formatDateKeyShort(bucket.start);
}

/** The full name of a period, for the tooltip and the table: a day, "de … a …" for a week, a month. */
function periodLabel(granularity: Granularity, bucket: PeriodBucket): string {
  if (granularity === "month") return formatDateKeyMonth(bucket.start);
  if (granularity === "week") return `${formatDateKey(bucket.start)} a ${formatDateKey(bucket.end)}`;
  return formatDateKey(bucket.start);
}

/** The API's own message for a rejected range (a field error in Portuguese), else the one for its code. */
function failureMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const field = Object.values(error.fieldErrors).flat()[0];
    return field ?? errorMessage(error.code);
  }
  return errorMessage("unknown_error");
}

type BookingsChartProps = {
  query: ChartQuery;
  onChange: (patch: Partial<ChartQuery>) => void;
};

/**
 * US25: bookings per day, week (from Monday) or month, by the Lisbon date they were made. The range is the last 30 days
 * until the admin picks one; the API widens it to whole periods and says which, and the date fields show that. Cancelled
 * bookings are left out unless asked for. The same numbers are in a table for screen readers.
 */
export function BookingsChart({ query, onChange }: BookingsChartProps) {
  const bookings = useQuery({
    queryKey: bookingsOverTimeQueryKey(query),
    queryFn: () => fetchBookingsOverTime(query),
    placeholderData: keepPreviousData,
  });
  const data = bookings.data;
  const granularity = data?.granularity ?? query.granularity;
  const points = (data?.buckets ?? []).map((bucket) => ({
    key: bucket.start,
    axis: axisLabel(granularity, bucket),
    label: periodLabel(granularity, bucket),
    count: bucket.count,
  }));

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-end gap-4">
        <div role="group" aria-label="Agrupar por" className="flex rounded-md border">
          {GRANULARITIES.map((option) => (
            <button
              key={option.value}
              type="button"
              aria-pressed={query.granularity === option.value}
              onClick={() => onChange({ granularity: option.value })}
              className={
                query.granularity === option.value
                  ? "bg-primary px-3 py-1.5 text-sm text-primary-foreground first:rounded-l-md last:rounded-r-md"
                  : "px-3 py-1.5 text-sm text-muted-foreground first:rounded-l-md last:rounded-r-md hover:text-foreground"
              }
            >
              {option.label}
            </button>
          ))}
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="chart-from">De</Label>
          <Input
            id="chart-from"
            type="date"
            value={query.from ?? data?.from ?? ""}
            onChange={(event) => onChange({ from: event.target.value || null })}
          />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="chart-to">Até</Label>
          <Input
            id="chart-to"
            type="date"
            value={query.to ?? data?.to ?? ""}
            onChange={(event) => onChange({ to: event.target.value || null })}
          />
        </div>
        <Button type="button" variant="outline" disabled={query.from === null && query.to === null} onClick={() => onChange({ from: null, to: null })}>
          Últimos 30 dias
        </Button>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={query.includeCancelled}
            onChange={(event) => onChange({ includeCancelled: event.target.checked })}
            className="size-4"
          />
          Incluir canceladas
        </label>
      </div>

      {bookings.isError ? (
        <div role="alert" className="flex flex-col items-start gap-3">
          <p>{failureMessage(bookings.error)}</p>
          <Button type="button" variant="outline" onClick={() => void bookings.refetch()}>
            Tentar novamente
          </Button>
        </div>
      ) : bookings.isPending ? (
        <p role="status" className="text-muted-foreground">
          A carregar o gráfico…
        </p>
      ) : (
        <>
          <p role="status" className="text-sm text-muted-foreground">
            {data!.total === 1 ? "1 reserva" : `${number.format(data!.total)} reservas`} feitas entre {formatDateKey(data!.from)} e{" "}
            {formatDateKey(data!.to)}
            {data!.includeCancelled ? ", com as canceladas" : ""}.
          </p>
          {data!.total === 0 ? (
            <p className="rounded-md border border-dashed px-4 py-3 text-sm text-muted-foreground">Ainda não houve reservas neste intervalo.</p>
          ) : null}
          <div role="img" aria-label="Gráfico de barras com as reservas feitas em cada período" className="h-72 w-full">
            <ResponsiveContainer width="100%" height="100%" initialDimension={{ width: 640, height: 288 }}>
              <BarChart data={points} margin={{ top: 8, right: 8, bottom: 8, left: 0 }}>
                <CartesianGrid vertical={false} strokeDasharray="3 3" />
                <XAxis dataKey="axis" tickLine={false} interval="preserveStartEnd" minTickGap={16} fontSize={12} />
                <YAxis allowDecimals={false} width={40} fontSize={12} />
                <Tooltip
                  formatter={(value) => [number.format(Number(value)), "Reservas"]}
                  labelFormatter={(_, payload) => payload?.[0]?.payload?.label ?? ""}
                />
                <Bar dataKey="count" name="Reservas" fill="var(--primary)" radius={[3, 3, 0, 0]} isAnimationActive={false} />
              </BarChart>
            </ResponsiveContainer>
          </div>
          <table className="sr-only">
            <caption>Reservas feitas em cada período</caption>
            <thead>
              <tr>
                <th scope="col">Período</th>
                <th scope="col">Reservas</th>
              </tr>
            </thead>
            <tbody>
              {points.map((point) => (
                <tr key={point.key}>
                  <th scope="row">{point.label}</th>
                  <td>{point.count}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </div>
  );
}
