/**
 * Security headers for every page (T06), applied from next.config.ts. Plain functions with no Next.js imports, so a test can
 * read them as they are served.
 *
 * Content-Security-Policy: nothing is loaded from another origin (fonts are self-hosted by next/font, the API is reached on
 * the same origin through the rewrite), plugins, framing and foreign form targets are refused, and stylesheets come from 'self'.
 *
 * Known relaxation: script-src allows 'unsafe-inline'. Next.js streams inline scripts that carry each page's data
 * (`self.__next_f.push(...)`), and with `cacheComponents` (partial prerendering) the static shell is built before any request
 * exists. A per-request nonce therefore cannot reach those scripts, and a build-time hash cannot cover data that differs
 * per page and per request. Both were tried in a browser and both stopped the app from hydrating (experimental SRI only
 * hashes external scripts). A strict script-src needs a nonce and every page rendered per request, i.e. giving up
 * `cacheComponents`; that is an architecture decision, not a header setting.
 */

export type HeaderEntry = { key: string; value: string };

export type SecurityHeaderOptions = {
  /** `next dev`: React needs eval for debugging, and Hot Module Replacement connects over a WebSocket. */
  development: boolean;
  /** The site is served over HTTPS in production: adds HSTS and upgrade-insecure-requests (ENABLE_HSTS, T06). */
  https: boolean;
};

export function contentSecurityPolicy({ development, https }: SecurityHeaderOptions): string {
  const directives = [
    "default-src 'self'",
    // 'unsafe-inline': see the note above. External scripts still have to come from this origin.
    `script-src 'self' 'unsafe-inline'${development ? " 'unsafe-eval'" : ""}`,
    // 'unsafe-inline' for styles: style="..." attributes (calendar grid positions, Radix and Recharts sizing) and the <style>
    // element that Sonner adds at run time for the toasts. Our own CSS is a file from 'self'.
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: blob:",
    "font-src 'self'",
    `connect-src 'self'${development ? " ws:" : ""}`,
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
  ];
  if (https) {
    directives.push("upgrade-insecure-requests");
  }
  return directives.join("; ");
}

/** One year, this host only: no includeSubDomains and no preload, which are hard to undo. */
export const HSTS_VALUE = "max-age=31536000";

export function securityHeaders(options: SecurityHeaderOptions): HeaderEntry[] {
  const headers: HeaderEntry[] = [
    { key: "Content-Security-Policy", value: contentSecurityPolicy(options) },
    { key: "X-Content-Type-Options", value: "nosniff" },
    { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
    {
      key: "Permissions-Policy",
      value: "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()",
    },
  ];
  if (options.https && !options.development) {
    headers.push({ key: "Strict-Transport-Security", value: HSTS_VALUE });
  }
  return headers;
}
