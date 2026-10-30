# 0007 · Slots calculados, não guardados

- **Estado:** Aceite.
- **Data:** 2026-10-30 (registo; a decisão vem das US08 e US17 e da T04).
- **Elementos:** D-PITCH, D-OPENDAY, D-PRICEBAND, D-BLOCK, D-BOOKING, D-SLOT, CC-CLUBTIME, CC-DAYCAL, CC-SLOTCHECK, API-SCHED, API-CATALOG, DB-BOOKINGS.

## Contexto

Um campo tem um horário semanal, uma duração de slot (30, 60 ou 90 minutos), bloqueios e reservas, e o jogador reserva «os slots livres» dos próximos 30 dias. Podia-se guardar uma linha por slot, ou gerá-los quando são precisos. O fuso `Europe/Lisbon` complica: dois dias do ano têm 23 e 25 horas.

Alternativas:

1. **Uma tabela de slots**, preenchida por antecipação (30 dias × campos × slots). Tem de ser regenerada a cada mudança de horário, de duração ou de bloqueio, e arrisca ficar fora de sincronia com o horário.
2. **Gerar os slots do horário quando são precisos.**

## Decisão

Opção 2:

- **Só se guardam os factos:** o horário (`pitch_opening_hours`, hora local e dia da semana), a duração do slot, os bloqueios, as faixas de preço e as reservas, que guardam o intervalo UTC `[starts_at, ends_at)` e o preço do momento. Não há tabela de slots.
- **Um só gerador:** `Pitch.SlotsOn(date, timeZone)` chama `SlotGenerator`, que corta o intervalo de abertura em blocos de **tempo real decorrido** desde a abertura; a conversão local↔UTC é só `ClubTime` (regras de mudança de hora da T04). Nos dias de mudança de hora há menos ou mais um slot, e cada um dura exatamente a sua duração.
- **Uma só definição de «livre»** (`PitchDayCalendar`, `DaySlot.IsFreeAt`) para a pré-visualização do dono, os slots públicos e a pesquisa; **uma só verificação de reservável** (`BookingSlotCheck`) para o jogador, o dono e as séries; **um só preço** (`Pitch.PriceForSlot`).
- Para reservar, o pedido leva o início (um instante com zona); o servidor tem de o encontrar entre os slots gerados (`SlotStartingAt`, senão 422 `invalid_slot`) e o fim vem sempre dali.

## Consequências

**Boas**

- **Mudar o horário, a duração ou as faixas de preço nunca toca nas reservas**: uma reserva fora do novo horário continua válida (US08), e cada uma guarda o seu preço.
- Nada a regenerar nem a sincronizar; o horário é a única fonte.
- A conta é igual em todo o lado, e a exclusion constraint ([ADR 0003](0003-exclusion-constraint-reservas.md)) trata das sobreposições entre reservas independentemente da grelha.

**Más ou a vigiar**

- Cada consulta de slots recalcula e junta bloqueios e reservas (uma leitura de cada para todos os campos pedidos; a pesquisa só calcula para os clubes da página).
- Uma mudança de duração ou de horário pode deixar reservas confirmadas que já não coincidem com a grelha nova; não há migração delas, de propósito.
- Um horário que passa da meia-noite não é suportado (limitação conhecida do MVP): o fecho mais tarde é `00:00`.
