import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { RouteLoading } from "@/components/route-loading";
import { OwnerDetails } from "@/features/admin/components/owner-details";
import { isGuid } from "@/lib/guid";

export const metadata: Metadata = {
  title: "Dono · Administração",
};

/** US29: one owner. The page reads its own data in the browser, where an expired session can be renewed. */
export default async function Page({ params }: PageProps<"/admin/owners/[id]">) {
  const { id } = await params;
  // Public ids are GUIDs: anything else is an owner that cannot exist, with no need to ask the API.
  if (!isGuid(id)) {
    notFound();
  }

  return (
    <section className="mx-auto flex w-full max-w-4xl flex-1 flex-col gap-6 px-4 py-10">
      <h1 className="text-2xl font-semibold tracking-tight">Dono</h1>
      <Suspense fallback={<RouteLoading />}>
        <OwnerDetails ownerId={id} />
      </Suspense>
    </section>
  );
}
