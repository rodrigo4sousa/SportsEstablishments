import { beforeEach, describe, expect, it, vi } from "vitest";
import { visitorIpHeader } from "./visitor-ip";

let forwardedFor: string | null = null;
vi.mock("next/headers", () => ({
  headers: async () => ({ get: (name: string) => (name === "x-forwarded-for" ? forwardedFor : null) }),
}));

describe("visitorIpHeader", () => {
  beforeEach(() => {
    forwardedFor = null;
  });

  it("forwards the visitor's address", async () => {
    forwardedFor = "198.51.100.7";
    await expect(visitorIpHeader()).resolves.toEqual({ "X-Forwarded-For": "198.51.100.7" });
  });

  it("forwards an IPv6 address", async () => {
    forwardedFor = "2001:db8::1";
    await expect(visitorIpHeader()).resolves.toEqual({ "X-Forwarded-For": "2001:db8::1" });
  });

  it("uses the last entry of a list, the one added by the nearest proxy", async () => {
    forwardedFor = "203.0.113.9, 198.51.100.7";
    await expect(visitorIpHeader()).resolves.toEqual({ "X-Forwarded-For": "198.51.100.7" });
  });

  it.each([null, "", "not-an-address", "198.51.100.7, nonsense"])("sends nothing for %s", async (value) => {
    forwardedFor = value;
    await expect(visitorIpHeader()).resolves.toEqual({});
  });
});
