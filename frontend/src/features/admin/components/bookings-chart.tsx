"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { ChartDataTable } from "./chart-data-table";
import { LoadingRegion } from "@/components/loading-region";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import { useFormats } from "@/i18n/formats";
import { bookingsOverTimeQueryKey, fetchBookingsOverTime, type ChartQuery } from "../api";
import { axisLabel, periodLabel, useAdminFailureMessage } from "../period-labels";
import type { Granularity } from "../types";

const GRANULARITIES = [
  { value: "day", label: "day" },
  { value: "week", label: "week" },
  { value: "month", label: "month" },
] as const satisfies ReadonlyArray<{ value: Granularity; label: string }>;

type BookingsChartProps = {
  query: ChartQuery;
  onChange: (patch: Partial<ChartQuery>) => void;
};

/**
 * US25: bookings per day, week (from Monday) or month, by the Lisbon date they were made. The period (shared with the registrations
 * and the top of the clubs, US28 and US27) is chosen above, in `PeriodFilter`; the API widens it to whole periods and the
 * summary says which. Cancelled bookings are left out unless asked for. The same numbers are in a table for screen readers.
 */
export function BookingsChart({ query, onChange }: BookingsChartProps) {
  const t = useTranslations("admin.chart");
  const common = useTranslations("common");
  const formats = useFormats();
  const failureMessage = useAdminFailureMessage();
  const weekRange = (from: string, to: string) => t("weekRange", { from, to });
  const bookings = useQuery({
    queryKey: bookingsOverTimeQueryKey(query),
    queryFn: () => fetchBookingsOverTime(query),
    placeholderData: keepPreviousData,
  });
  const data = bookings.data;
  const granularity = data?.granularity ?? query.granularity;
  const points = (data?.buckets ?? []).map((bucket) => ({
    key: bucket.start,
    axis: axisLabel(formats, granularity, bucket),
    label: periodLabel(formats, weekRange, granularity, bucket),
    count: bucket.count,
  }));

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-end gap-4">
        <div role="group" aria-label={t("groupBy")} className="flex rounded-md border">
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
              {t(option.label)}
            </button>
          ))}
        </div>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={query.includeCancelled}
            onChange={(event) => onChange({ includeCancelled: event.target.checked })}
            className="size-4"
          />
          {t("includeCancelled")}
        </label>
      </div>

      {bookings.isError ? (
        <div role="alert" className="flex flex-col items-start gap-3">
          <p>{failureMessage(bookings.error)}</p>
          <Button type="button" variant="outline" onClick={() => void bookings.refetch()}>
            {common("retry")}
          </Button>
        </div>
      ) : bookings.isPending ? (
        <LoadingRegion label={t("loading")}>
          <Skeleton className="h-72 w-full" />
        </LoadingRegion>
      ) : (
        <>
          <p role="status" className="text-sm text-muted-foreground">
            {t(data!.includeCancelled ? "summaryWithCancelled" : "summary", {
              count: data!.total,
              from: formats.dateKey(data!.from),
              to: formats.dateKey(data!.to),
            })}
          </p>
          {data!.total === 0 ? (
            <p className="rounded-md border border-dashed px-4 py-3 text-sm text-muted-foreground">{t("empty")}</p>
          ) : null}
          <div role="img" aria-label={t("image")} className="h-72 w-full">
            <ResponsiveContainer width="100%" height="100%" initialDimension={{ width: 640, height: 288 }}>
              <BarChart data={points} margin={{ top: 8, right: 8, bottom: 8, left: 0 }}>
                <CartesianGrid vertical={false} strokeDasharray="3 3" />
                <XAxis dataKey="axis" tickLine={false} interval="preserveStartEnd" minTickGap={16} fontSize={12} />
                <YAxis allowDecimals={false} width={40} fontSize={12} />
                <Tooltip
                  formatter={(value) => [formats.count(Number(value)), t("bookings")]}
                  labelFormatter={(_, payload) => payload?.[0]?.payload?.label ?? ""}
                />
                <Bar dataKey="count" name={t("bookings")} fill="var(--primary)" radius={[3, 3, 0, 0]} isAnimationActive={false} />
              </BarChart>
            </ResponsiveContainer>
          </div>
          <ChartDataTable
            caption={t("tableCaption")}
            firstColumn={t("periodColumn")}
            series={[t("bookings")]}
            rows={points.map((point) => ({ key: point.key, label: point.label, values: [point.count] }))}
          />
        </>
      )}
    </div>
  );
}
