import { describe, expect, it } from "vitest";
import { isGuid } from "./guid";

describe("isGuid", () => {
  it.each(["0199f7a0-0000-7000-8000-0000000000aa", "0199F7A0-0000-7000-8000-0000000000AA"])("accepts %s", (value) => {
    expect(isGuid(value)).toBe(true);
  });

  it.each(["", "new", "123", "0199f7a0-0000-7000-8000-0000000000a", "0199f7a0-0000-7000-8000-0000000000aa/x", "../admin", " 0199f7a0-0000-7000-8000-0000000000aa"])(
    "rejects %j",
    (value) => {
      expect(isGuid(value)).toBe(false);
    },
  );
});
