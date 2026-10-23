import { Suspense } from "react";
import { AreaGuard } from "@/components/area-guard";

export default function Layout({ children }: { children: React.ReactNode }) {
  return (
    <Suspense fallback={null}>
      <AreaGuard profile="Owner" title="Área do dono">
        {children}
      </AreaGuard>
    </Suspense>
  );
}
