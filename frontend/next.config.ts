import type { NextConfig } from "next";
import { securityHeaders } from "./src/lib/security-headers";

// Base URL of the ASP.NET Core API as seen from the Next.js server.
// Rewrites keep the browser on a single origin, so auth cookies stay first-party (see docs/stack.md).
const apiInternalUrl = process.env.API_INTERNAL_URL ?? "http://localhost:5080";

// The headers are fixed when `next build` runs (the routes manifest), so ENABLE_HSTS is a build setting, off unless set to "true"
// (the Dockerfile takes it as a build argument). Turn it on only for a site that is served over HTTPS.
const https = process.env.ENABLE_HSTS === "true";

const nextConfig: NextConfig = {
  output: "standalone",
  // No `X-Powered-By: Next.js`.
  poweredByHeader: false,
  cacheComponents: true,
  partialPrefetching: true,
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
  async headers() {
    return [
      {
        source: "/:path*",
        headers: securityHeaders({ development: process.env.NODE_ENV === "development", https }),
      },
      {
        // US05: the reset link carries a secret token in its address. Without a Referer, nothing the page loads or links to can
        // learn it, and the page is never cached. Listed after the rule above: for the same header, the later rule wins.
        source: "/reset-password",
        headers: [
          { key: "Referrer-Policy", value: "no-referrer" },
          { key: "Cache-Control", value: "no-store" },
        ],
      },
    ];
  },
  async rewrites() {
    return [
      {
        source: "/api/:path*",
        destination: `${apiInternalUrl}/api/:path*`,
      },
    ];
  },
};

export default nextConfig;
