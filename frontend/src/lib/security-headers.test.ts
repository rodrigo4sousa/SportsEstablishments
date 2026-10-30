import { describe, expect, it } from "vitest";
import nextConfig from "../../next.config";
import { HSTS_VALUE, contentSecurityPolicy, securityHeaders } from "./security-headers";

/** The headers a path gets: every rule whose source matches, in order, and for the same header the later rule wins (as in Next.js). */
async function servedHeaders(path: string): Promise<Record<string, string>> {
  const rules = await nextConfig.headers!();
  const served: Record<string, string> = {};
  for (const rule of rules) {
    if (rule.source === "/:path*" || rule.source === path) {
      for (const header of rule.headers) {
        served[header.key] = header.value;
      }
    }
  }
  return served;
}

const production = { development: false, https: false };

describe("the headers every page is served with", () => {
  it("has the security headers on an ordinary page", async () => {
    const headers = await servedHeaders("/login");

    expect(headers["Content-Security-Policy"]).toBe(contentSecurityPolicy({ development: false, https: false }));
    expect(headers["X-Content-Type-Options"]).toBe("nosniff");
    expect(headers["Referrer-Policy"]).toBe("strict-origin-when-cross-origin");
    expect(headers["Permissions-Policy"]).toContain("camera=()");
    expect(headers["Permissions-Policy"]).toContain("geolocation=()");
  });

  it("keeps the reset page's no-referrer and no-store, and still gives it the other headers", async () => {
    const headers = await servedHeaders("/reset-password");

    expect(headers["Referrer-Policy"]).toBe("no-referrer");
    expect(headers["Cache-Control"]).toBe("no-store");
    expect(headers["Content-Security-Policy"]).toBeDefined();
    expect(headers["X-Content-Type-Options"]).toBe("nosniff");
  });

  it("does not send Strict-Transport-Security unless the site is served over HTTPS", async () => {
    expect((await servedHeaders("/login"))["Strict-Transport-Security"]).toBeUndefined();
    expect(securityHeaders({ development: false, https: false }).map((h) => h.key)).not.toContain("Strict-Transport-Security");
  });

  it("sends Strict-Transport-Security in production over HTTPS, and never in development", () => {
    const over = Object.fromEntries(securityHeaders({ development: false, https: true }).map((h) => [h.key, h.value]));
    expect(over["Strict-Transport-Security"]).toBe(HSTS_VALUE);
    expect(HSTS_VALUE).toBe("max-age=31536000");

    const dev = securityHeaders({ development: true, https: true }).map((h) => h.key);
    expect(dev).not.toContain("Strict-Transport-Security");
  });
});

describe("the Content-Security-Policy", () => {
  const directives = (policy: string) =>
    Object.fromEntries(policy.split("; ").map((part) => [part.split(" ")[0], part.split(" ").slice(1)]));

  it("loads nothing from another origin", () => {
    const policy = contentSecurityPolicy(production);

    expect(policy).not.toMatch(/https?:/);
    expect(policy).not.toContain("*");
    expect(directives(policy)["default-src"]).toEqual(["'self'"]);
    expect(directives(policy)["connect-src"]).toEqual(["'self'"]);
  });

  it("refuses framing, plugins and other form targets", () => {
    const parsed = directives(contentSecurityPolicy(production));

    expect(parsed["frame-ancestors"]).toEqual(["'none'"]);
    expect(parsed["object-src"]).toEqual(["'none'"]);
    expect(parsed["base-uri"]).toEqual(["'self'"]);
    expect(parsed["form-action"]).toEqual(["'self'"]);
  });

  it("allows no eval in production, only in development", () => {
    expect(contentSecurityPolicy(production)).not.toContain("'unsafe-eval'");
    expect(contentSecurityPolicy({ development: true, https: false })).toContain("'unsafe-eval'");
  });

  it("relaxes only inline scripts and styles (a known, documented limit), never script sources", () => {
    const parsed = directives(contentSecurityPolicy(production));

    expect(parsed["script-src"]).toEqual(["'self'", "'unsafe-inline'"]);
    expect(parsed["style-src"]).toEqual(["'self'", "'unsafe-inline'"]);
    expect(parsed["font-src"]).toEqual(["'self'"]);
  });

  it("upgrades insecure requests only when the site is served over HTTPS", () => {
    expect(contentSecurityPolicy(production)).not.toContain("upgrade-insecure-requests");
    expect(contentSecurityPolicy({ development: false, https: true })).toContain("upgrade-insecure-requests");
  });
});
