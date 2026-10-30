import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { toast } from "sonner";
import { afterAll, afterEach, beforeAll } from "vitest";
import { server } from "@/test/msw";

// Any request without a handler fails the test, so nothing reaches a real network.
beforeAll(() => server.listen({ onUnhandledFrame: "error" }));

afterEach(() => {
  cleanup();
  // Sonner keeps its toasts in module state: without this, one test's toast would still be there for the next one.
  toast.dismiss();
  server.resetHandlers();
});

afterAll(() => server.close());
