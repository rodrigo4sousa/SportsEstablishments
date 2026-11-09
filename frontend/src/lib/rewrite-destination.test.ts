import { describe, expect, it } from "vitest";
import { rewriteDestination } from "./rewrite-destination";

describe("rewriteDestination", () => {
  it("uses the configured origin", () => {
    expect(rewriteDestination("https://reservas-api.onrender.com", true)).toBe("https://reservas-api.onrender.com");
    expect(rewriteDestination("http://api:8080", false)).toBe("http://api:8080");
  });

  it("falls back to the local API outside Vercel", () => {
    expect(rewriteDestination(undefined, false)).toBe("http://localhost:5080");
    expect(rewriteDestination("  ", false)).toBe("http://localhost:5080");
  });

  it("fails the build on Vercel when the variable is missing", () => {
    expect(() => rewriteDestination(undefined, true)).toThrow("API_INTERNAL_URL is required");
    expect(() => rewriteDestination("", true)).toThrow("API_INTERNAL_URL is required");
  });

  it("refuses plain HTTP on Vercel", () => {
    expect(() => rewriteDestination("http://reservas-api.onrender.com", true)).toThrow("must use https");
  });

  it.each(["https://api.example.com/", "https://api.example.com/v1", "https://api.example.com?x=1", "api.example.com", "ftp://api.example.com"])(
    "refuses %s, which is not a bare origin",
    (value) => {
      expect(() => rewriteDestination(value, false)).toThrow("bare origin");
    },
  );
});
