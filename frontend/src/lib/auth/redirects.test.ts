import { describe, expect, it } from "vitest";
import { areaDecision, loginUrl, profileHome, registerUrl, safeNext } from "./redirects";

describe("profileHome", () => {
  it.each([
    ["Player", "/"],
    ["Owner", "/manage"],
    ["Admin", "/admin"],
    ["Something", "/"],
  ])("%s lands on %s", (profile, home) => {
    expect(profileHome(profile)).toBe(home);
  });
});

describe("loginUrl", () => {
  it("carries the path to come back to, encoded", () => {
    expect(loginUrl("/manage")).toBe("/login?next=%2Fmanage");
    expect(loginUrl("/manage/clubs?x=1&y=2")).toBe("/login?next=%2Fmanage%2Fclubs%3Fx%3D1%26y%3D2");
  });
});

describe("areaDecision", () => {
  it("sends a visitor without a session to the login page, to come back to the area", () => {
    expect(areaDecision("Owner", null)).toEqual({ allowed: false, redirectTo: "/login?next=%2Fmanage" });
    expect(areaDecision("Admin", null)).toEqual({ allowed: false, redirectTo: "/login?next=%2Fadmin" });
  });

  it("sends a visitor without a session back to the page they asked for, when it is inside the area", () => {
    expect(areaDecision("Owner", null, "/manage/clubs/new")).toEqual({
      allowed: false,
      redirectTo: "/login?next=%2Fmanage%2Fclubs%2Fnew",
    });
    expect(areaDecision("Owner", null, "/manage/clubs/0199f7a0-0000-7000-8000-0000000000aa?tab=a")).toEqual({
      allowed: false,
      redirectTo: "/login?next=%2Fmanage%2Fclubs%2F0199f7a0-0000-7000-8000-0000000000aa%3Ftab%3Da",
    });
    expect(areaDecision("Admin", null, "/admin/owners")).toEqual({ allowed: false, redirectTo: "/login?next=%2Fadmin%2Fowners" });
  });

  it.each([
    ["Owner", "/admin/owners"],
    ["Owner", "//evil.example"],
    ["Owner", "https://evil.example/manage"],
    ["Owner", "/manage/../admin"],
    ["Owner", ""],
    ["Owner", null],
    ["Owner", undefined],
    ["Admin", "/manage/clubs/new"],
  ] as const)("falls back to the area's home when the %s area was asked for %j", (area, requested) => {
    const home = area === "Owner" ? "/manage" : "/admin";

    expect(areaDecision(area, null, requested)).toEqual({ allowed: false, redirectTo: `/login?next=${encodeURIComponent(home)}` });
  });

  it("ignores the requested path for a session of another profile", () => {
    expect(areaDecision("Owner", { profile: "Player" }, "/manage/clubs/new")).toEqual({ allowed: false, redirectTo: "/" });
  });

  it("lets the area's own profile in", () => {
    const owner = { profile: "Owner", name: "Rui" };
    const admin = { profile: "Admin", name: "Ana" };

    expect(areaDecision("Owner", owner)).toEqual({ allowed: true, account: owner });
    expect(areaDecision("Admin", admin)).toEqual({ allowed: true, account: admin });
  });

  it.each([
    ["Owner", "Player", "/"],
    ["Owner", "Admin", "/admin"],
    ["Admin", "Player", "/"],
    ["Admin", "Owner", "/manage"],
  ] as const)("sends a %s area visitor with the %s profile to their own area (%s)", (area, profile, home) => {
    expect(areaDecision(area, { profile })).toEqual({ allowed: false, redirectTo: home });
  });
});

