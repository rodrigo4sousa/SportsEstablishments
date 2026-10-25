import { describe, expect, it } from "vitest";
import nextConfig from "../../../next.config";

describe("the reset-password page headers", () => {
  it("is served with Referrer-Policy: no-referrer and never cached, so the token in its address cannot leak", async () => {
    const rules = await nextConfig.headers!();

    const rule = rules.find((candidate) => candidate.source === "/reset-password");
    expect(rule).toBeDefined();
    expect(Object.fromEntries(rule!.headers.map((header) => [header.key, header.value]))).toEqual({
      "Referrer-Policy": "no-referrer",
      "Cache-Control": "no-store",
    });
  });
});
