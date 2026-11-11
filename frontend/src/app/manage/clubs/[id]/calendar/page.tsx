import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { PageShell } from "@/components/page-shell";
import { RouteLoading } from "@/components/route-loading";
import { ClubCalendar } from "@/features/calendar/components/club-calendar";
import { isGuid } from "@/lib/guid";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages.calendar");
  return {
    title: t("title"),
  };
}

export default async function Page({ params }: PageProps<"/manage/clubs/[id]/calendar">) {
  const { id } = await params;
  // Public ids are GUIDs: anything else is a club that cannot exist, with no need to ask the API.
  if (!isGuid(id)) {
    notFound();
  }
  const t = await getTranslations("pages.calendar");

  return (
    <PageShell title={t("title")} width="calendar">
      {/* The day, the cancelled toggle and the open booking are read from the URL in the browser (`useSearchParams`), which Cache Components wants inside a boundary. */}
      <Suspense fallback={<RouteLoading />}>
        <ClubCalendar clubId={id} />
      </Suspense>
    </PageShell>
  );
}
