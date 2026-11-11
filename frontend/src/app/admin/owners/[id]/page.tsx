import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { PageShell } from "@/components/page-shell";
import { RouteLoading } from "@/components/route-loading";
import { OwnerDetails } from "@/features/admin/components/owner-details";
import { isGuid } from "@/lib/guid";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages.owner");
  return {
    title: t("metaTitle"),
  };
}

/** US29: one owner. The page reads its own data in the browser, where an expired session can be renewed. */
export default async function Page({ params }: PageProps<"/admin/owners/[id]">) {
  const { id } = await params;
  // Public ids are GUIDs: anything else is an owner that cannot exist, with no need to ask the API.
  if (!isGuid(id)) {
    notFound();
  }
  const t = await getTranslations("pages.owner");

  return (
    <PageShell title={t("title")} width="wide">
      <Suspense fallback={<RouteLoading />}>
        <OwnerDetails ownerId={id} />
      </Suspense>
    </PageShell>
  );
}
