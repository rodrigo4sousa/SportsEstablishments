const LOCAL_API = "http://localhost:5080";

const BARE_ORIGIN_RULE = "must be a bare origin such as https://api.example.com (scheme, host and optional port; no path or trailing slash)";

/**
 * The value as it was received, for an error message: JSON-quoted, with every character outside printable ASCII written as a u-escape (backslash, u, four hex digits).
 * `JSON.stringify` alone leaves an invisible character (a zero-width space pasted from a web page, a no-break space) in place, so the
 * message would show a value that looks right and still fails. An origin is not a secret, so it is safe to show.
 */
export function describeReceived(value: string): string {
  return JSON.stringify(value).replace(/[^\x20-\x7e]/g, (char) => "\\u" + char.charCodeAt(0).toString(16).padStart(4, "0"));
}

/** Why a non-empty, trimmed value is not a bare origin, in words; undefined when it is one. */
function bareOriginProblem(value: string): string | undefined {
  if (!value.startsWith("https://") && !value.startsWith("http://")) {
    return "it does not start with https:// or http:// (bad scheme)";
  }
  if (/\s/.test(value)) {
    return "it contains whitespace";
  }
  if (/["'`]/.test(value)) {
    return "it contains quotation marks";
  }
  if (/[^\x21-\x7e]/.test(value)) {
    return "it contains invisible or non-ASCII characters";
  }

  let url: URL;
  try {
    url = new URL(value);
  } catch {
    return "it is not a valid URL";
  }
  if (url.search !== "" || url.hash !== "" || value.includes("?") || value.includes("#")) {
    return "it has a query string or a fragment";
  }
  if (url.pathname === "/" && value.endsWith("/")) {
    return "it has a trailing slash";
  }
  if (url.pathname !== "/") {
    return "it has a path";
  }
  if (url.username !== "" || url.password !== "") {
    return "it has credentials";
  }
  if (url.origin !== value) {
    return "it is not in normal form (host in lower case, no default port)";
  }
  return undefined;
}

/**
 * Where `/api/*` is rewritten to (T06), read once by `next.config.ts` when `next build` runs: `API_INTERNAL_URL`, the same variable the
 * server code reads at run time. On Vercel a missing or plain-HTTP value fails the build, because the fallback would break every API call
 * without any error at build time and plain HTTP would carry the session cookies unencrypted between Vercel and the API.
 *
 * A refused value is quoted in the error, exactly as received, together with the reason, so a build log says what the build really got.
 */
export function rewriteDestination(apiInternalUrl: string | undefined, onVercel: boolean): string {
  const value = apiInternalUrl?.trim();
  if (!value) {
    if (onVercel) {
      throw new Error(
        `API_INTERNAL_URL is required on Vercel: the /api/* rewrite is fixed at build time (see docs/deployment/environment-variables.md). Received ${describeReceived(apiInternalUrl ?? "")}: it is empty.`,
      );
    }
    return LOCAL_API;
  }

  const problem = bareOriginProblem(value);
  if (problem) {
    throw new Error(`API_INTERNAL_URL ${BARE_ORIGIN_RULE}. Received ${describeReceived(apiInternalUrl ?? value)}: ${problem}.`);
  }
  if (onVercel && !value.startsWith("https://")) {
    throw new Error(`API_INTERNAL_URL must use https on Vercel: the requests it carries include the session cookies. Received ${describeReceived(apiInternalUrl ?? value)}: it is plain http.`);
  }
  return value;
}
