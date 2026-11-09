const LOCAL_API = "http://localhost:5080";

/**
 * Where `/api/*` is rewritten to (T06), read once by `next.config.ts` when `next build` runs: `API_INTERNAL_URL`, the same variable the
 * server code reads at run time. On Vercel a missing or plain-HTTP value fails the build, because the fallback would break every API call
 * without any error at build time and plain HTTP would carry the session cookies unencrypted between Vercel and the API.
 */
export function rewriteDestination(apiInternalUrl: string | undefined, onVercel: boolean): string {
  const value = apiInternalUrl?.trim();
  if (!value) {
    if (onVercel) {
      throw new Error("API_INTERNAL_URL is required on Vercel: the /api/* rewrite is fixed at build time (see docs/deployment/environment-variables.md).");
    }
    return LOCAL_API;
  }

  let origin: string | undefined;
  try {
    origin = new URL(value).origin;
  } catch {
    origin = undefined;
  }
  if (origin !== value || (!value.startsWith("https://") && !value.startsWith("http://"))) {
    throw new Error("API_INTERNAL_URL must be a bare origin such as https://api.example.com (scheme, host and optional port; no path or trailing slash).");
  }
  if (onVercel && !value.startsWith("https://")) {
    throw new Error("API_INTERNAL_URL must use https on Vercel: the requests it carries include the session cookies.");
  }
  return value;
}
