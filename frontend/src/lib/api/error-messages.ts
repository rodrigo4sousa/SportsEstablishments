import { NETWORK_ERROR } from "./client";

/** User-facing message for each stable API error `code` (docs/stack.md: the front end picks the message by code). */
const MESSAGES: Record<string, string> = {
  invalid_credentials: "Email ou palavra-passe incorretos.",
  invalid_password: "A palavra-passe está incorreta.",
  owner_has_clubs: "Uma conta com clubes não pode ser apagada aqui. Contacta a administração:",
  admin_cannot_delete_account: "A conta de administração não pode ser apagada.",
  club_not_found: "Este clube não existe ou não é teu.",
  pitch_not_found: "Este campo já não existe neste clube.",
  block_not_found: "Este bloqueio já não existe.",
  block_overlap: "Este período sobrepõe-se a outro bloqueio deste campo.",
  block_already_started: "Um bloqueio que já começou não pode ser removido.",
  series_unavailable: "Nenhuma das datas escolhidas está disponível, por isso não foi feita nenhuma reserva.",
  price_band_not_found: "Esta faixa de preço já não existe.",
  price_band_overlap: "Esta faixa sobrepõe-se a outra faixa de preço deste campo num dos dias escolhidos.",
  price_band_limit_reached: "Um campo pode ter no máximo 20 faixas de preço.",
  slot_taken: "Este horário acabou de ser reservado. Escolhe outro.",
  slot_blocked: "Este horário não está disponível. Escolhe outro.",
  slot_started: "Este horário já começou. Escolhe outro.",
  invalid_slot: "Este horário já não existe. Escolhe outro.",
  outside_booking_window: "Só é possível reservar de hoje até 30 dias à frente.",
  pitch_inactive: "Este campo já não está a aceitar reservas.",
  club_unavailable: "Este clube já não está a aceitar reservas.",
  booking_not_found: "Esta reserva já não existe.",
  booking_already_started: "Esta reserva já começou, por isso já não pode ser cancelada.",
  booking_not_ended: "Só é possível marcar falta depois de a reserva terminar.",
  booking_not_no_show: "Esta reserva não está marcada como falta.",
  booking_not_confirmed: "Esta reserva já não está confirmada, por isso não pode ser cancelada.",
  outside_cancellation_window: "O prazo para cancelar esta reserva já terminou.",
  invalid_or_expired_reset_link: "Esta ligação já não é válida: expirou ou já foi usada. Pede uma nova.",
  owner_not_found: "Este dono já não existe.",
  owner_already_deactivated: "Este dono já está desativado.",
  owner_not_deactivated: "Este dono não está desativado.",
  forbidden: "Não tens permissão para fazer esta ação.",
  email_taken: "Já existe uma conta com este email.",
  terms_not_accepted: "Para criar conta, tens de aceitar os Termos e Condições e tomar conhecimento da Política de Privacidade.",
  validation_failed: "Há campos com erros. Corrige-os e tenta novamente.",
  origin_not_allowed: "O pedido foi recusado. Recarrega a página e tenta novamente.",
  too_many_requests: "Fizeste demasiados pedidos seguidos. Espera um pouco e tenta novamente.",
  [NETWORK_ERROR]: "Não foi possível contactar o servidor. Verifica a ligação e tenta novamente.",
};

const FALLBACK = "Ocorreu um erro inesperado. Tenta novamente mais tarde.";

export function errorMessage(code: string): string {
  return MESSAGES[code] ?? FALLBACK;
}
