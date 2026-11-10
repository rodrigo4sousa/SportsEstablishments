import { beforeEach, describe, expect, it, vi } from "vitest";
import { checkClub, guidSegment, hasImpossibleId, KNOWN_CLUB_TTL_MS, publicClubSlug, resetKnownClubs, visitorAddress } from "./not-found-check";

const GUID = "0199f7a0-0000-7000-8000-0000000000aa";

function answer(status: number) {
  return vi.fn(async () => new Response(status === 404 ? null : "{}", { status }));
}

describe("publicClubSlug", () => {
  it("takes the one segment after /clubs", () => {
    expect(publicClubSlug("/clubs/clube-da-foz")).toBe("clube-da-foz");
    expect(publicClubSlug("/clubs/clube-da-foz/")).toBe("clube-da-foz");
    expect(publicClubSlug("/clubs/Clube%20X")).toBe("Clube X");
  });

  it("ignores every other path", () => {
    for (const path of ["/clubs", "/clubs/", "/clubs/a/b", "/", "/manage/clubs/x", "/club/x"]) {
      expect(publicClubSlug(path)).toBeNull();
    }
  });

  it("turns a broken escape into something that is not a slug", () => {
    expect(publicClubSlug("/clubs/%E0%A4%A")).toBe("%");
  });
});

describe("guidSegment", () => {
  it("takes the id of the owner and admin pages that have one", () => {
    expect(guidSegment(`/manage/clubs/${GUID}`)).toBe(GUID);
    expect(guidSegment(`/manage/clubs/${GUID}/calendar`)).toBe(GUID);
    expect(guidSegment(`/manage/clubs/${GUID}/statistics/`)).toBe(GUID);
    expect(guidSegment(`/admin/owners/${GUID}`)).toBe(GUID);
  });

  it("ignores the pages without an id, and the page that creates a club", () => {
    for (const path of ["/manage", "/manage/clubs/new", "/manage/clubs/new/", "/manage/statistics", "/admin", "/admin/owners", `/manage/clubs/${GUID}/other`]) {
      expect(guidSegment(path)).toBeNull();
    }
  });
});

describe("hasImpossibleId", () => {
  it.each(["/clubs/Not_A_Slug", "/clubs/UPPER", "/clubs/a--b", "/manage/clubs/abc", "/manage/clubs/abc/calendar", "/admin/owners/1"])("is true for %s", (path) => {
    expect(hasImpossibleId(path)).toBe(true);
  });

  it.each(["/clubs/clube-da-foz", `/manage/clubs/${GUID}`, `/admin/owners/${GUID}`, "/manage/clubs/new", "/", "/login", "/bookings"])("is false for %s", (path) => {
    expect(hasImpossibleId(path)).toBe(false);
  });
});

describe("visitorAddress", () => {
  it("is the last entry, and only an IP", () => {
    expect(visitorAddress("198.51.100.7, 203.0.113.9")).toBe("203.0.113.9");
    expect(visitorAddress("2001:db8::1")).toBe("2001:db8::1");
    expect(visitorAddress("not an ip")).toBeUndefined();
    expect(visitorAddress("")).toBeUndefined();
    expect(visitorAddress(null)).toBeUndefined();
  });
});

describe("checkClub", () => {
  beforeEach(() => resetKnownClubs());
  const deps = (fetchFn: typeof fetch, now = () => 1_000) => ({ apiUrl: "http://api", fetchFn, now });

  it("is missing for a 404 and exists for a 200", async () => {
    expect(await checkClub("a", deps(answer(404) as unknown as typeof fetch))).toBe("missing");
    expect(await checkClub("b", deps(answer(200) as unknown as typeof fetch))).toBe("exists");
  });

  it("is unknown, never missing, when the API fails", async () => {
    expect(await checkClub("a", deps(answer(500) as unknown as typeof fetch))).toBe("unknown");
    expect(await checkClub("a", deps(answer(429) as unknown as typeof fetch))).toBe("unknown");
    expect(await checkClub("a", deps((async () => Promise.reject(new Error("down"))) as unknown as typeof fetch))).toBe("unknown");
  });

  it("asks for the club by its encoded slug, with the visitor's address", async () => {
    const fetchFn = answer(200);

    await checkClub("clube-da-foz", { apiUrl: "http://api", forwardedFor: "203.0.113.9", fetchFn: fetchFn as unknown as typeof fetch });

    const [url, init] = fetchFn.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("http://api/api/clubs/clube-da-foz");
    expect((init.headers as Record<string, string>)["X-Forwarded-For"]).toBe("203.0.113.9");
  });

  it("does not ask again for a club it saw a moment ago, but does after the time is up", async () => {
    const fetchFn = answer(200);
    let now = 1_000;
    const check = () => checkClub("a", deps(fetchFn as unknown as typeof fetch, () => now));

    expect(await check()).toBe("exists");
    now += KNOWN_CLUB_TTL_MS - 1;
    expect(await check()).toBe("exists");
    expect(fetchFn).toHaveBeenCalledTimes(1);

    now += 2;
    expect(await check()).toBe("exists");
    expect(fetchFn).toHaveBeenCalledTimes(2);
  });

  it("forgets a club the API stops knowing", async () => {
    let status = 200;
    const fetchFn = vi.fn(async () => new Response(status === 404 ? null : "{}", { status }));
    let now = 1_000;
    const check = () => checkClub("a", deps(fetchFn as unknown as typeof fetch, () => now));

    expect(await check()).toBe("exists");
    status = 404;
    now += KNOWN_CLUB_TTL_MS + 1;
    expect(await check()).toBe("missing");
    expect(await check()).toBe("missing");
  });

  it("does not remember a failure as a good answer", async () => {
    const fetchFn = vi.fn().mockResolvedValueOnce(new Response(null, { status: 500 })).mockResolvedValue(new Response(null, { status: 404 }));

    expect(await checkClub("a", deps(fetchFn as unknown as typeof fetch))).toBe("unknown");
    expect(await checkClub("a", deps(fetchFn as unknown as typeof fetch))).toBe("missing");
  });
});
