# 0011 · Apagar a conta por anonimização

- **Estado:** Aceite em 2026-11-10 (US30), com as decisões marcadas como provisórias no [backlog](../../backlog.md).
- **Data:** 2026-11-10.
- **Elementos:** API-AUTH, API-AUTH-PRIVACY, DB-USERS, DB-REFRESH, DB-BOOKINGS, DB-OUTBOX, D-BOOKING, WEB-F-ACCOUNT, DEV-E2E.

## Contexto

A T07 publicou a Política de Privacidade e deixou o apagamento e a portabilidade como um pedido por email, tratado à mão. A US30 passa-os para a aplicação. O que o repositório impõe:

- **As reservas nunca se apagam** e os clubes precisam do histórico; `bookings.player_id` aponta para `users`.
- **Um clube tem sempre um dono**, e `owner_status_changes` (só de acrescentar, com chaves `RESTRICT`) aponta para as contas.
- **Um email, uma conta**: o índice único em `normalized_email` tem de ficar livre para a pessoa se poder registar outra vez.
- **O access token não é verificado em cada pedido** (15 minutos aceites para uma conta desativada, US29). Quem apaga a conta espera que ela deixe de funcionar logo, em todos os dispositivos.

Alternativas:

1. **Apagar a linha de `users`.** Obriga a apagar ou a desligar tudo o que aponta para ela: perde o histórico dos clubes e colide com o `owner_status_changes`.
2. **Só desativar.** Os dados pessoais ficavam na base de dados: não é um apagamento.
3. **Anonimizar a linha.** Tudo o que aponta para ela continua válido, e ela deixa de dizer quem era a pessoa.

## Decisão

Opção 3, em `POST /api/me/delete-account`, com a palavra-passe outra vez:

- **Numa só transação** (`AccountPrivacyStore.DeleteAsync`): tranca a linha da conta (`FOR UPDATE`, que espera por uma reserva a ser inserida por via da chave estrangeira); volta a ver se tem clubes; substitui o nome por «Conta apagada» e o email e o nome de utilizador por `deleted-<id>@deleted.invalid` (`.invalid` não recebe correio, RFC 2606); apaga a palavra-passe, o telemóvel e o registo da aceitação; muda o security stamp; marca `deactivated_at` (todas as verificações de conta desativada passam a aplicar-se) e `deleted_at`; cancela as reservas futuras; apaga os refresh tokens e o resto do Identity; apaga os emails por enviar. As reservas passam a `CancelledByPlayer` por um `UPDATE` condicional, sem a janela de cancelamento: a pessoa não pode ficar presa a uma reserva de uma conta que já não existe.
- **O access token deixa de funcionar logo.** `OnTokenValidated` pergunta, em cada pedido autenticado, se a conta foi apagada (`IAccountStore.IsDeletedAsync`, uma leitura pela chave primária) e recusa-o. É só para o apagamento: uma conta desativada mantém os 15 minutos aceites na US29. Sem isto, um token noutro dispositivo poderia continuar a reservar em nome de uma conta apagada.
- **Recusas:** dono com clubes (422 `owner_has_clubs`, seja qual for o estado dos campos; os clubes não têm estado próprio) e admin (422 `admin_cannot_delete_account`). Vêm antes da palavra-passe: são sobre a conta, que a sessão já identifica. Uma palavra-passe errada é 400 `invalid_password` e não 401, porque para o cliente do browser um 401 é uma sessão expirada, que ele tentaria renovar; conta para o bloqueio, como no login.
- **Sem email de confirmação**, porque isso obrigaria a guardar o endereço. A confirmação aparece no ecrã e os cookies são limpos.
- **Exportação:** `GET /api/me/data-export` devolve um ficheiro JSON com o perfil, as reservas e a aceitação dos Termos, sem hashes, tokens, ids internos nem dados de outras pessoas. Limite por IP, como os outros endpoints sensíveis.
- **Interface:** a página `/account` («A minha conta»), para qualquer perfil, com a secção «Privacidade e dados» (WEB-F-ACCOUNT).
- **E2E:** um quarto fluxo em `e2e/`, além dos três da [ADR 0010](0010-testes-end-to-end-playwright.md), por ser uma operação irreversível que atravessa o site, os cookies e a base de dados. As regras de estabilidade dessa ADR mantêm-se.

## Consequências

- Uma coluna nova (`users.deleted_at`, opcional, compatível com a versão anterior da API) e, em cada pedido autenticado, uma leitura a mais pela chave primária.
- Uma conta apagada deixa de contar nos totais e na lista de donos do admin, mas continua nos gráficos de registos ao longo do tempo: o registo aconteceu.
- O que não se anonimiza, por não ser um campo da conta: um motivo escrito à mão por um clube ao cancelar ou por um admin ao desativar um dono (este último é só de acrescentar). A Política de Privacidade di-lo.
- Mantêm-se as mensagens do outbox já fechadas (enviadas, falhadas ou ignoradas): têm o id, que já não identifica ninguém, e dados do clube.
- Limite conhecido: um pedido que já passou a verificação do token e insere uma reserva depois do `COMMIT` (milissegundos) deixaria uma reserva confirmada numa conta apagada. O worker nunca envia um email para um endereço `deleted.invalid`.
