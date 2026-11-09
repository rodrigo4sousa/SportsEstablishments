import "server-only";
import { notFound } from "next/navigation";
import { cache } from "react";
import type { components } from "@/lib/api/schema";
import { isClubSlug } from "@/features/catalog/slug";
import { toPublicClub, type PublicClub } from "@/features/catalog/types";
import { apiInternalUrl } from "./api-url";
import { visitorIpHeader } from "./visitor-ip";

/**
 * US16: the public page of a club, read on the server from `GET /api/clubs/{slug}`. It is public, so no cookie is
 * sent. Null for a club that does not exist or is hidden (the API answers the same 404 for both); any other failure
 * throws, so the route's error page shows instead of a wrong "not found". Memoised for the request, so the page and its
 * title (`generateMetadata`) share one call to the API.
 */
export const getPublicClub = cache(async (slug: string): Promise<PublicClub | null> => {
  const response = await fetch(`${apiInternalUrl()}/api/clubs/${encodeURIComponent(slug)}`, {
    headers: { Accept: "application/json", ...(await visitorIpHeader()) },
    cache: "no-store",
  });
  if (response.status === 404) {
    return null;
  }
  if (!response.ok) {
    throw new Error(`GET /api/clubs/${slug} failed with ${response.status}`);
  }
  return toPublicClub((await response.json()) as components["schemas"]["PublicClubResponse"]);
});

/** The club for a page, or the not-found page: for a slug that cannot exist, and for a club the API does not have. */
export async function loadPublicClub(slug: string): Promise<PublicClub> {
  if (!isClubSlug(slug)) {
    notFound();
  }
  return (await getPublicClub(slug)) ?? notFound();
}
