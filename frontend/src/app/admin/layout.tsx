import { Suspense } from "react";
import { AreaGuard } from "@/components/area-guard";

export default function Layout({ children }: { children: React.ReactNode }) {
  return (
    <Suspense fallback={null}>
      <AreaGuard profile="Admin" title="Administração">
        {children}
      </AreaGuard>
    </Suspense>
  );
}
