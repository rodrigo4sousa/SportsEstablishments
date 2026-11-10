import { describe, expect, it } from "vitest";
import { describeReceived, rewriteDestination } from "./rewrite-destination";

const refused = (value: string | undefined, onVercel = false): string => {
  try {
    rewriteDestination(value, onVercel);
  } catch (error) {
    return (error as Error).message;
  }
  throw new Error("expected rewriteDestination to throw");
};

describe("rewriteDestination", () => {
  it("uses the configured origin", () => {
    expect(rewriteDestination("https://reservas-api.onrender.com", true)).toBe("https://reservas-api.onrender.com");
    expect(rewriteDestination("http://api:8080", false)).toBe("http://api:8080");
  });

  it("still accepts a value with surrounding whitespace or a trailing newline, which is trimmed", () => {
    expect(rewriteDestination("https://api.example.com\n", true)).toBe("https://api.example.com");
    expect(rewriteDestination("  https://api.example.com\r\n", true)).toBe("https://api.example.com");
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

  describe("says what it received and why it refused it", () => {
    it.each([
      ["https://api.example.com/", "it has a trailing slash"],
      ["https://api.example.com/v1", "it has a path"],
      ["https://api.example.com/v1/", "it has a path"],
      ["https://api.example.com?x=1", "it has a query string or a fragment"],
      ["https://api.example.com/#top", "it has a query string or a fragment"],
      ["api.example.com", "bad scheme"],
      ["ftp://api.example.com", "bad scheme"],
      ['"https://api.example.com"', "bad scheme"],
      ["https://api example.com", "it contains whitespace"],
      ["https://api.example.com\nfoo", "it contains whitespace"],
      ['https://"api.example.com"', "it contains quotation marks"],
      ["https://api.example.com​", "it contains invisible or non-ASCII characters"],
      ["https://apié.example.com", "it contains invisible or non-ASCII characters"],
      ["https://", "it is not a valid URL"],
      ["https://user:pass@api.example.com", "it has credentials"],
      ["https://API.example.com", "it is not in normal form"],
      ["https://api.example.com:443", "it is not in normal form"],
    ])("refuses %j: %s", (value, reason) => {
      const message = refused(value);
      expect(message).toContain("bare origin");
      expect(message).toContain(reason);
    });

    it("quotes the value JSON-style", () => {
      expect(refused("https://api.example.com/")).toContain('Received "https://api.example.com/"');
    });

    it("writes invisible characters out, so the message does not show a value that looks right", () => {
      expect(refused("https://api.example.com​")).toContain('Received "https://api.example.com\\u200b"');
      expect(refused("https://api.example.com /x")).toContain("\\u00a0");
    });

    it("quotes the raw value, not the trimmed one", () => {
      expect(refused("  https://api.example.com/\n")).toContain('Received "  https://api.example.com/\\n"');
    });

    it("explains a missing value on Vercel", () => {
      expect(refused(undefined, true)).toContain('Received "": it is empty');
      expect(refused(" \n", true)).toContain('Received " \\n": it is empty');
    });

    it("explains plain http on Vercel", () => {
      expect(refused("http://api.example.com", true)).toContain('Received "http://api.example.com": it is plain http');
    });
  });
});

describe("describeReceived", () => {
  it("is JSON, with everything outside printable ASCII as a \\u escape", () => {
    expect(describeReceived("a\tb")).toBe('"a\\tb"');
    expect(describeReceived("﻿x")).toBe('"\\ufeffx"');
    expect(describeReceived("é")).toBe('"\\u00e9"');
  });
});
