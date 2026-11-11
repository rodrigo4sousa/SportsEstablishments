import "@testing-library/jest-dom/vitest";
import { cleanup, configure } from "@testing-library/react";
import { toast } from "sonner";
import { afterAll, afterEach, beforeAll, vi } from "vitest";
import { server } from "@/test/msw";

// Server Components read their messages through next-intl's request config, which only exists inside Next.js: tests get the real
// catalogs in the language a test chose (`setServerLocale`), pt-PT by default.
vi.mock("next-intl/server", () => import("@/test/intl-server"));

// `findBy…` and `waitFor` wait up to 3 s instead of 1 s: with the whole suite running in parallel, a busy machine (or CI) can take
// longer than a second to settle a form that answers after a request. A test that passes is not slower for it.
configure({ asyncUtilTimeout: 3000 });

// Any request without a handler fails the test, so nothing reaches a real network.
beforeAll(() => server.listen({ onUnhandledFrame: "error" }));

afterEach(async () => {
  cleanup();
  // Sonner keeps its toasts in module state: without this, one test's toast would still be there for the next one.
  toast.dismiss();
  server.resetHandlers();
  (await import("@/test/intl-server")).resetServerLocale();
});

afterAll(() => server.close());
