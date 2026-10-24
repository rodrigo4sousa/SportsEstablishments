import { describe, expect, it } from "vitest";
import { searchQuery } from "./api";
import { clubHref, filtersFromParams, NO_FILTERS, searchHref } from "./search-filters";

const params = (query: string) => new URLSearchParams(query);

describe("filtersFromParams", () => {
  it("reads every filter from the URL", () => {
    expect(filtersFromParams(params("sport=padel&locality=Porto&date=2026-05-04&page=3"))).toEqual({
      sport: "padel",
      locality: "Porto",
      date: "2026-05-04",
      page: 3,
    });
  });

  it("starts empty", () => {
    expect(filtersFromParams(params(""))).toEqual(NO_FILTERS);
  });

  it("drops what is not valid instead of searching for it", () => {
    expect(filtersFromParams(params("sport=golfe")).sport).toBe("");
    expect(filtersFromParams(params("sport=Padel")).sport).toBe("");
    expect(filtersFromParams(params("date=amanha")).date).toBe("");
    expect(filtersFromParams(params("date=2026-02-30")).date).toBe("");
    expect(filtersFromParams(params(`locality=${"x".repeat(101)}`)).locality).toBe("");
    for (const page of ["0", "-1", "1.5", "abc", "10001", ""]) {
      expect(filtersFromParams(params(`page=${page}`)).page, page).toBe(1);
    }
  });

  it("keeps a real date even when it is outside the booking window, so the page can say so", () => {
    expect(filtersFromParams(params("date=2030-01-01")).date).toBe("2030-01-01");
  });

  it("trims the locality", () => {
    expect(filtersFromParams(params("locality=%20%C3%89vora%20")).locality).toBe("Évora");
  });
});

describe("searchQuery and searchHref", () => {
  it("sends only the filters that are set, and page only after the first", () => {
    expect(searchQuery(NO_FILTERS)).toBe("");
    expect(searchQuery({ sport: "tennis", locality: "São João da Madeira", date: "", page: 1 })).toBe("sport=tennis&locality=S%C3%A3o+Jo%C3%A3o+da+Madeira");
    expect(searchQuery({ sport: "", locality: "", date: "2026-05-04", page: 2 })).toBe("date=2026-05-04&page=2");
  });

  it("builds the address of a search", () => {
    expect(searchHref("/", NO_FILTERS)).toBe("/");
    expect(searchHref("/", { ...NO_FILTERS, sport: "padel", page: 2 })).toBe("/?sport=padel&page=2");
  });

  it("round-trips through the URL", () => {
    const filters = { sport: "football7", locality: "Vila Nova de Gaia", date: "2026-05-10", page: 4 };
    const [, query] = searchHref("/", filters).split("?");

    expect(filtersFromParams(params(query))).toEqual(filters);
  });
});

describe("clubHref", () => {
  it("opens the club page on the date that was searched", () => {
    expect(clubHref("clube-da-foz", { date: "" })).toBe("/clubs/clube-da-foz");
    expect(clubHref("clube-da-foz", { date: "2026-05-04" })).toBe("/clubs/clube-da-foz?date=2026-05-04");
  });
});
