import { NETWORK_ERROR } from "./client";

/** User-facing message for each stable API error `code` (docs/stack.md: the front end picks the message by code). */
const MESSAGES: Record<string, string> = {
  invalid_credentials: "Email ou palavra-passe incorretos.",
  club_not_found: "Este clube não existe ou não é teu.",
  pitch_not_found: "Este campo já não existe neste clube.",
  block_not_found: "Este bloqueio já não existe.",
  block_overlap: "Este período sobrepõe-se a outro bloqueio deste campo.",
  block_already_started: "Um bloqueio que já começou não pode ser removido.",
  slot_taken: "Este horário acabou de ser reservado. Escolhe outro.",
  slot_blocked: "Este horário não está disponível. Escolhe outro.",
  slot_started: "Este horário já começou. Escolhe outro.",
  invalid_slot: "Este horário já não existe. Escolhe outro.",
  outside_booking_window: "Só é possível reservar de hoje até 30 dias à frente.",
  pitch_inactive: "Este campo já não está a aceitar reservas.",
  club_unavailable: "Este clube já não está a aceitar reservas.",
  booking_not_found: "Esta reserva já não existe.",
  booking_not_confirmed: "Esta reserva já não está confirmada, por isso não pode ser cancelada.",
  outside_cancellation_window: "O prazo para cancelar esta reserva já terminou.",
  forbidden: "Não tens permissão para fazer esta ação.",
  email_taken: "Já existe uma conta com este email.",
  validation_failed: "Há campos com erros. Corrige-os e tenta novamente.",
  origin_not_allowed: "O pedido foi recusado. Recarrega a página e tenta novamente.",
  too_many_requests: "Fizeste demasiados pedidos seguidos. Espera um pouco e tenta novamente.",
  [NETWORK_ERROR]: "Não foi possível contactar o servidor. Verifica a ligação e tenta novamente.",
};

const FALLBACK = "Ocorreu um erro inesperado. Tenta novamente mais tarde.";

export function errorMessage(code: string): string {
  return MESSAGES[code] ?? FALLBACK;
}
