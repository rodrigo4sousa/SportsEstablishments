import { setupServer } from "msw/node";

/** Simulated API for component tests (MSW). Add handlers per test with `server.use(...)`. */
export const server = setupServer();
