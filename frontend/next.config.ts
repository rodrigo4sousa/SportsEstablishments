import type { NextConfig } from "next";

// Base URL of the ASP.NET Core API as seen from the Next.js server.
// Rewrites keep the browser on a single origin, so auth cookies stay first-party (see docs/stack.md).
const apiInternalUrl = process.env.API_INTERNAL_URL ?? "http://localhost:5080";

const nextConfig: NextConfig = {
  output: "standalone",
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
        // US05: the reset link carries a secret token in its address. Without a Referer, nothing the page loads or links to can
        // learn it, and the page is never cached.
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
