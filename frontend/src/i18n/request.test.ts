import { beforeEach, describe, expect, it, vi } from "vitest";

let cookie: string | undefined;
let acceptLanguage: string | null;

vi.mock("next/headers", () => ({
  cookies: async () => ({ get: (name: string) => (name === "NEXT_LOCALE" && cookie !== undefined ? { name, value: cookie } : undefined) }),
  headers: async () => new Headers(acceptLanguage === null ? {} : { "accept-language": acceptLanguage }),
}));

/** The request config next-intl calls for every render: the language, its catalog and the Lisbon time zone (T09). */
async function config() {
  const create = (await import("./request")).default as unknown as () => Promise<{ locale: string; messages: { header: { brand: string }; skipLink: { label: string } }; timeZone: string }>;
  return create();
}

describe("the request config (T09)", () => {
  beforeEach(() => {
    cookie = undefined;
    acceptLanguage = null;
  });

  it("uses pt-PT without a cookie or an Accept-Language it knows", async () => {
    acceptLanguage = "de-DE";
    const result = await config();

    expect(result.locale).toBe("pt-PT");
    expect(result.messages.skipLink.label).toBe("Saltar para o conteúdo");
    expect(result.timeZone).toBe("Europe/Lisbon");
  });

  it("follows Accept-Language without a cookie", async () => {
    acceptLanguage = "en-US,en;q=0.9";
    const result = await config();

    expect(result.locale).toBe("en");
    expect(result.messages.skipLink.label).toBe("Skip to content");
  });

  it("puts the switcher's cookie before Accept-Language", async () => {
    acceptLanguage = "en-GB";
    cookie = "pt-PT";

    expect((await config()).locale).toBe("pt-PT");
  });
});
