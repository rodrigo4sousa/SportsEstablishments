import "server-only";

/** Base URL of the ASP.NET Core API as seen from the Next.js server (also used by the rewrites in next.config.ts). */
export function apiInternalUrl(): string {
  return process.env.API_INTERNAL_URL ?? "http://localhost:5080";
}
