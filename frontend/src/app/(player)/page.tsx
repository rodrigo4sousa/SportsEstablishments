import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { Suspense } from "react";
import { PageShell } from "@/components/page-shell";
import { RouteLoading } from "@/components/route-loading";
import { ClubSearch } from "@/features/catalog/components/club-search";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages.search");
  return {
    title: t("metaTitle"),
  };
}

export default function Page() {
  const t = useTranslations("pages.search");
  return (
    <PageShell
      title={t("title")}
      description={
        <div className="flex flex-col gap-2 text-muted-foreground">
          <p>{t("intro")}</p>
          <p>
            {t.rich("noAccount", {
              link: (chunks) => (
                <Link href="/register/owner" className="text-foreground underline underline-offset-4">
                  {chunks}
                </Link>
              ),
            })}
          </p>
        </div>
      }
    >
      {/* The search reads the URL in the browser (`useSearchParams`), which Cache Components wants inside a boundary. */}
      <Suspense fallback={<RouteLoading />}>
        <ClubSearch />
      </Suspense>
    </PageShell>
  );
}
