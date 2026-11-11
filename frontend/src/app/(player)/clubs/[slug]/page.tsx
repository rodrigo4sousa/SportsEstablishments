import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { PageShell } from "@/components/page-shell";
import { RouteLoading } from "@/components/route-loading";
import { ClubBooking } from "@/features/booking/components/club-booking";
import { ClubContacts, ClubPitches } from "@/features/catalog/components/club-details";
import { isClubSlug } from "@/features/catalog/slug";
import { getPublicClub, loadPublicClub } from "@/lib/server/public-club";

/** UX-011: the tab says which club this is. A club that does not exist gets the not-found page, titled to match. */
export async function generateMetadata({ params }: PageProps<"/clubs/[slug]">): Promise<Metadata> {
  const { slug } = await params;
  const club = isClubSlug(slug) ? await getPublicClub(slug) : null;
  const t = await getTranslations("pages.club");
  return { title: club ? club.name : t("notFoundTitle") };
}

export default async function Page({ params }: PageProps<"/clubs/[slug]">) {
  const { slug } = await params;
  const club = await loadPublicClub(slug);
  const t = await getTranslations("pages.club");

  // UX-010: the reason to be here, the slots, comes first; then what the pitches are, then how to reach the club.
  return (
    <PageShell
      title={club.name}
      before={
        <p>
          <Link href="/" className="inline-flex min-h-11 items-center text-sm text-muted-foreground underline underline-offset-4 sm:min-h-0">
            {t("back")}
          </Link>
        </p>
      }
      description={
        <p className="text-muted-foreground">
          {club.address}, {club.locality}
        </p>
      }
    >
      {/* The booking part reads the URL in the browser (`useSearchParams`), which Cache Components wants inside a boundary. */}
      <Suspense fallback={<RouteLoading />}>
        <ClubBooking club={club} />
      </Suspense>
      <ClubPitches club={club} />
      <ClubContacts club={club} />
    </PageShell>
  );
}
