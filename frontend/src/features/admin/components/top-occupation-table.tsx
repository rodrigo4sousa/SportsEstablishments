"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { LoadingRegion } from "@/components/loading-region";
import { ScrollRegion } from "@/components/scroll-region";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import { useFormats } from "@/i18n/formats";
import { fetchTopOccupation, topOccupationQueryKey, type PeriodQuery } from "../api";
import { useAdminFailureMessage } from "../period-labels";

/**
 * US27: the 10 clubs with the highest occupation in the period, with the formula of the owner's statistics (US14): booked hours over
 * the bookable hours. A club with nothing open in the period is not in the list. Shows the club, its owner's name and its locality,
 * and nothing about players. The period is the one shared with the charts.
 */
export function TopOccupationTable({ period }: { period: PeriodQuery }) {
  const t = useTranslations("admin.top");
  const common = useTranslations("common");
  const formats = useFormats();
  const failureMessage = useAdminFailureMessage();
  const top = useQuery({
    queryKey: topOccupationQueryKey(period),
    queryFn: () => fetchTopOccupation(period),
    placeholderData: keepPreviousData,
  });

  if (top.isError) {
    return (
      <div role="alert" className="flex flex-col items-start gap-3">
        <p>{failureMessage(top.error)}</p>
        <Button type="button" variant="outline" onClick={() => void top.refetch()}>
          {common("retry")}
        </Button>
      </div>
    );
  }
  if (top.isPending) {
    return (
      <LoadingRegion label={t("loading")} className="flex flex-col gap-2">
        {[0, 1, 2, 3, 4].map((key) => (
          <Skeleton key={key} className="h-8 w-full" />
        ))}
      </LoadingRegion>
    );
  }

  const data = top.data;
  if (data.clubs.length === 0) {
    return (
      <p role="status" className="rounded-md border border-dashed px-4 py-6 text-muted-foreground">
        {t("empty", { from: formats.dateKey(data.from), to: formats.dateKey(data.to) })}
      </p>
    );
  }

  return (
    <div className="flex flex-col gap-3">
      <p role="status" className="text-sm text-muted-foreground">
        {t("range", { from: formats.dateKey(data.from), to: formats.dateKey(data.to) })}
      </p>
      {/* Not the section's own name (axe: landmark-unique). */}
      <ScrollRegion label={t("region")}>
        <table role="table" className="w-full text-sm max-sm:block">
          <caption className="sr-only">{t("caption")}</caption>
          <thead role="rowgroup" className="border-b bg-muted/50 text-left max-sm:sr-only">
            <tr role="row">
              <th role="columnheader" scope="col" className="px-3 py-2 text-right">#</th>
              <th role="columnheader" scope="col" className="px-3 py-2">{t("club")}</th>
              <th role="columnheader" scope="col" className="px-3 py-2">{t("owner")}</th>
              <th role="columnheader" scope="col" className="px-3 py-2">{t("locality")}</th>
              <th role="columnheader" scope="col" className="px-3 py-2 text-right">{t("occupation")}</th>
              <th role="columnheader" scope="col" className="px-3 py-2 text-right">{t("bookedHours")}</th>
              <th role="columnheader" scope="col" className="px-3 py-2 text-right">{t("openHours")}</th>
            </tr>
          </thead>
          <tbody role="rowgroup" className="max-sm:block max-sm:p-3 max-sm:pb-0">
            {data.clubs.map((club, index) => (
              <tr key={club.clubId} role="row" className="border-b last:border-b-0 max-sm:mb-3 max-sm:block max-sm:rounded-md max-sm:border max-sm:p-3 max-sm:last:border-b">
                {/* The rank is its own column from sm up; on a phone it leads the card's title. */}
                <td role="cell" className="px-3 py-2 text-right tabular-nums text-muted-foreground max-sm:hidden">{index + 1}</td>
                <th role="rowheader" scope="row" className="px-3 py-2 text-left font-medium max-sm:block max-sm:px-0 max-sm:pt-0 max-sm:text-base">
                  <span aria-hidden="true" className="tabular-nums text-muted-foreground sm:hidden">{index + 1}. </span>
                  <Link href={`/clubs/${club.slug}`} className="underline underline-offset-4 decoration-muted-foreground hover:decoration-foreground">
                    {club.clubName}
                  </Link>
                </th>
                <td role="cell" data-label={t("owner")} className="px-3 py-2 max-sm:flex max-sm:justify-between max-sm:gap-3 max-sm:px-0 max-sm:py-0.5 max-sm:text-left max-sm:before:text-muted-foreground max-sm:before:content-[attr(data-label)]">{club.ownerName}</td>
                <td role="cell" data-label={t("locality")} className="px-3 py-2 max-sm:flex max-sm:justify-between max-sm:gap-3 max-sm:px-0 max-sm:py-0.5 max-sm:text-left max-sm:before:text-muted-foreground max-sm:before:content-[attr(data-label)]">{club.locality}</td>
                <td role="cell" data-label={t("occupation")} className="px-3 py-2 text-right tabular-nums max-sm:flex max-sm:justify-between max-sm:gap-3 max-sm:px-0 max-sm:py-0.5 max-sm:text-left max-sm:before:text-muted-foreground max-sm:before:content-[attr(data-label)]">{formats.occupation(club.occupation)}</td>
                <td role="cell" data-label={t("bookedHours")} className="px-3 py-2 text-right tabular-nums max-sm:flex max-sm:justify-between max-sm:gap-3 max-sm:px-0 max-sm:py-0.5 max-sm:text-left max-sm:before:text-muted-foreground max-sm:before:content-[attr(data-label)]">{formats.hours(club.bookedHours)}</td>
                <td role="cell" data-label={t("openHours")} className="px-3 py-2 text-right tabular-nums max-sm:flex max-sm:justify-between max-sm:gap-3 max-sm:px-0 max-sm:py-0.5 max-sm:text-left max-sm:before:text-muted-foreground max-sm:before:content-[attr(data-label)]">{formats.hours(club.openHours)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </ScrollRegion>
    </div>
  );
}
