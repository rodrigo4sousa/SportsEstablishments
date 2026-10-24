import { NextResponse, type NextRequest } from "next/server";
import { REQUEST_PATH_HEADER } from "@/lib/auth/request-path";

/**
 * Only forwards the requested path to the area guards in a request header (US03, deep links). It does not renew
 * tokens, redirect or decide access: the guards and the API do. Whatever a client sends in that header is
 * overwritten, so the value can only come from this proxy; the guards still pass it through `safeNext`.
 */
export function proxy(request: NextRequest) {
  const headers = new Headers(request.headers);
  headers.set(REQUEST_PATH_HEADER, `${request.nextUrl.pathname}${request.nextUrl.search}`);
  return NextResponse.next({ request: { headers } });
}

// Only the areas that have guards. Other pages never receive the header, and the guards ignore a missing one.
export const config = {
  matcher: ["/manage/:path*", "/admin/:path*", "/bookings"],
};
