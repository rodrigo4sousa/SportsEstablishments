import { NextResponse, type NextRequest } from "next/server";
import { REQUEST_PATH_HEADER } from "@/lib/auth/request-path";
import { checkClub, hasImpossibleId, NOT_FOUND_ROUTE, publicClubSlug } from "@/lib/not-found-check";

/**
 * Two jobs, neither of which renews tokens, redirects or decides access (the guards and the API do):
 *
 * 1. Forwards the requested path to the area guards in a request header (US03, deep links). Whatever a client sends in that header is
 *    overwritten, so the value can only come from this proxy; the guards still pass it through `safeNext`.
 * 2. Answers a real 404 for an id that cannot exist and for a public club the API does not have (`lib/not-found-check.ts`): a page
 *    that streams cannot change its status after the first byte, so the check has to happen here, before the page renders. The pages
 *    still call `notFound()` themselves, for what the proxy lets through (an API that is down, a club hidden a moment ago).
 */
export async function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl;
  const headers = new Headers(request.headers);
  headers.set(REQUEST_PATH_HEADER, `${pathname}${search}`);

  // The root not-found page, which Next.js serves with a 404 (the same as a path that matches no route at all).
  const notFound = () => NextResponse.rewrite(new URL(NOT_FOUND_ROUTE, request.url), { request: { headers } });

  if (hasImpossibleId(pathname)) {
    return notFound();
  }

  const slug = publicClubSlug(pathname);
  if (slug !== null) {
    const club = await checkClub(slug, {
      apiUrl: process.env.API_INTERNAL_URL ?? "http://localhost:5080",
      forwardedFor: request.headers.get("x-forwarded-for"),
    });
    if (club === "missing") {
      return notFound();
    }
  }

  return NextResponse.next({ request: { headers } });
}

// The areas that have guards and the pages that take an id. Other pages never receive the header, and the guards ignore a missing one.
// A public club page is checked only for a real page load, not for the prefetches and client-side navigations of Next.js (they carry
// the `rsc` or `next-router-prefetch` header, have no status code that matters, and a search page links to up to 20 clubs).
export const config = {
  matcher: [
    "/manage/:path*",
    "/admin/:path*",
    "/bookings",
    {
      source: "/clubs/:slug",
      missing: [
        { type: "header", key: "rsc" },
        { type: "header", key: "next-router-prefetch" },
      ],
    },
  ],
};
