import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { PageShell } from "@/components/page-shell";
import { RouteLoading } from "@/components/route-loading";
import { ClubStatistics } from "@/features/statistics/components/club-statistics";
import { isGuid } from "@/lib/guid";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages.clubStatistics");
  return {
    title: t("title"),
  };
}

export default async function Page({ params }: PageProps<"/manage/clubs/[id]/statistics">) {
  const { id } = await params;
  // Public ids are GUIDs: anything else is a club that cannot exist, with no need to ask the API.
  if (!isGuid(id)) {
    notFound();
  }
  const t = await getTranslations("pages.clubStatistics");

  return (
    <PageShell title={t("title")}>
      <Suspense fallback={<RouteLoading />}>
        <ClubStatistics clubId={id} />
      </Suspense>
    </PageShell>
  );
}
