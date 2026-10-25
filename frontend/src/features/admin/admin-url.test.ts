import { describe, expect, it } from "vitest";
import { adminHref, chartFromParams, DEFAULT_CHART, DEFAULT_OWNERS, ownersFromParams } from "./admin-url";

const params = (query: string) => new URLSearchParams(query);

describe("admin URL state", () => {
  it("has the defaults when the URL is empty, and an empty URL when everything is the default", () => {
    expect(chartFromParams(params(""))).toEqual(DEFAULT_CHART);
    expect(ownersFromParams(params(""))).toEqual(DEFAULT_OWNERS);
    expect(adminHref("/admin", DEFAULT_CHART, DEFAULT_OWNERS)).toBe("/admin");
  });

  it("round-trips every part of the view", () => {
    const chart = { granularity: "month", from: "2026-01-01", to: "2026-06-30", includeCancelled: true } as const;
    const owners = { search: "ana", sort: "players", direction: "desc", page: 3 } as const;

    const href = adminHref("/admin", chart, owners);

    expect(href).toBe("/admin?granularity=month&from=2026-01-01&to=2026-06-30&cancelled=1&search=ana&sort=players&direction=desc&page=3");
    const read = params(href.split("?")[1]);
    expect(chartFromParams(read)).toEqual(chart);
    expect(ownersFromParams(read)).toEqual(owners);
  });

  it.each([
    ["granularity=year", { granularity: "day" }],
    ["from=2026-02-30", { from: null }],
    ["to=amanhã", { to: null }],
    ["cancelled=true", { includeCancelled: false }],
  ])("falls back to the default for %s", (query, expected) => {
    expect(chartFromParams(params(query))).toMatchObject(expected);
  });

  it.each([
    ["sort=password", { sort: "name" }],
    ["direction=up", { direction: "asc" }],
    ["page=0", { page: 1 }],
    ["page=-2", { page: 1 }],
    ["page=abc", { page: 1 }],
    ["page=1.5", { page: 1 }],
  ])("falls back to the default for %s", (query, expected) => {
    expect(ownersFromParams(params(query))).toMatchObject(expected);
  });

  it("trims the search and cuts it at the API's limit", () => {
    expect(ownersFromParams(params("search=%20%20ana%20")).search).toBe("ana");
    expect(ownersFromParams(params(`search=${"a".repeat(150)}`)).search).toHaveLength(100);
  });
});
