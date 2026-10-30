import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useRef, useState } from "react";
import { describe, expect, it } from "vitest";
import { useFocusOn, useRestoreFocus } from "./focus";

function Result({ trigger }: { trigger?: unknown }) {
  const focus = useFocusOn<HTMLParagraphElement>(trigger);
  return <p {...focus}>Feito</p>;
}

describe("useFocusOn", () => {
  it("focuses the element when it first appears, and makes it focusable without putting it in the Tab order", () => {
    render(<Result />);

    const result = screen.getByText("Feito");
    expect(result).toHaveFocus();
    expect(result).toHaveAttribute("tabindex", "-1");
  });

  it("does not focus while the trigger is falsy, and does when it becomes truthy", () => {
    const { rerender } = render(<Result trigger={false} />);
    expect(screen.getByText("Feito")).not.toHaveFocus();

    rerender(<Result trigger />);

    expect(screen.getByText("Feito")).toHaveFocus();
  });

  it("focuses again when the trigger changes (a new result replacing the old one)", async () => {
    const { rerender } = render(
      <>
        <button type="button">Outro</button>
        <Result trigger="primeiro" />
      </>,
    );
    await userEvent.click(screen.getByRole("button", { name: "Outro" }));
    expect(screen.getByText("Feito")).not.toHaveFocus();

    rerender(
      <>
        <button type="button">Outro</button>
        <Result trigger="segundo" />
      </>,
    );

    expect(screen.getByText("Feito")).toHaveFocus();
  });

  it("does not make the element a live region, so a screen reader reads it once (focus), not twice", () => {
    render(<Result />);

    const result = screen.getByText("Feito");
    expect(result).not.toHaveAttribute("role");
    expect(result).not.toHaveAttribute("aria-live");
  });
});

function Opener() {
  const [open, setOpen] = useState(false);
  const opener = useRestoreFocus<HTMLButtonElement>(open);
  return open ? (
    <div>
      <p>Tens a certeza?</p>
      <button type="button" onClick={() => setOpen(false)}>
        Não
      </button>
    </div>
  ) : (
    <button ref={opener} type="button" onClick={() => setOpen(true)}>
      Abrir
    </button>
  );
}

describe("useRestoreFocus", () => {
  it("puts focus back on the control that opened something, when closing it left focus on the page", async () => {
    render(<Opener />);
    await userEvent.click(screen.getByRole("button", { name: "Abrir" }));

    await userEvent.click(screen.getByRole("button", { name: "Não" }));

    expect(screen.getByRole("button", { name: "Abrir" })).toHaveFocus();
  });

  it("leaves focus alone when something else took it as the thing closed (a result message)", async () => {
    function TakenByResult() {
      const [open, setOpen] = useState(true);
      const opener = useRestoreFocus<HTMLButtonElement>(open);
      const result = useRef<HTMLParagraphElement>(null);
      return (
        <>
          <button ref={opener} type="button">
            Abrir
          </button>
          <p ref={result} tabIndex={-1}>
            Resultado
          </p>
          <button
            type="button"
            onClick={() => {
              result.current?.focus();
              setOpen(false);
            }}
          >
            Fechar
          </button>
        </>
      );
    }
    render(<TakenByResult />);

    await userEvent.click(screen.getByRole("button", { name: "Fechar" }));

    expect(screen.getByText("Resultado")).toHaveFocus();
  });
});
