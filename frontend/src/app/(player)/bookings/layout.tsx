import { Suspense } from "react";
import { PlayerGuard } from "@/components/player-guard";
import { RouteLoading } from "@/components/route-loading";

export default function Layout({ children }: { children: React.ReactNode }) {
  return (
    <Suspense fallback={<RouteLoading />}>
      <PlayerGuard fallbackPath="/bookings">{children}</PlayerGuard>
    </Suspense>
  );
}
