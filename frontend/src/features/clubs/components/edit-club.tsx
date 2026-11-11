"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { LoadingRegion } from "@/components/loading-region";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { useDocumentTitle } from "@/lib/document-title";
import { ApiError } from "@/lib/api/client";
import { UNKNOWN_ERROR, useApiErrors } from "@/lib/api/error-messages";
import { PitchSection } from "@/features/pitches/components/pitch-section";
import { clubQueryKey, fetchClub } from "../api";
import { ClubForm } from "./club-form";
import { ClubNotFound } from "./club-not-found";

/** US06: loads one of the owner's clubs and shows its edit form; a 404 (missing, or another owner's) is the not-found page. */
export function EditClub({ clubId }: { clubId: string }) {
  const t = useTranslations("clubs.edit");
  const common = useTranslations("common");
  const apiErrors = useApiErrors();
  const club = useQuery({
    queryKey: clubQueryKey(clubId),
    queryFn: () => fetchClub(clubId),
    // A missing club stays missing, and the form must not be refreshed under the owner's hands while they type.
    retry: false,
    refetchOnWindowFocus: false,
  });

  useDocumentTitle(t("pageTitle"), club.data?.name);

  if (club.isPending) {
    return (
      <LoadingRegion label={t("loading")} className="flex flex-col gap-6">
        <Skeleton className="h-7 w-48" />
        <Skeleton className="h-11 w-full sm:w-80" />
        <Skeleton className="h-64 w-full" />
      </LoadingRegion>
    );
  }

  if (club.isError) {
    if (club.error instanceof ApiError && club.error.status === 404) {
      return <ClubNotFound />;
    }
    const code = club.error instanceof ApiError ? club.error.code : UNKNOWN_ERROR;
    return (
      <div role="alert" className="flex flex-col items-start gap-3">
        <p>{apiErrors.message(code)}</p>
        <Button type="button" variant="outline" onClick={() => void club.refetch()}>
          {common("retry")}
        </Button>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-10">
      <div className="flex flex-col gap-4">
        {/* The page's title says what it is; this says which club (UX-012). */}
        <p className="text-lg font-medium">{club.data.name}</p>
        <div className="flex flex-wrap gap-3">
          <Button asChild size="cta">
            <Link href={`/manage/clubs/${club.data.id}/calendar`}>{t("calendar")}</Link>
          </Button>
          <Button asChild variant="outline" size="cta">
            <Link href={`/manage/clubs/${club.data.id}/statistics`}>{t("statistics")}</Link>
          </Button>
        </div>
      </div>
      <section aria-labelledby="club-data-heading" className="flex flex-col gap-4">
        <h2 id="club-data-heading" className="text-xl font-semibold tracking-tight">
          {t("data")}
        </h2>
        <ClubForm club={club.data} />
      </section>
      <PitchSection clubId={club.data.id} />
    </div>
  );
}
