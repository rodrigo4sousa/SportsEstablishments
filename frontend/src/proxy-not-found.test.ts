import { http, HttpResponse } from "msw";
import { NextRequest } from "next/server";
import { beforeEach, describe, expect, it } from "vitest";
import { resetKnownClubs } from "@/lib/not-found-check";
import { server } from "@/test/msw";
import { proxy } from "./proxy";

const API = "http://localhost:5080";
const GUID = "0199f7a0-0000-7000-8000-0000000000aa";

function requestFor(url: string, headers: Record<string, string> = {}) {
  return new NextRequest(`http://localhost:3000${url}`, { headers });
}

/** A proxy answer that rewrites to the not-found route, which Next.js serves with a 404. */
function expectNotFound(response: Response) {
  expect(response.headers.get("x-middleware-rewrite")).toBe("http://localhost:3000/_not-found");
}

function expectLetThrough(response: Response) {
  expect(response.status).toBe(200);
  expect(response.headers.get("x-middleware-rewrite")).toBeNull();
}

describe("proxy: a real 404 for resources that do not exist", () => {
  beforeEach(() => resetKnownClubs());

  it("answers 404 for a club the API does not have", async () => {
    server.use(http.get(`${API}/api/clubs/:slug`, () => new HttpResponse(null, { status: 404 })));

    expectNotFound(await proxy(requestFor("/clubs/clube-que-nao-existe")));
  });

  it("lets a club the API has through, as before", async () => {
    server.use(http.get(`${API}/api/clubs/clube-da-foz`, () => HttpResponse.json({ slug: "clube-da-foz" })));

    expectLetThrough(await proxy(requestFor("/clubs/clube-da-foz")));
  });

  it("does not ask the API about a slug that cannot exist", async () => {
    // No handler: an unhandled request would fail the test.
    expectNotFound(await proxy(requestFor("/clubs/Clube_Inexistente!")));
    expectNotFound(await proxy(requestFor("/clubs/%E0%A4%A")));
  });

  it("lets the page decide when the API fails: a 500, an unreachable API", async () => {
    server.use(http.get(`${API}/api/clubs/:slug`, () => new HttpResponse(null, { status: 500 })));
    expectLetThrough(await proxy(requestFor("/clubs/clube-da-foz")));

    server.use(http.get(`${API}/api/clubs/:slug`, () => HttpResponse.error()));
    expectLetThrough(await proxy(requestFor("/clubs/outro-clube")));
  });

  it("forwards the visitor's address to the API", async () => {
    let seen: string | null = null;
    server.use(
      http.get(`${API}/api/clubs/:slug`, ({ request }) => {
        seen = request.headers.get("x-forwarded-for");
        return HttpResponse.json({});
      }),
    );

    await proxy(requestFor("/clubs/clube-da-foz", { "x-forwarded-for": "198.51.100.7, 203.0.113.9" }));

    expect(seen).toBe("203.0.113.9");
  });

  it("answers 404 for an id that is not a GUID on the owner and admin pages, without asking anyone", async () => {
    for (const path of ["/manage/clubs/not-a-guid", "/manage/clubs/not-a-guid/calendar", "/manage/clubs/not-a-guid/statistics", "/admin/owners/not-a-guid"]) {
      expectNotFound(await proxy(requestFor(path)));
    }
  });

  it("lets a GUID through to the page, which asks the API with the session", async () => {
    for (const path of [`/manage/clubs/${GUID}`, `/manage/clubs/${GUID}/calendar`, `/manage/clubs/${GUID}/statistics`, `/admin/owners/${GUID}`]) {
      expectLetThrough(await proxy(requestFor(path)));
    }
  });

  it("does not take /manage/clubs/new for a club id", async () => {
    expectLetThrough(await proxy(requestFor("/manage/clubs/new")));
  });

  it("keeps the request headers on the 404 answer too, so the guards still see the path", async () => {
    const response = await proxy(requestFor("/manage/clubs/not-a-guid", { cookie: "access_token=abc" }));

    expect(response.headers.get("x-middleware-request-x-pathname")).toBe("/manage/clubs/not-a-guid");
    expect(response.headers.get("x-middleware-request-cookie")).toBe("access_token=abc");
  });
});
