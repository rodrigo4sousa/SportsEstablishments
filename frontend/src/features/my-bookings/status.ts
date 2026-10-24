/** The pt-PT label of each booking status code (backlog: Confirmada, Cancelada pelo jogador, Cancelada pelo clube, Falta). */
const STATUS_LABELS: Record<string, string> = {
  confirmed: "Confirmada",
  cancelled_by_player: "Cancelada pelo jogador",
  cancelled_by_club: "Cancelada pelo clube",
  no_show: "Falta",
};

/** An unknown code (a newer API) is shown as it is. */
export function statusLabel(code: string): string {
  return STATUS_LABELS[code] ?? code;
}
