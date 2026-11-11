import { renderWithIntl } from "@/test/intl";
import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ResultNotice } from "./result-notice";

describe("ResultNotice", () => {
  it("takes focus when it appears, so a result far from the user is brought into view", () => {
    renderWithIntl(<ResultNotice kind="success">Reserva cancelada.</ResultNotice>);

    expect(screen.getByText("Reserva cancelada.")).toHaveFocus();
  });

  it("is not a live region: it is read because it has focus, not a second time because it appeared", () => {
    renderWithIntl(<ResultNotice kind="error">Não foi possível.</ResultNotice>);

    const notice = screen.getByText("Não foi possível.");
    expect(notice).not.toHaveAttribute("role");
    expect(notice).not.toHaveAttribute("aria-live");
  });

  it("takes focus again when the message changes", () => {
    const { rerender } = renderWithIntl(
      <>
        <button type="button">Outro</button>
        <ResultNotice kind="success">Falta marcada.</ResultNotice>
      </>,
    );
    screen.getByRole("button", { name: "Outro" }).focus();

    rerender(
      <>
        <button type="button">Outro</button>
        <ResultNotice kind="success">Falta anulada.</ResultNotice>
      </>,
    );

    expect(screen.getByText("Falta anulada.")).toHaveFocus();
  });

  it("tells success and error apart by colour and by data-kind, never by colour alone", () => {
    const { rerender } = renderWithIntl(<ResultNotice kind="error">Falhou.</ResultNotice>);
    expect(screen.getByText("Falhou.")).toHaveAttribute("data-kind", "error");
    expect(screen.getByText("Falhou.").className).toContain("text-destructive");

    rerender(<ResultNotice kind="success">Feito.</ResultNotice>);
    expect(screen.getByText("Feito.")).toHaveAttribute("data-kind", "success");
  });
});
