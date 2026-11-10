import { isIP } from "node:net";
import { isClubSlug } from "@/features/catalog/slug";
import { isGuid } from "@/lib/guid";

/**
 * Real 404 statuses for unknown resources, decided in `proxy.ts` before the page streams.
 *
 * A page that shows a `loading.tsx` or a Suspense fallback starts streaming first, which fixes the status at 200; a `notFound()` after
 * that renders the not-found page but keeps the 200 (Next.js adds `noindex`, which keeps it out of search results, but it is still a
 * "soft 404" for crawlers, links and analytics). The documented way to get the 404 is to check before the response body starts: in Proxy
 * (docs: file conventions, `loading`, "Status codes"). The pages keep their own `notFound()`, for the cases the proxy lets through.
 */

/** Next.js's own route for the root not-found page: rewriting to it answers 404 (a rewrite of a page to itself keeps its 200 on the production server). */
export const NOT_FOUND_ROUTE = "/_not-found";

/** The slug of a public club page (`/clubs/<slug>`, exactly one segment), or null for any other path. */
export function publicClubSlug(pathname: string): string | null {
  const match = /^\/clubs\/([^/]+)\/?$/.exec(pathname);
  if (!match) {
    return null;
  }
  try {
    return decodeURIComponent(match[1]);
  } catch {
    return "%"; // a malformed escape: not a slug, so a 404
  }
}

/** The id segment of the owner and admin pages that take a GUID, or null for any other path. */
export function guidSegment(pathname: string): string | null {
  const match = /^\/(?:manage\/clubs\/([^/]+)(?:\/(?:calendar|statistics))?|admin\/owners\/([^/]+))\/?$/.exec(pathname);
  const raw = match?.[1] ?? match?.[2];
  // `/manage/clubs/new` is a page of its own, not a club id.
  if (raw === undefined || pathname.replace(/\/$/, "") === "/manage/clubs/new") {
    return null;
  }
  try {
    return decodeURIComponent(raw);
  } catch {
    return "%";
  }
}

/** True when the path is one of those routes with an id that cannot exist, whatever the API would say. */
export function hasImpossibleId(pathname: string): boolean {
  const slug = publicClubSlug(pathname);
  if (slug !== null) {
    return !isClubSlug(slug);
  }
  const id = guidSegment(pathname);
  return id !== null && !isGuid(id);
}

export type ClubCheck = "exists" | "missing" | "unknown";

/** How long a club that answered 200 is trusted without asking again (the proxy runs in front of every page load). */
export const KNOWN_CLUB_TTL_MS = 60_000;
const MAX_KNOWN_CLUBS = 500;
const API_TIMEOUT_MS = 3_000;

const knownClubs = new Map<string, number>();

/** Forgets the clubs seen so far (tests). */
export function resetKnownClubs(): void {
  knownClubs.clear();
}

/** The address the API should see for this visitor: the last entry of `X-Forwarded-For`, and only if it is an IP (as `lib/server/visitor-ip.ts`). */
export function visitorAddress(forwardedFor: string | null): string | undefined {
  const address = forwardedFor?.split(",").at(-1)?.trim() ?? "";
  return isIP(address) === 0 ? undefined : address;
}

export type ClubCheckDeps = {
  apiUrl: string;
  forwardedFor?: string | null;
  fetchFn?: typeof fetch;
  now?: () => number;
};

/**
 * Does the API have this public club? "missing" only for a 404 (the API answers the same for a club that does not exist and one that is
 * hidden). Anything else that goes wrong (the API is asleep or slow, a 5xx, no network) is "unknown": the page is let through and decides
 * for itself, so the proxy can never turn a working page into an error. A club that exists is remembered for a minute, so a visitor
 * clicking around does not cost the API a second request per page.
 */
export async function checkClub(slug: string, { apiUrl, forwardedFor = null, fetchFn = fetch, now = Date.now }: ClubCheckDeps): Promise<ClubCheck> {
  const knownUntil = knownClubs.get(slug);
  if (knownUntil !== undefined && knownUntil > now()) {
    return "exists";
  }

  const address = visitorAddress(forwardedFor);
  try {
    const response = await fetchFn(`${apiUrl}/api/clubs/${encodeURIComponent(slug)}`, {
      headers: { Accept: "application/json", ...(address ? { "X-Forwarded-For": address } : {}) },
      cache: "no-store",
      signal: AbortSignal.timeout(API_TIMEOUT_MS),
    });
    await response.body?.cancel();
    if (response.status === 404) {
      knownClubs.delete(slug);
      return "missing";
    }
    if (!response.ok) {
      return "unknown";
    }
  } catch {
    return "unknown";
  }

  if (knownClubs.size >= MAX_KNOWN_CLUBS) {
    knownClubs.clear();
  }
  knownClubs.set(slug, now() + KNOWN_CLUB_TTL_MS);
  return "exists";
}
