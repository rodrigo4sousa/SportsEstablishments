import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { useTranslations } from "next-intl";
import { PageShell } from "@/components/page-shell";
import { RouteLoading } from "@/components/route-loading";
import { MyBookings } from "@/features/my-bookings/components/my-bookings";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages.bookings");
  return {
    title: t("title"),
  };
}

export default function Page() {
  const t = useTranslations("pages.bookings");
  return (
    <PageShell title={t("title")}>
      {/* The tab and page are read from the URL in the browser (`useSearchParams`), which Cache Components wants inside a boundary. */}
      <Suspense fallback={<RouteLoading />}>
        <MyBookings />
      </Suspense>
    </PageShell>
  );
}
