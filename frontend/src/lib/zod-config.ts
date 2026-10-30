import { z } from "zod";

// Zod 4 compiles a faster parser with `Function(...)` when the page allows it, and probes that with `Function("")`. Our
// Content-Security-Policy has no 'unsafe-eval', so the probe would log a violation on every page that uses a form. Without
// the compiled parser, validation behaves the same (only the speed of very large inputs changes; our forms are small).
z.config({ jitless: true });