describe("safeNext", () => {
  it.each([
    ["Owner", "/manage"],
    ["Owner", "/manage/clubs/1?tab=a#top"],
    ["Admin", "/admin"],
    ["Admin", "/admin/owners"],
    ["Player", "/"],
    ["Player", "/clubs/abc"],
    ["Player", "/search?sport=padel"],
  ])("%s keeps %s", (profile, next) => {
    expect(safeNext(next, profile)).toBe(next);
  });

  it.each([
    ["Owner", "/admin", "/manage"],
    ["Owner", "/", "/manage"],
    ["Owner", "/managers", "/manage"],
    ["Admin", "/manage", "/admin"],
    ["Player", "/manage", "/"],
    ["Player", "/admin/owners", "/"],
    ["Player", "/login", "/"],
    ["Player", "/register", "/"],
  ])("%s does not get %s: falls back to %s", (profile, next, home) => {
    expect(safeNext(next, profile)).toBe(home);
  });

  it.each([
    "//evil.example",
    "//evil.example/manage",
    "/\evil.example",
    "/manage\..\admin",
    "https://evil.example/manage",
    "http://localhost/manage",
    "javascript:alert(1)",
    "manage",
    "",
    "/manage/../admin",
    "/manage/%2e%2e/admin",
    "/man\nage",
    "/manage\t",
  ])("ignores the unsafe value %j", (next) => {
    expect(safeNext(next, "Owner")).toBe("/manage");
  });

  describe("returning to a slot on a club page after signing in (US18)", () => {
    const pitch = "0199f7a0-0000-7000-8000-0000000000d1";
    // As ClubBooking writes it (colons percent-encoded) and as a hand-made link might (raw colons).
    const encoded = `/clubs/clube-da-foz?pitch=${pitch}&date=2026-05-04&slot=2026-05-04T10%3A00%3A00Z`;
    const raw = `/clubs/clube-da-foz?pitch=${pitch}&date=2026-05-04&slot=2026-05-04T10:00:00Z`;

    it.each([encoded, raw, "/clubs/clube-da-foz", "/clubs/clube-da-foz?date=2026-05-04"])("a player is sent back to %s unchanged", (next) => {
      expect(safeNext(next, "Player")).toBe(next);
    });

    it("survives the trip through the login address", () => {
      const next = new URL(loginUrl(encoded), "http://localhost").searchParams.get("next");

      expect(next).toBe(encoded);
      expect(safeNext(next, "Player")).toBe(encoded);
    });

    it("sends an owner or an admin to their own area instead, since they cannot book", () => {
      expect(safeNext(encoded, "Owner")).toBe("/manage");
      expect(safeNext(encoded, "Admin")).toBe("/admin");
    });

    it("still refuses a way out of the site written into a club address", () => {
      for (const next of ["//evil.example/clubs/x", "/\\evil.example/clubs/x", "https://evil.example/clubs/x"]) {
        expect(safeNext(next, "Player"), next).toBe("/");
      }
    });
  });

  it("uses the profile's home when there is no next", () => {
    expect(safeNext(null, "Admin")).toBe("/admin");
    expect(safeNext(undefined, "Owner")).toBe("/manage");
    expect(safeNext(null, "Player")).toBe("/");
  });

  it("falls back to the player home for an unknown profile", () => {
    expect(safeNext("/manage", "Mystery")).toBe("/");
  });
});

describe("registerUrl", () => {
  it("is the plain sign-up page without a next", () => {
    for (const next of [null, undefined, ""]) {
      expect(registerUrl(next)).toBe("/register");
    }
  });

  it("carries next, encoded, and gives it back whole after the trip", () => {
    const next = "/clubs/clube-da-foz?pitch=p1&date=2026-05-04&slot=2026-05-04T10%3A00%3A00Z";

    const url = registerUrl(next);

    expect(url).toMatch(/^\/register\?next=/);
    expect(new URL(url, "http://localhost").searchParams.get("next")).toBe(next);
  });

  it("passes on what it is given without judging it: safeNext does that once the account exists", () => {
    const url = registerUrl("//evil.example");

    expect(new URL(url, "http://localhost").searchParams.get("next")).toBe("//evil.example");
    expect(safeNext("//evil.example", "Player")).toBe("/");
  });
});
