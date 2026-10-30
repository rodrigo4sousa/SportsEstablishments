import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";

/** UX-001: the focus ring is one rule in `globals.css`, so no component may take it away from a control in the keyboard order. */
describe("components and the focus rule", () => {
  it("no component takes the outline away from a control in the keyboard order", () => {
    // The exceptions are not controls in the Tab order: `<main>` (the skip link's target, focused by script) and the empty
    // hour cells of the calendar, which are deliberately left out of it (`tabIndex={-1}`).
    const offenders: string[] = [];
    const root = join(__dirname, "..");
    const walk = (dir: string) => {
      for (const entry of readdirSync(dir, { withFileTypes: true })) {
        const path = join(dir, entry.name);
        if (entry.isDirectory()) {
          walk(path);
        } else if (entry.name.endsWith(".tsx") && !entry.name.includes(".test.") && /outline-none|ring-\[3px\]/.test(readFileSync(path, "utf8"))) {
          offenders.push(path.slice(root.length + 1).replaceAll("\\", "/"));
        }
      }
    };
    walk(root);

    expect(offenders.sort()).toEqual(["components/main.tsx", "features/calendar/components/day-grid.tsx"]);
  });
});
