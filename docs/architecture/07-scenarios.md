# 07 · Cenários

<!-- Vista 4+1: cenários (+1) · diagramas dinâmicos do C4 por caso de uso. Ver README.md. -->

Sete casos de uso que atravessam várias vistas e as validam: se um cenário não se conseguir contar com os elementos e os diagramas das vistas 01 a 06, falta alguma coisa nessas vistas. Cada cenário diz quem participa, os passos pela ordem em que acontecem, as regras que decidem o resultado e onde ver a mesma coisa desenhada. Não repete o detalhe: aponta para ele. Os ids dos elementos estão no [inventário](00-inventory.md) (secção 11 para os cenários, `SC-`); as regras completas estão no [backlog](../backlog.md) (US e T) e no [AGENTS.md](../../AGENTS.md).

| Id | Cenário | Atores | Histórias |
| --- | --- | --- | --- |
| [SC-01](#sc-01--visitante-pesquisa-e-reserva-um-slot) | Visitante pesquisa e reserva um slot | P-VIS → P-JOG | US16, US17, US18, US01, US03, US22 |
| [SC-02](#sc-02--dono-configura-clube-campos-horários-e-preços) | Dono configura clube, campos, horários e preços | P-DONO | US02, US06, US07, US08, US09, US10 |
| [SC-03](#sc-03--dono-cancela-uma-reserva-no-calendário) | Dono cancela uma reserva no calendário | P-DONO, P-JOG | US11, US12, US22 |
| [SC-04](#sc-04--reserva-semanal-com-uma-data-saltada) | Reserva semanal com uma data saltada | P-JOG | US20, US18 |
| [SC-05](#sc-05--recuperação-da-palavra-passe) | Recuperação da palavra-passe | P-VIS (com conta) | US05 |
| [SC-06](#sc-06--admin-desativa-um-dono) | Admin desativa um dono | P-ADMIN, P-DONO | US29, US04 |
| [SC-07](#sc-07--corrida-pelo-mesmo-slot) | Corrida pelo mesmo slot | P-JOG, P-JOG | T03, US18 |

## SC-01 · Visitante pesquisa e reserva um slot

**Atores.** Um visitante sem sessão (P-VIS) que depois entra como jogador (P-JOG); C-WEB, C-API, C-DB; no fim, o worker (C-WORKER) e o servidor de email (X-SMTP).

**Passos**

1. O visitante abre `/` (`ClubSearch`, WEB-F-CATALOG) e filtra por desporto, localidade e data. O filtro vive no endereço (`?sport=&locality=&date=&page=`). O browser chama `GET /api/clubs` (API-CATALOG), 20 clubes por página.
2. Abre um clube: `/clubs/{slug}` é um Server Component que lê a API pelo URL interno, sem cookies (WEB-SERVER, `loadPublicClub`). Um clube desconhecido ou escondido é o mesmo 404.
3. Escolhe campo e data; o browser lê os slots (`GET /api/clubs/{slug}/pitches/{pitchId}/slots?date=`). Cada slot tem um só indicador `available` e o preço da API; nunca se diz porque está indisponível. Escolher um slot escreve `?pitch=&date=&slot=` no endereço.
4. Sem sessão, o resumo mostra «Entrar para reservar»: vai para `/login?next=<este endereço>` (ou para o registo, que leva o mesmo `next`). `/login` tenta primeiro um refresh silencioso e depois mostra o formulário; o login responde com os dois cookies e o jogador volta à mesma escolha (`safeNext`).
5. O jogador confirma: `POST /api/bookings` com só `{ pitchId, startsAt }`.
6. A API valida, confirma que o dono está ativo, tranca o campo, volta a ler o campo, verifica o slot (`BookingSlotCheck`), calcula o preço (`Pitch.PriceForSlot`) e insere a reserva; na mesma transação entram no outbox a confirmação, o aviso ao dono (se o clube o quer) e o lembrete (3 h antes, se ainda for futuro). Resposta 201 com `Location`.
7. O browser mostra a confirmação e invalida `["catalog"]` e `["my-bookings"]`, por isso o slot passa a indisponível e a reserva aparece em `/bookings`.
8. Em segundo plano, o worker lê o outbox, compõe o email em pt-PT e envia-o (SMTP; em desenvolvimento, o Mailpit).

**Regras**

- Só se vê o que é público: um clube precisa de dono ativo e de um campo ativo; as respostas não levam dados de jogadores nem de donos; as leituras públicas têm limite por IP (120 por minuto, provisório).
- Reservas de hoje até 30 dias à frente, na data de Lisboa; o slot tem de existir na grelha gerada e não pode ter começado nem estar bloqueado ([ADR 0007](adr/0007-slots-calculados.md)).
- Só um jogador reserva; dono e admin veem os slots só de leitura.
- A reserva guarda o preço do momento; sem pagamentos.
- A sobreposição com outra reserva fica para a base de dados ([ADR 0003](adr/0003-exclusion-constraint-reservas.md); ver SC-07).
- O email nunca bloqueia a reserva e só existe se a reserva foi confirmada ([ADR 0004](adr/0004-outbox-para-emails.md)).
- A sessão vive em cookies httpOnly ([ADR 0002](adr/0002-sessao-em-cookies-httponly.md)).

**Diagramas**

- [Contexto](diagrams/01-context.svg) e [contentores](diagrams/02-containers-dev.svg): quem fala com quem.
- [Front end: rotas, guardas e caminhos](diagrams/03-logical-web.svg) e [áreas e funcionalidades](diagrams/03-logical-web-features.svg).
- [Componentes das reservas](diagrams/03-logical-api-bookings.svg), [código dos slots](diagrams/03-logical-code-slots.svg) e [estados de uma reserva](diagrams/03-logical-state-booking.svg).
- [Pipeline de um pedido](diagrams/04-process-pipeline.svg), [login](diagrams/04-process-login.svg), [refresh no browser](diagrams/04-process-refresh-client.svg), [reservar](diagrams/04-process-book.svg) e [worker de emails](diagrams/04-process-outbox.svg) ([vista de processo, 2.1](04-process-view.md#21-jogador-reserva-um-slot)).

## SC-02 · Dono configura clube, campos, horários e preços

**Atores.** Um dono (P-DONO); C-WEB, C-API, C-DB.

**Passos**

1. A pessoa regista-se como dono (`/register/owner`, `POST /api/auth/register/owner`); a conta nasce com o perfil `Owner` e a sessão começa logo. Vai para `/manage`, que, sem clubes, mostra «Criar o primeiro clube».
2. **Clube:** `/manage/clubs/new` envia `POST /api/manage/clubs` (nome, morada, localidade, contactos, janela de cancelamento, por omissão 24 h, e o aviso de novas reservas). A API gera o `slug` a partir do nome, que não muda se o nome mudar.
3. **Campo:** em `/manage/clubs/{id}`, `PitchSection` cria o campo (`POST …/pitches`): desporto, nome único no clube, preço base (> 0 e ≤ 500 €). O campo nasce ativo, com slots de 60 minutos e **fechado todos os dias**.
4. **Horário:** o botão «Horário» abre o `ScheduleEditor`; `PUT …/schedule` substitui a semana toda (sete dias, abertura e fecho de 30 em 30 minutos, ou fechado; fecho `00:00` = meia-noite) e a duração do slot (30, 60 ou 90). O `SlotPreview` mostra os slots de um dia, de hoje a hoje + 30, gerados a partir do horário.
5. **Preços:** o botão «Faixas de preço» cria faixas (`POST …/price-bands`) com dias, horas e preço. Sem faixa, o slot custa o preço base. A pré-visualização passa a mostrar o preço de cada slot.
6. **Bloqueios (opcional):** o botão «Bloqueios» cria períodos sem reservas (`POST …/blocks`), em hora de Lisboa e de 30 em 30 minutos.
7. Com o campo ativo e o dono ativo, o clube passa a aparecer na pesquisa pública (SC-01).

**Regras**

- Cada pedido do dono parte de `OwnedClubs`/`OwnedPitches` (T02): o clube é resolvido antes de validar o corpo, e o clube de outro dono dá o mesmo 404 `club_not_found` que um inexistente.
- O campo nunca se apaga: desativa-se (e só se não houver reservas confirmadas por terminar).
- Mudar o horário, a duração ou as faixas não altera reservas já feitas ([ADR 0007](adr/0007-slots-calculados.md)). Um horário que passa da meia-noite é recusado (limitação do MVP).
- Faixas: no máximo 20 por campo e sem sobreposição no mesmo dia (tocar é permitido): 409 `price_band_overlap`. Faixas e bloqueios mudam sob o trinco do campo, como as reservas.
- Um bloqueio recusa-se se sobrepõe outro bloqueio (409 `block_overlap`) ou reservas confirmadas (409 `block_overlaps_bookings`); só se remove antes de começar.
- O preço de um slot só se decide em `Pitch.PriceForSlot`; o browser mostra o que a API devolve.

**Diagramas**

- [Componentes da API, visão geral](diagrams/03-logical-api-overview.svg) (API-CLUBS, API-PITCHES, API-SCHED, API-BLOCKS, API-PRICE) e [contas](diagrams/03-logical-api-accounts.svg) (o registo).
- [Domínio das reservas](diagrams/03-logical-domain-booking.svg) (clube, campo, dia de abertura, faixa, bloqueio) e [código dos slots](diagrams/03-logical-code-slots.svg).
- [Front end](diagrams/03-logical-web.svg) e [funcionalidades](diagrams/03-logical-web-features.svg) (WEB-F-CLUBS, WEB-F-PITCHES).
- [Pipeline](diagrams/04-process-pipeline.svg) e [página com guarda](diagrams/04-process-guard.svg) (`/manage` exige o perfil `Owner`).

## SC-03 · Dono cancela uma reserva no calendário

**Atores.** O dono (P-DONO) e o jogador da reserva (P-JOG, só como destinatário); C-WEB, C-API, C-DB, C-WORKER.

**Passos**

1. O dono abre `/manage/clubs/{id}/calendar` (a vista, a data e a reserva aberta estão no endereço). O browser lê `GET …/calendar?from=&to=` do dia, com os campos em colunas, e `layoutDay` posiciona tudo por tempo real decorrido (um dia de mudança de hora tem 23 ou 25 linhas).
2. Clica numa reserva: abrem-se os detalhes (`GET …/bookings/{bookingId}`), com o nome do jogador (nunca o email nem o id), `canCancel` e `playerNoShows`.
3. «Cancelar reserva» abre o formulário do motivo (obrigatório, até 200 caracteres); envia `POST …/bookings/{bookingId}/cancel`.
4. A API resolve o clube do dono (404 `club_not_found`) e a reserva dentro dele (404 `booking_not_found`), valida o motivo (400), tranca o campo, volta a ler a reserva e chama `Booking.CancelByClub(reason, now)`. Se está confirmada e ainda não começou, passa a `CancelledByClub`, guarda o motivo e `cancelled_at`, e entra no outbox o email ao jogador, tudo na mesma transação.
5. A resposta é 200; o browser invalida `["calendar", clubId]`, e a reserva sai do dia (ou aparece como cancelada, com «Mostrar reservas canceladas»).
6. O slot fica livre de imediato: a exclusion constraint só cobre `Confirmed`.
7. O worker envia ao jogador o email com quem cancelou e o motivo; na lista «As minhas reservas» a reserva aparece como «Cancelada pelo clube».

**Regras**

- Sem janela de cancelamento: o dono pode cancelar até ao início; começar agora conta como começado (422 `booking_already_started`). Uma reserva já não confirmada é 422 `booking_not_confirmed`, verificada primeiro.
- O cancelamento do dono e o do jogador tomam o mesmo trinco: quem chega primeiro ganha e o outro recebe 422.
- Uma reserva feita pelo dono para um cliente sem conta (US13) cancela-se da mesma maneira, mas não gera email.
- O dono vê do jogador só o nome ([ADR 0005](adr/0005-marketplace-unico.md)).
- O ecrã nunca calcula se se pode cancelar: segue o `canCancel` da API.
- O email só existe se o cancelamento foi confirmado ([ADR 0004](adr/0004-outbox-para-emails.md)).

**Diagramas**

- [Dono cancela](diagrams/04-process-cancel-club.svg) ([vista de processo, 2.3](04-process-view.md#23-cancelamentos)) e, para comparar, [jogador cancela](diagrams/04-process-cancel-player.svg).
- [Estados de uma reserva](diagrams/03-logical-state-booking.svg), [componentes das reservas](diagrams/03-logical-api-bookings.svg) e [código do agregado Booking](diagrams/03-logical-code-booking.svg).
- [Worker de emails](diagrams/04-process-outbox.svg) e [estados da mensagem do outbox](diagrams/03-logical-state-outbox.svg).
- [Front end](diagrams/03-logical-web.svg) (WEB-F-CALENDAR) e [pipeline](diagrams/04-process-pipeline.svg).

## SC-04 · Reserva semanal com uma data saltada

**Atores.** Um jogador com sessão (P-JOG); C-WEB, C-API, C-DB, C-WORKER.

**Passos**

1. No resumo de uma reserva (SC-01, passo 4), o jogador escolhe «Repetir semanalmente» com 3 semanas. O endereço ganha `?weeks=3`.
2. O browser chama `POST /api/bookings/recurring/preview`. A API calcula as três datas (a primeira como foi pedida; as outras na mesma data local + 7 dias, à mesma hora local de Lisboa), verifica cada uma com as regras de uma reserva simples e devolve, por data, o preço e `free` ou `unavailable` com o motivo. Suponha-se que a 2.ª data tem um bloqueio (`blocked`).
3. A lista mostra as três datas e o motivo da que não se pode reservar, em palavras. O botão diz «Confirmar 2 reservas».
4. O jogador confirma: `POST /api/bookings/recurring`.
5. A API tenta cada data **em ordem, cada uma na sua transação** (tranca o campo, verifica, preço, insere; entram o aviso ao dono e o lembrete dessa reserva). A 1.ª e a 3.ª ficam criadas; a 2.ª é saltada com o motivo `blocked`. Todas as reservas criadas partilham o `SeriesId`.
6. Depois das datas, um `SaveChanges` à parte grava um só email, `booking_series_confirmed`, com as reservas feitas.
7. Resposta 201 com `created` e `skipped`. `RecurringConfirmation` lista as reservas e as datas que ficaram de fora; invalida `["catalog"]` e `["my-bookings"]`.
8. Em «As minhas reservas», cada reserva mostra «Reserva semanal: 1 de 2» e «2 de 2» (só contam as criadas).

**Regras**

- De 2 a 4 semanas (1 é uma reserva simples). A série **não é atómica**: cada data é independente, outra pessoa pode reservar uma data entre duas, e a pré-visualização é só indicativa.
- Motivos de uma data recusada: campo inativo, fora da janela de 30 dias, dia fechado, hora que não é um slot, já começada, bloqueada, ou ocupada (`taken`, que vem da exclusion constraint, 23P01).
- Se nenhuma data se reservar: 409 `series_unavailable` com as datas e os motivos.
- Uma série que atravessa a mudança de hora mantém a hora de Lisboa.
- O resumo é gravado fora da transação das datas: se a API cair entre os dois passos, as reservas ficam e o resumo perde-se (decisão provisória da US20).
- O cancelamento é sempre de uma reserva de cada vez (`POST /api/bookings/{id}/cancel`); não há um endpoint para cancelar a série.

**Diagramas**

- [Série semanal](diagrams/04-process-series.svg) ([vista de processo, 2.2](04-process-view.md#22-série-semanal)) e, por data, [reservar](diagrams/04-process-book.svg).
- [Componentes das reservas](diagrams/03-logical-api-bookings.svg) (API-BOOK-SERIES) e [código dos slots](diagrams/03-logical-code-slots.svg).
- [Worker de emails](diagrams/04-process-outbox.svg) e [front end](diagrams/03-logical-web-features.svg) (WEB-F-BOOKING, WEB-F-MYBOOK).

## SC-05 · Recuperação da palavra-passe

**Atores.** Uma pessoa com conta de jogador ou de dono que se esqueceu da palavra-passe; C-WEB, C-API, C-DB, C-WORKER, X-SMTP.

**Passos**

1. Em `/login`, «Esqueci-me da palavra-passe» leva a `/forgot-password`; a pessoa escreve o email.
2. `POST /api/auth/password-reset/request` responde sempre o mesmo 202, exista a conta ou não (só um email mal escrito é 400). A página mostra sempre a mesma mensagem.
3. Para uma conta ativa de jogador ou de dono, `PasswordResetStore` tranca a linha da conta e, se não houver já um pedido com menos de 5 minutos, põe no outbox uma mensagem `password_reset` com **só o id da conta**. Para uma conta inexistente, desativada ou de admin, não faz nada.
4. O worker lê a mensagem, confirma que a conta continua elegível, gera o token com o Identity (chaves do Data Protection na base de dados) e compõe a ligação `/reset-password?email=&token=` com `PublicSite:BaseUrl`. O email é enviado.
5. A pessoa abre a ligação. `/reset-password` lê o email e o token do endereço e mostra o formulário (nova palavra-passe e confirmação).
6. `POST /api/auth/password-reset/confirm`: primeiro valida a nova palavra-passe (400 por campo); depois toda a ligação má (email desconhecido, token errado, expirado ou já usado) é o mesmo 400 `invalid_or_expired_reset_link`.
7. Se a ligação vale, numa transação: muda o hash e o security stamp (a ligação e as anteriores deixam de valer), revoga todos os refresh tokens da conta, limpa o bloqueio por tentativas e põe no outbox o aviso `password_changed`. Resposta 204; ninguém fica com sessão.
8. A página mostra o sucesso e a ligação para `/login`; o worker envia o aviso de que a palavra-passe mudou.

**Regras**

- Sem enumeração de contas e no máximo um email por conta de 5 em 5 minutos; limites de pedidos por IP nos dois passos.
- A ligação vale 1 hora e uma só vez. O token nunca está no outbox, nos logs nem no armazenamento do browser; `/reset-password` é servida com `Referrer-Policy: no-referrer` e `Cache-Control: no-store`.
- A palavra-passe tem pelo menos 8 caracteres, sem regras de composição.
- As chaves que protegem o token estão na base de dados, para a ligação sobreviver a um reinício ([ADR 0008](adr/0008-chaves-data-protection-na-base-de-dados.md)). O gerador de tokens do Identity lê o relógio do sistema: é a exceção ao `TimeProvider`.

**Diagramas**

- [Pedir nova palavra-passe](diagrams/04-process-reset-request.svg) e [definir a nova palavra-passe](diagrams/04-process-reset-confirm.svg) ([vista de processo, 2.5](04-process-view.md#25-recuperação-da-palavra-passe)).
- [Worker de emails](diagrams/04-process-outbox.svg) e [componentes das contas](diagrams/03-logical-api-accounts.svg) (API-AUTH-RESET, CC-DATAPROT).
- [Estados de uma mensagem do outbox](diagrams/03-logical-state-outbox.svg) e [login](diagrams/04-process-login.svg).

## SC-06 · Admin desativa um dono

**Atores.** O admin (P-ADMIN), o dono desativado (P-DONO) e, depois, jogadores e visitantes que deixam de o ver; C-WEB, C-API, C-DB.

**Passos**

1. O admin existe porque o seed o criou no arranque (`AdminSeed:*`); entra em `/login` e vai para `/admin` (o guarda e a política `Admin` mandam os outros perfis embora).
2. `/admin` mostra os totais e os gráficos (só contagens) e a lista de donos, a única parte com nomes e emails (de donos, nunca de jogadores). O admin abre o dono em `/admin/owners/{id}`.
3. A página mostra os números do dono, o estado e o histórico, e quantas reservas futuras confirmadas ficam (`futureBookings`). «Desativar dono» abre o formulário do motivo (obrigatório, até 500 caracteres).
4. `POST /api/admin/owners/{id}/deactivate`: a API confirma que o id é de um dono (404 `owner_not_found`, antes de validar o corpo) e valida o motivo.
5. Numa transação: o `UPDATE users SET deactivated_at = now WHERE id = … AND deactivated_at IS NULL` decide (0 linhas = 422 `owner_already_deactivated`); com 1 linha grava-se a linha de auditoria em `owner_status_changes` (admin vindo da sessão, nunca do pedido) e revogam-se todos os refresh tokens do dono. Resposta 200.
6. O browser invalida tudo o que está sob `["admin"]`; o dono aparece desativado e o histórico ganha a linha.
7. **Efeitos.** O dono não consegue entrar (o mesmo 401 `invalid_credentials`); o refresh da sessão que tinha dá 401; os seus clubes saem das páginas públicas (404); ninguém pode reservar nos seus campos (422 `club_unavailable`); os avisos por enviar ao dono são ignorados. As reservas já feitas ficam confirmadas.
8. Reativar é a mesma sequência com a condição inversa, sem revogar nada.

**Regras**

- O admin vê só contagens; nenhum nome, email, telemóvel ou id de jogadores.
- Só o seed dá o perfil Admin; a palavra-passe do admin muda-se com o comando `set-admin-password`.
- O histórico é só de acrescentar (trigger `owner_status_changes_append_only`, 23001) e as chaves estrangeiras são `RESTRICT`.
- **Compromisso aceite:** o access token já emitido continua válido até 15 minutos, por isso o dono pode ainda usar a área de gestão nesse intervalo ([ADR 0002](adr/0002-sessao-em-cookies-httponly.md)).
- Nenhuma reserva é cancelada pela desativação.

**Diagramas**

- [Desativar um dono](diagrams/04-process-owner-deactivation.svg) ([vista de processo, 2.8](04-process-view.md#28-desativar-um-dono)) e [estados de um dono](diagrams/03-logical-state-owner.svg).
- [Refresh na API](diagrams/04-process-refresh.svg) (recusa a conta desativada), [login](diagrams/04-process-login.svg) e [componentes das contas](diagrams/03-logical-api-accounts.svg).
- [Domínio das contas](diagrams/03-logical-domain-accounts.svg) (`OwnerStatusChange`) e [página com guarda](diagrams/04-process-guard.svg).

## SC-07 · Corrida pelo mesmo slot

**Atores.** Dois jogadores (P-JOG) com sessão, A e B, que confirmam o mesmo slot ao mesmo tempo; C-API, C-DB, C-WORKER.

**Passos**

1. A e B veem o slot livre e carregam em «Confirmar reserva» no mesmo instante. Chegam à API dois `POST /api/bookings` iguais, cada um no seu pedido, com o seu `AppDbContext` e a sua ligação.
2. Os dois passam a validação, a verificação do campo e a do dono ativo, que não precisam de trinco.
3. Os dois pedem o trinco do campo: `SELECT … FOR UPDATE` na linha do campo. A obtém-no; B espera.
4. A, dentro do trinco, relê o campo, verifica o slot (livre de bloqueios e não começado), calcula o preço, junta as mensagens ao outbox e insere. O `COMMIT` grava a reserva e as mensagens e liberta o trinco. Resposta 201.
5. B obtém o trinco, relê o campo e verifica o slot; **a verificação não procura outras reservas**. Tenta inserir.
6. A exclusion constraint `bookings_no_overlap` recusa (SQLSTATE 23P01). O `BookingStore` desliga as entradas novas (a reserva e os emails de B), regista o caso em Information, sem o jogador, e a API responde 409 `slot_taken`. Nenhum email de B fica no outbox.
7. O browser de B fecha o resumo, mostra a mensagem e recarrega os slots; o slot já aparece indisponível, sem dizer porquê.

**Variantes com o mesmo trinco**

- *Reserva contra bloqueio* (dono e jogador): ambos tomam o trinco; o que vem depois vê o outro, e responde 409 `block_overlaps_bookings` (o bloqueio) ou 409 `slot_blocked` (a reserva).
- *Dois cancelamentos da mesma reserva* (jogador e dono, ou dois separadores): o segundo relê a reserva já cancelada e recebe 422 `booking_not_confirmed`; um só email.
- *Anular uma falta* depois de outra pessoa ter reservado o slot: 409 `slot_taken`, pela mesma constraint.
- *Duas datas de uma série* contra uma reserva de outro jogador: a data fica `taken` e é saltada (SC-04).

**Regras**

- A garantia é da base de dados, não do código: nenhuma verificação na aplicação procura sobreposição entre reservas ([ADR 0003](adr/0003-exclusion-constraint-reservas.md)). O trinco serializa o que a constraint não cobre (bloqueios, cancelamentos) e faz com que a releitura veja o que o pedido anterior confirmou.
- A constraint cobre só `Confirmed`: um slot cancelado fica livre.
- Perder a corrida é esperado: 409 e Information, nunca 500 nem Error.
- O email e a reserva vivem e morrem juntos ([ADR 0004](adr/0004-outbox-para-emails.md)).
- O teste da T03 lança 20 pedidos simultâneos para o mesmo slot contra um PostgreSQL real e espera exatamente um 201 e os restantes 409.

**Diagramas**

- [Reservar](diagrams/04-process-book.svg) (com as falhas) ([vista de processo, 2.1](04-process-view.md#21-jogador-reserva-um-slot)) e a [tabela de concorrência](04-process-view.md#3-concorrência-o-que-está-protegido-e-o-que-não-está).
- [Cancelar (jogador)](diagrams/04-process-cancel-player.svg), [cancelar (dono)](diagrams/04-process-cancel-club.svg) e [série semanal](diagrams/04-process-series.svg).
- [Estados de uma reserva](diagrams/03-logical-state-booking.svg), [componentes das reservas](diagrams/03-logical-api-bookings.svg) e [domínio das reservas](diagrams/03-logical-domain-booking.svg).

## Notas

- **Substitui** os quatro títulos vazios que este ficheiro tinha: «Jogador reserva um slot» é agora SC-01, «Jogador reserva uma série semanal» é SC-04, «Dono cancela uma reserva com motivo» é SC-03 e «Admin desativa um dono» é SC-06. Os outros três (SC-02, SC-05, SC-07) são novos.
- **Ids novos:** `SC-01` a `SC-07`, no [inventário](00-inventory.md), secção 11. Nenhum diagrama novo: cada cenário reutiliza os das vistas 01 a 06.
- **O que não está desenhado como sequência:** a configuração do clube (SC-02) e a vista do calendário (SC-03, passos 1 e 2) são pedidos simples de leitura e escrita sob o pipeline normal; as suas regras estão no texto e nos diagramas de componentes e de domínio.
