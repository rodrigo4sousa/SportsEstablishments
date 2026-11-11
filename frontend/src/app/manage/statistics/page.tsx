import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { useTranslations } from "next-intl";
import { PageShell } from "@/components/page-shell";
import { RouteLoading } from "@/components/route-loading";
import { OwnerStatistics } from "@/features/statistics/components/owner-statistics";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages.statistics");
  return {
    title: t("title"),
  };
}

export default function Page() {
  const t = useTranslations("pages.statistics");
  return (
    <PageShell title={t("title")}>
      {/* The period is read from the URL in the browser (`useSearchParams`), which Cache Components wants inside a boundary. */}
      <Suspense fallback={<RouteLoading />}>
        <OwnerStatistics />
      </Suspense>
    </PageShell>
  );
}
