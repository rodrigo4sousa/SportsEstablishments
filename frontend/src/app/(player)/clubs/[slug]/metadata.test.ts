import { beforeEach, describe, expect, it, vi } from "vitest";
import type { PublicClub } from "@/features/catalog/types";

const getPublicClub = vi.fn<(slug: string) => Promise<PublicClub | null>>();
vi.mock("@/lib/server/public-club", () => ({ getPublicClub: (slug: string) => getPublicClub(slug), loadPublicClub: vi.fn() }));
vi.mock("next/navigation", async () => import("@/test/navigation"));

const { generateMetadata } = await import("./page");

const params = (slug: string) => ({ params: Promise.resolve({ slug }) }) as Parameters<typeof generateMetadata>[0];

describe("the title of a club page (UX-011)", () => {
  beforeEach(() => getPublicClub.mockReset());

  it("is the club's name, so the tab and the history say which club it is", async () => {
    getPublicClub.mockResolvedValue({ name: "Arena Futsal Boavista", slug: "arena-futsal-boavista" } as PublicClub);

    expect(await generateMetadata(params("arena-futsal-boavista"))).toEqual({ title: "Arena Futsal Boavista" });
  });

  it("says the club was not found when the API has no such club", async () => {
    getPublicClub.mockResolvedValue(null);

    expect(await generateMetadata(params("clube-que-nao-existe"))).toEqual({ title: "Clube não encontrado" });
  });

  it("does not ask the API about a slug that cannot exist", async () => {
    expect(await generateMetadata(params("Não é um slug!"))).toEqual({ title: "Clube não encontrado" });
    expect(getPublicClub).not.toHaveBeenCalled();
  });
});
