import type { Metadata } from "next";
import { Suspense } from "react";
import { PageShell } from "@/components/page-shell";
import { RouteLoading } from "@/components/route-loading";
import { MyBookings } from "@/features/my-bookings/components/my-bookings";

export const metadata: Metadata = {
  title: "As minhas reservas · Reservas de Campos",
};

export default function Page() {
  return (
    <PageShell title="As minhas reservas">
      {/* The tab and page are read from the URL in the browser (`useSearchParams`), which Cache Components wants inside a boundary. */}
      <Suspense fallback={<RouteLoading />}>
        <MyBookings />
      </Suspense>
    </PageShell>
  );
}
