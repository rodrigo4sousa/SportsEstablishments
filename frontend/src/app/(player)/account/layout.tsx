import { Suspense } from "react";
import { AccountGuard } from "@/components/account-guard";
import { RouteLoading } from "@/components/route-loading";

export default function Layout({ children }: { children: React.ReactNode }) {
  return (
    <Suspense fallback={<RouteLoading />}>
      <AccountGuard path="/account">{children}</AccountGuard>
    </Suspense>
  );
}
