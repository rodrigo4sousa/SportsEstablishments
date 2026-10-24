/**
 * Where a user goes depending on session and profile (US03). Pure functions, so every redirect decision can be
 * unit-tested; the area layouts and the login page only apply them. The API stays the real gatekeeper.
 */

export const PROFILES = ["Player", "Owner", "Admin"] as const;

export type Profile = (typeof PROFILES)[number];

/** The home page of each profile's area. */
const PROFILE_HOME: Record<Profile, string> = {
  Player: "/",
  Owner: "/manage",
  Admin: "/admin",
};

export const LOGIN_PATH = "/login";

export function isProfile(value: unknown): value is Profile {
  return PROFILES.some((profile) => profile === value);
}

/** Where a profile lands after login, and where a user of another profile is sent back to. */
export function profileHome(profile: string): string {
  return isProfile(profile) ? PROFILE_HOME[profile] : PROFILE_HOME.Player;
}

/** `/login?next=<path>`: signing in (or a silent refresh) brings the user back to `path`. */
export function loginUrl(next: string): string {
  return `${LOGIN_PATH}?next=${encodeURIComponent(next)}`;
}

export const REGISTER_PATH = "/register";

/**
 * The sign-up page, keeping where to go afterwards: someone who chooses "Criar conta" on the login page still comes
 * back to the page they were sent from. `next` is passed on as it is; it is judged by `safeNext` when the account has
 * been created, so a bad value is only ever dropped, never followed.
 */
export function registerUrl(next: string | null | undefined): string {
  return typeof next === "string" && next !== "" ? `${REGISTER_PATH}?next=${encodeURIComponent(next)}` : REGISTER_PATH;
}

export type AreaDecision<T> = { allowed: true; account: T } | { allowed: false; redirectTo: string };

/**
 * Guard of an area layout (`/manage`, `/admin`). No session goes to the login page, coming back to the page that was
 * asked for (`requestedPath`: path and query, from the proxy's header) when it is inside the area, else to the
 * area's home; a session of another profile goes to that user's own area. A guard is a convenience: the API
 * answers 401/403 itself.
 */
export function areaDecision<T extends { profile: string }>(
  area: Profile,
  account: T | null,
  requestedPath?: string | null,
): AreaDecision<T> {
  if (account === null) {
    // safeNext is the one place that decides what is an acceptable return path, here and after login.
    return { allowed: false, redirectTo: loginUrl(safeNext(requestedPath, area)) };
  }
  if (account.profile !== area) {
    return { allowed: false, redirectTo: profileHome(account.profile) };
  }
  return { allowed: true, account };
}

/** Pages that are not part of anyone's area: sign-in and sign-up. */
const ACCOUNT_PAGES = [LOGIN_PATH, "/register"];

/** Backslashes (some browsers treat them as slashes) and control characters such as line breaks. */
function hasUnsafeCharacter(value: string): boolean {
  return [...value].some((character) => {
    const code = character.charCodeAt(0);
    return character === "\\" || code < 0x20 || code === 0x7f;
  });
}

function isWithin(path: string, base: string): boolean {
  return path === base || path.startsWith(`${base}/`) || path.startsWith(`${base}?`) || path.startsWith(`${base}#`);
}

/**
 * The `next` parameter is accepted only if it is a relative path starting with a single `/` that belongs to the
 * profile's own area; anything else (another site, another profile's area, the login page itself, garbage) falls back
 * to the profile's home. Owners and admins have their own prefix; players have the public pages.
 */
export function safeNext(next: string | null | undefined, profile: string): string {
  const home = profileHome(profile);
  if (typeof next !== "string" || !next.startsWith("/") || next.startsWith("//") || hasUnsafeCharacter(next)) {
    return home;
  }

  // Resolve against a throwaway origin: a path that escapes it (or changes once normalized, e.g. "/a/../b") is rejected.
  const base = "http://localhost";
  let resolved: URL;
  try {
    resolved = new URL(next, base);
  } catch {
    return home;
  }
  if (resolved.origin !== base || `${resolved.pathname}${resolved.search}${resolved.hash}` !== next) {
    return home;
  }

  const path = resolved.pathname;
  const inOwnerArea = isWithin(path, PROFILE_HOME.Owner);
  const inAdminArea = isWithin(path, PROFILE_HOME.Admin);
  const belongs =
    profile === "Owner"
      ? inOwnerArea
      : profile === "Admin"
        ? inAdminArea
        : isProfile(profile) && !inOwnerArea && !inAdminArea && !ACCOUNT_PAGES.some((page) => isWithin(path, page));
  return belongs ? next : home;
}
