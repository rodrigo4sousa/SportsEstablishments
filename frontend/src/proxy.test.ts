import { NextRequest } from "next/server";
import { describe, expect, it } from "vitest";
import { config, proxy } from "./proxy";

/** What the page and the layouts will see as request headers: Next.js reports them as `x-middleware-request-*`. */
function forwardedHeader(response: Response, name: string) {
  return response.headers.get(`x-middleware-request-${name}`);
}

function requestFor(url: string, headers: Record<string, string> = {}) {
  return new NextRequest(`http://localhost:3000${url}`, { headers });
}

describe("proxy", () => {
  it("forwards the path and query of the page being requested", async () => {
    const response = await proxy(requestFor("/manage/clubs/0199f7a0-0000-7000-8000-0000000000aa?tab=horarios"));

    expect(forwardedHeader(response, "x-pathname")).toBe("/manage/clubs/0199f7a0-0000-7000-8000-0000000000aa?tab=horarios");
  });

  it("forwards a path without a query as it is", async () => {
    expect(forwardedHeader(await proxy(requestFor("/admin")), "x-pathname")).toBe("/admin");
  });

  it("always overwrites a value sent by the client", async () => {
    const response = await proxy(requestFor("/manage/clubs/new", { "x-pathname": "/admin/secrets" }));

    expect(forwardedHeader(response, "x-pathname")).toBe("/manage/clubs/new");
  });

  it("overwrites a client value written in another casing too", async () => {
    const response = await proxy(requestFor("/manage", { "X-PathName": "//evil.example" }));

    expect(forwardedHeader(response, "x-pathname")).toBe("/manage");
  });

  it("keeps the other request headers, including the cookies", async () => {
    const response = await proxy(requestFor("/manage", { cookie: "access_token=abc", "accept-language": "pt-PT" }));

    // The headers listed as overridden are the full set of request headers the page receives.
    const overridden = (response.headers.get("x-middleware-override-headers") ?? "").split(",");
    expect(overridden).toEqual(expect.arrayContaining(["cookie", "accept-language", "x-pathname"]));
    expect(forwardedHeader(response, "cookie")).toBe("access_token=abc");
  });

  it("does not redirect, rewrite or decide anything", async () => {
    const response = await proxy(requestFor("/manage"));

    expect(response.status).toBe(200);
    expect(response.headers.get("location")).toBeNull();
    expect(response.headers.get("x-middleware-rewrite")).toBeNull();
    expect(response.headers.get("set-cookie")).toBeNull();
  });

  it("forwards the tab and page of the player's bookings, so a visitor comes back to them after signing in", async () => {
    expect(forwardedHeader(await proxy(requestFor("/bookings?tab=past&page=2")), "x-pathname")).toBe("/bookings?tab=past&page=2");
  });

  it("runs for the areas with guards, the bookings page, and a real load of a public club page (not prefetches)", async () => {
    expect(config.matcher).toEqual([
      "/manage/:path*",
      "/admin/:path*",
      "/bookings",
      { source: "/clubs/:slug", missing: [{ type: "header", key: "rsc" }, { type: "header", key: "next-router-prefetch" }] },
    ]);
  });
});
