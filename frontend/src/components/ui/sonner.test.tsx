import { renderWithIntl } from "@/test/intl";
import { act, screen } from "@testing-library/react";
import { toast } from "sonner";
import { beforeAll, describe, expect, it, vi } from "vitest";
import { Toaster } from "./sonner";

describe("the toaster", () => {
  // Our Toaster follows the colour scheme (theme "system"), which asks the browser through matchMedia; jsdom has none.
  beforeAll(() => {
    vi.stubGlobal(
      "matchMedia",
      vi.fn(() => ({ matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn(), addListener: vi.fn(), removeListener: vi.fn() })),
    );
  });

  it("names its region in pt-PT for assistive technology, not in Sonner's English (UX-024)", async () => {
    renderWithIntl(<Toaster />);

    act(() => {
      toast.error("Algo correu mal.");
    });

    expect(await screen.findByText("Algo correu mal.")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: /^Notificações/ })).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: /Notifications/ })).not.toBeInTheDocument();
  });
});
