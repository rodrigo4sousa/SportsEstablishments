import { createTranslator } from "next-intl";
import { describe, expect, it } from "vitest";
import en from "../../messages/en.json";
import pt from "../../messages/pt-PT.json";

type Catalog = { [key: string]: string | Catalog };

/** Every message of a catalog, by its dotted key. */
function leaves(catalog: Catalog, prefix = ""): Map<string, string> {
  const result = new Map<string, string>();
  for (const [key, value] of Object.entries(catalog)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (typeof value === "string") {
      result.set(path, value);
    } else {
      for (const [inner, text] of leaves(value, path)) result.set(inner, text);
    }
  }
  return result;
}

/** The ICU arguments of a message (`{name}`, `{count, plural, …}`): an identifier right after `{`, then `,` or `}`. */
function argumentsOf(message: string): string[] {
  return [...new Set([...message.matchAll(/\{\s*([A-Za-z_]\w*)\s*[,}]/g)].map((match) => match[1]))].sort();
}

/** The rich-text tags of a message (`<link>…</link>`). */
function tagsOf(message: string): string[] {
  return [...new Set([...message.matchAll(/<([A-Za-z]\w*)>/g)].map((match) => match[1]))].sort();
}

const ptMessages = leaves(pt);
const enMessages = leaves(en);

describe("the two catalogs (T09)", () => {
  it("have exactly the same keys", () => {
    const onlyPt = [...ptMessages.keys()].filter((key) => !enMessages.has(key));
    const onlyEn = [...enMessages.keys()].filter((key) => !ptMessages.has(key));
    expect({ onlyPt, onlyEn }).toEqual({ onlyPt: [], onlyEn: [] });
  });

  it("have no empty message", () => {
    const empty = [...ptMessages, ...enMessages].filter(([, text]) => text.trim() === "").map(([key]) => key);
    expect(empty).toEqual([]);
  });

  it("use the same ICU arguments and tags in both languages", () => {
    const different = [...ptMessages]
      .filter(([key, text]) => {
        const other = enMessages.get(key) ?? "";
        return argumentsOf(text).join() !== argumentsOf(other).join() || tagsOf(text).join() !== tagsOf(other).join();
      })
      .map(([key]) => key);
    expect(different).toEqual([]);
  });

  it.each([
    ["pt-PT", pt, ptMessages],
    ["en", en, enMessages],
  ] as const)("format every %s message without an error", (locale, messages, all) => {
    const errors: string[] = [];
    const t = createTranslator({ locale, messages, onError: (error) => errors.push(`${error.code}: ${error.message}`) });
    for (const [key, text] of all) {
      const values: Record<string, unknown> = {};
      for (const name of argumentsOf(text)) values[name] = 1;
      for (const tag of tagsOf(text)) values[tag] = (chunks: string) => chunks;
      t.markup(key as never, values as never);
    }
    expect(errors).toEqual([]);
  });

  it("are big enough to be the whole site (a key went missing otherwise)", () => {
    expect(ptMessages.size).toBeGreaterThan(900);
  });
});
