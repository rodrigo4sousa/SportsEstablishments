import { Suspense } from "react";
import { useTranslations } from "next-intl";
import { AreaGuard } from "@/components/area-guard";

export default function Layout({ children }: { children: React.ReactNode }) {
  const t = useTranslations("areas");
  return (
    <Suspense fallback={null}>
      <AreaGuard profile="Admin" title={t("admin")} width="wide">
        {children}
      </AreaGuard>
    </Suspense>
  );
}
