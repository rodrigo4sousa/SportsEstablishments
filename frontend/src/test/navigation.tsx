import { useMemo, useSyncExternalStore } from "react";
import { vi } from "vitest";

/**
 * A stand-in for `next/navigation` whose URL is real: `router.replace` and `router.push` change it and every
 * `useSearchParams`/`usePathname` re-renders, like in the app. Use it with
 * `vi.mock("next/navigation", async () => import("@/test/navigation"))`, then `resetNavigation("/path?query")`.
 */
let currentUrl = "/";
const listeners = new Set<() => void>();

export const push = vi.fn();
export const replace = vi.fn();

function go(url: string) {
  currentUrl = url;
  listeners.forEach((listener) => listener());
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

const router = {
  push: (url: string, options?: unknown) => {
    push(url, options);
    go(url);
  },
  replace: (url: string, options?: unknown) => {
    replace(url, options);
    go(url);
  },
  refresh: vi.fn(),
  back: vi.fn(),
  forward: vi.fn(),
  prefetch: vi.fn(),
};

/** Sets the URL (path and query) the page is at, and forgets the calls of earlier tests. */
export function resetNavigation(url = "/") {
  push.mockClear();
  replace.mockClear();
  go(url);
}

/** The path and query the page is at now. */
export function currentPath(): string {
  return currentUrl;
}

export function useRouter() {
  return router;
}

export function usePathname(): string {
  return useSyncExternalStore(subscribe, () => currentUrl, () => currentUrl).split("?")[0];
}

export function useSearchParams(): URLSearchParams {
  const url = useSyncExternalStore(subscribe, () => currentUrl, () => currentUrl);
  return useMemo(() => new URLSearchParams(url.split("#")[0].split("?")[1] ?? ""), [url]);
}

/** Mirrors Next's `notFound()`: it throws, and a test can tell it from any other error. */
export class NotFoundSignal extends Error {
  constructor() {
    super("NEXT_NOT_FOUND");
  }
}

export function notFound(): never {
  throw new NotFoundSignal();
}
