# 0004 · Outbox para os emails

- **Estado:** Aceite.
- **Data:** 2026-10-30 (registo; a decisão vem da [Stack](../../stack.md) e da US22).
- **Elementos:** C-WORKER, C-DB, API-NOTIF, API-NOTIF-OUTBOX, API-NOTIF-DISPATCH, API-NOTIF-PROC, API-NOTIF-COMPOSE, API-NOTIF-SENDER, D-OUTBOX, DB-OUTBOX, DB-OUTBOXUQ, X-SMTP.

## Contexto

Reservar, cancelar e recuperar a palavra-passe têm de enviar emails (confirmações, avisos ao dono, lembretes, ligações de recuperação). O servidor SMTP pode estar lento ou em baixo, e o envio não deve atrasar nem fazer falhar a reserva. Mas o email também não se pode perder nem sair para uma reserva que afinal não foi gravada.

Alternativas:

1. **Enviar dentro do pedido.** Atrasa o pedido e uma falha do SMTP faz falhar uma reserva já feita (ou, se se ignora o erro, perde o email).
2. **Fila em memória ou «disparar e esquecer».** Perde-se tudo quando o processo pára, e não há como adiar um lembrete para daqui a horas.
3. **Uma tabela `outbox_messages` na mesma base de dados e um worker.**

## Decisão

Opção 3:

- **Escrever:** o serviço junta a `OutboxMessage` ao `IAppDbContext` do pedido **antes** do `SaveChanges`, dentro da transação do trinco do campo; a mensagem e a reserva gravam-se, ou desfazem-se, juntas. O payload nunca leva endereços: leva ids (a reserva, ou só a conta), e o destinatário e o texto são lidos no momento do envio.
- **Enviar:** `OutboxDispatcher` (um `BackgroundService` no processo da API) corre `OutboxProcessor.ProcessDueAsync` numa transação por lote, com `SELECT … FOR UPDATE SKIP LOCKED` (lote de 20, passagem de 10 s). Compõe o email em pt-PT na hora de Lisboa e envia por SMTP (MailKit, uma ligação por mensagem).
- **Falhas:** cada falha conta uma tentativa e adia a seguinte com intervalo a dobrar (1, 2, 4, 8, 16 minutos, no máximo 1 hora); à 6.ª a mensagem fica falhada e regista-se um Warning com o id e o tipo da exceção, nunca o endereço. Uma mensagem que já não se aplica (lembrete de uma reserva cancelada ou já começada, aviso a um dono desativado) é fechada com `Skip`, que não é uma falha.
- Um índice único parcial (DB-OUTBOXUQ) garante no máximo um lembrete e um aviso ao dono por reserva.

## Consequências

**Boas**

- Um email existe se e só se a alteração foi confirmada; uma reserva perdida na corrida não deixa email.
- Uma falha do SMTP nunca faz falhar um pedido, e o envio é retomado quando o servidor volta.
- Várias instâncias da API podem correr o worker sem pegar na mesma mensagem.
- Os testes não precisam de SMTP: correm o processador à mão com um `FakeEmailSender`.

**Más ou a vigiar**

- **Pelo menos uma vez:** o envio acontece antes do `COMMIT` do lote; se o processo parar entre os dois, as mensagens do lote voltam a estar pendentes e podem sair duas vezes.
- O worker vive no processo da API: em produção a API tem de estar sempre ligada, senão os emails param e os lembretes chegam tarde ou são ignorados.
- O resumo de uma série semanal é gravado depois das datas, numa transação à parte: uma queda entre os dois passos perde o resumo (decisão provisória da US20).
