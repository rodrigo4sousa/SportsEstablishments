# Vista lógica

<!-- C4 níveis 3 (componentes) e 4 (código) · vista 4+1: lógica. Ver README.md. -->

Do mais geral ao mais detalhado: as camadas do back end, os componentes da API e do front end, o modelo do domínio, os estados por que passam as reservas, as mensagens do outbox e os donos, e por fim o código de duas peças centrais. Todos os nomes de classes, métodos e tabelas foram conferidos com o código; os ids estão no [inventário](../inventory.md).

## 1. Camadas

![Camadas do back end](../out/logical-layers.svg)

Fonte: [logical-layers.puml](logical-layers.puml).

| Nome | Tecnologia | Responsabilidade |
| --- | --- | --- |
| SportsBooking.Api | ASP.NET Core, controllers | Controllers finos, cookies e JWT, políticas por perfil, ProblemDetails, verificação do Origin, rate limiting. Program.cs só compõe. |
| SportsBooking.Infrastructure | EF Core, Npgsql, Identity, MailKit | AppDbContext, migrações, stores (I\*Store), PitchLock, sessões, Identity, envio SMTP, worker do outbox, comandos. |
| SportsBooking.Application | FluentValidation, EF Core (DbSet e LINQ, sem Npgsql) | Serviços por caso de uso, validação, Result/ServiceError, IAppDbContext e as interfaces que a Infrastructure implementa. |
| SportsBooking.Domain | C# sem dependências | Entidades com setters privados e métodos com intenção; regras de horas, slots e contas. |

**Regra de dependência.** As dependências apontam só para dentro: L-API → L-INF → L-APP → L-DOM. É o que dizem as referências dos projetos: `SportsBooking.Api` referencia só a Infrastructure (e chega à Application por ela), a Infrastructure referencia a Application, a Application referencia o Domain, e o Domain não referencia nada (nem pacotes). Cada camada regista-se a si mesma (`AddApplication()`, `AddInfrastructure(config)`); `Program.cs` só compõe.

| Id | Projeto | O que lá vive |
| --- | --- | --- |
| L-API | `SportsBooking.Api` | Controllers finos, `AuthCookies` e JwtBearer a ler o cookie, políticas por perfil, `ServiceErrorResults` (`Result` → ProblemDetails), `GlobalExceptionHandler`, verificação do `Origin`, rate limiting, `Program.cs` (arranque, migrações, seed do admin, comandos). |
| L-APP | `SportsBooking.Application` | Um serviço por caso de uso (`BookingService`, `ClubBookingService`, …), validadores FluentValidation, `Result<T>`/`ServiceError`, `IAppDbContext` e as interfaces que a Infrastructure implementa (`IAccountStore`, `ISessionService`, `IBookingStore`, `IPitchLock`, `IEmailSender`, …), as peças partilhadas (`OwnedClubs`, `VisibleClubs`, `PitchDayCalendar`, `BookingSlotCheck`) e a composição dos emails. |
| L-INF | `SportsBooking.Infrastructure` | `AppDbContext` e migrações, configurações EF, as stores (SQL próprio: 23P01, `SKIP LOCKED`, atualizações condicionais), `PitchLock`, Identity (`AppUser`, `AccountStore`), sessões (`SessionService`, `RefreshToken`), `SmtpEmailSender`, o worker do outbox, os comandos `seed-demo` e `set-admin-password`. |
| L-DOM | `SportsBooking.Domain` | Entidades (`Club`, `Pitch`, `Block`, `Booking`, …), valores e regras de tempo (`ClubTime`, `SlotGenerator`, `OpeningHours`, `TimeSlot`, `BookingWindow`), regras de contas (`Profiles`, `PasswordPolicy`, …). |

**O que mantém a regra.**

- **Controllers finos.** Um controller recebe o pedido, chama um serviço e transforma o `Result` com `this.Problem(error)`; não tem regras.
- **Entidades protegem as invariantes.** Setters privados, construtores privados e métodos com intenção: `booking.CancelByPlayer(window, now)`, `booking.CancelByClub(reason, now)`, `pitch.ChangeSchedule(duration, week, now)`, `pitch.TryAddPriceBand(details, now, out band)`. Quem pede uma mudança recebe o resultado (`CancellationOutcome`, `NoShowOutcome`, `PriceBandOutcome`) e o estado só muda no caso de sucesso.
- **Falhas esperadas são resultados.** Os serviços devolvem `Result<T>` com um `ServiceError` (`code` estável, `ErrorKind` → estado HTTP); exceções são só erros de programação ou falhas inesperadas, que o `GlobalExceptionHandler` transforma em 500.
- **`IAppDbContext`, sem repositório genérico.** A Application usa os `DbSet` e LINQ do EF Core diretamente (o `DbContext` é a unidade de trabalho). O que depende do PostgreSQL fica atrás de uma interface pequena implementada na Infrastructure (`IBookingStore`, `IPitchStore`, `IClubStore`, `IOwnerStatusStore`, `IOccupationStore`, …).
- **Hora injetada.** Toda a hora atual vem de `TimeProvider` (CC-TIME).

**Exceções reais** (ver as [Notas de arquitetura](../README.md#limitações-conhecidas)):

- Entidades persistidas fora do Domain: a conta (`AppUser`, D-USER, do Identity), o refresh token (`RefreshToken`, D-REFRESH) e a mudança de estado de um dono (`OwnerStatusChange`, D-STATUSCHG) vivem na Infrastructure; a mensagem do outbox (`OutboxMessage`, D-OUTBOX) vive na Application. No Domain a conta existe só como regras (`Dom/Accounts`).
- O gerador de tokens de recuperação de palavra-passe do Identity (`DataProtectorTokenProvider`, registado por `AddDefaultTokenProviders`) lê o relógio do sistema, não o `TimeProvider`; os testes fabricam tokens antigos no formato do Identity.

## 2. Componentes da API (C4 nível 3)

### Visão geral por funcionalidade

![Componentes da API, visão geral](../out/03-components-api-overview.svg)

Fonte: [03-components-api-overview.puml](../c4/03-components-api-overview.puml).

Cada componente junta o controller, os serviços e o que a Infrastructure lhe dá (caminhos no inventário). Os componentes de arranque e de linha de comandos (API-ADMINSEED, API-DEMO, API-ADMINPW) não estão aqui: correm antes de o servidor arrancar (C-CLI) e aparecem no diagrama de contas. As peças transversais que estão à frente de todos os controllers (CC-ORIGIN, CC-RATE, CC-COOKIES, CC-AUTHZ, CC-PROBLEM) também ficam de fora para não repetir uma seta por componente.

As ligações entre funcionalidades que importam:

- **Reservar passa por quatro peças partilhadas**: CC-AUTHZ (só no dono), CC-LOCK, CC-SLOTCHECK e API-NOTIF. O pormenor está no diagrama seguinte.
- **Um só cálculo de slots livres** (CC-DAYCAL) para a pré-visualização do dono (API-SCHED) e para o catálogo público (API-CATALOG).
- **Um só cálculo de ocupação** (`OccupationService`, em API-STATS), usado também pelo top 10 do admin.
- **API-PITCHES é a porta para os campos do dono** (`OwnedPitches`), usada pelos horários, bloqueios, faixas de preço e reservas do dono.

### Reservas: uma, série semanal e do dono

![Componentes das reservas](../out/03-components-api-bookings.svg)

Fonte: [03-components-api-bookings.puml](../c4/03-components-api-bookings.puml).

- Uma reserva (API-BOOK-SINGLE), cada data de uma série (API-BOOK-SERIES, em `BookOneAsync`) e a reserva para um cliente (API-CLUBBOOK) fazem a mesma sequência: tranca o campo (CC-LOCK), volta a ler o campo dentro do trinco, `BookingSlotCheck.CheckAsync` (CC-SLOTCHECK), preço de `Pitch.PriceForSlot`, e insere por `IBookingStore.TryAddAsync`. A sobreposição com outra reserva fica para a `bookings_no_overlap` (DB-NOOVERLAP): 23P01 → 409 `slot_taken` (ou o motivo `taken` numa série).
- Os emails (API-NOTIF) entram no mesmo `IAppDbContext` antes do `SaveChanges` de quem chama, por isso são escritos, ou desfeitos, com a reserva. A série tem um resumo próprio, gravado depois das datas, numa transação à parte.
- Cancelar (jogador ou dono) e marcar ou anular uma falta também correm sob CC-LOCK e voltam a ler a reserva lá dentro. Anular uma falta grava por `TrySaveChangesAsync`, porque a constraint pode recusar.
- A pré-visualização de uma série lê o campo pelos clubes visíveis (`VisibleClubs`, API-CATALOG); a criação, como uma reserva simples, só verifica que o campo existe e que o dono está ativo.

### Contas, sessões e recuperação de palavra-passe

![Componentes das contas](../out/03-components-api-accounts.svg)

Fonte: [03-components-api-accounts.puml](../c4/03-components-api-accounts.puml).

- O registo e o login terminam em `ISessionService.StartAsync` (CC-COOKIES): um JWT de acesso com `sub` e `role` e um refresh token guardado só como SHA-256; o `AuthController` escreve os dois cookies com `AuthCookies`.
- O refresh (CC-COOKIES) recusa uma conta desativada e consome o token com uma atualização condicional; um token já usado revoga todas as sessões da conta.
- A recuperação (API-AUTH-RESET) só põe no outbox o id da conta; o token é feito pelo worker, com o Identity e as chaves do Data Protection (CC-DATAPROT, guardadas em DB-DPKEYS). Confirmar muda o hash, revoga as sessões e põe o aviso `password_changed` no outbox, numa transação.
- Desativar um dono (API-ADMIN) e `set-admin-password` (API-ADMINPW) também revogam refresh tokens. Só o seed (API-ADMINSEED) dá o perfil Admin.

### Notificações: outbox e worker

![Componentes das notificações](../out/03-components-api-notifications.svg)

Fonte: [03-components-api-notifications.puml](../c4/03-components-api-notifications.puml).

- Do lado do pedido, só se escreve na tabela `outbox_messages`, na transação da alteração. O índice DB-OUTBOXUQ garante no máximo um lembrete e um aviso ao dono por reserva.
- O worker (C-WORKER) é um `BackgroundService` no processo da API: `OutboxDispatcher` corre `OutboxProcessor.ProcessDueAsync` num scope por passagem; este reclama as mensagens devidas com `FOR UPDATE SKIP LOCKED`, lê a reserva e o destinatário **no momento do envio**, compõe (pt-PT, hora de Lisboa) e envia por `IEmailSender`. Os estados da mensagem estão na secção 5.

## 3. Componentes do front end (C4 nível 3)

### Rotas, guardas e caminhos até à API

![Componentes do front end](../out/03-components-web.svg)

Fonte: [03-components-web.puml](../c4/03-components-web.puml).

Há dois caminhos até à API:

- **No browser**, os Client Components das funcionalidades chamam `/api/*` na mesma origem, só por WEB-API-CLIENT (`apiRequest`/`apiJson`); o Next.js reencaminha para a API (WEB-REWRITE). Os cookies httpOnly vão com o pedido; num 401 há um só refresh partilhado (Web Lock entre separadores) e uma nova tentativa.
- **No servidor**, WEB-SERVER (`lib/server`, `server-only`) chama a API diretamente pelo **URL interno** (`API_INTERNAL_URL`, `lib/server/api-url.ts`), com `no-store`: `getSessionAccount` passa o cookie do utilizador a `GET /api/auth/me`, `loadPublicClub` não passa cookies, `loadAdminStats` passa os do admin. O servidor nunca renova uma sessão. Hoje, no compose, esse URL é `http://api:8080`. Em produção (T06) é o URL público do serviço da API no Render (PL-RENDER-API), definido em `API_INTERNAL_URL`.

WEB-PROXY (`proxy.ts`) escreve o caminho pedido em `x-pathname` nas áreas com guarda (`/manage/*`, `/admin/*`, `/bookings`); os guardas (WEB-GUARDS) usam-no para mandar para `/login?next=…`. Também responde 404 a um id impossível e a um clube público que a API não tem, porque uma página que já começou a enviar não muda o estado HTTP. Os guardas são uma conveniência: quem decide o acesso é sempre a API.

**O estado está no endereço, não num store.** O passo da reserva (`?pitch=&date=&slot=&weeks=`, `features/booking/selection.ts`), a vista do calendário (`?date=&view=&pitch=&booking=&cancelled=`, `features/calendar/calendar-url.ts`), os filtros da pesquisa, as páginas e os filtros do admin e das estatísticas vivem no endereço da página (`useSearchParams`, `router.replace`). É isso que deixa um visitante voltar à mesma escolha depois do login e partilhar ligações. Os dados da API ficam no TanStack Query; não há Zustand.

### Áreas e funcionalidades

![Áreas e funcionalidades do front end](../out/03-components-web-features.svg)

Fonte: [03-components-web-features.puml](../c4/03-components-web-features.puml).

As setas entre funcionalidades são as importações reais que contam alguma coisa (um componente, uma leitura, uma invalidação de cache). WEB-F-PITCHES também dá a quase todas as outras funções de formatação (`formatPrice`, `sportLabel`, `previewDateRange`); essas setas foram omitidas.

## 4. Modelo do domínio

Dois diagramas, para não passar de cerca de 15 elementos: o das reservas e o das contas e entidades de outras camadas. O pacote de cada classe é a camada onde ela vive.

### Clubes, campos, bloqueios e reservas

![Modelo do domínio: reservas](../out/logical-domain-booking.svg)

Fonte: [logical-domain-booking.puml](logical-domain-booking.puml).

### Contas, sessões, histórico dos donos e outbox

![Modelo do domínio: contas](../out/logical-domain-accounts.svg)

Fonte: [logical-domain-accounts.puml](logical-domain-accounts.puml).

### Invariantes principais e onde são garantidas

| Elemento | Invariante | Garantida por |
| --- | --- | --- |
| D-CLUB | Um só dono, para sempre (`OwnerId` só na criação). | `Club.Create`; FK `RESTRICT` para `users`. |
| D-CLUB | `Slug` único, gerado do nome na criação, nunca mudado. | `Club.UpdateDetails` não o toca; `ix_clubs_slug` (único). |
| D-CLUB | Janela de cancelamento de 1 a 168 h; lida no momento do cancelamento, nunca copiada para a reserva. | `ClubRules.IsValidCancellationWindow`; `ck_clubs_cancellation_window_hours`. |
| D-PITCH | Nunca apagado (ativo/inativo); nome único no clube sem distinguir maiúsculas. | `Deactivate`/`Reactivate`; `NameKey` + `ix_pitches_club_id_name_key`. |
| D-PITCH | Preço > 0, ≤ 500, duas casas; slot de 30, 60 ou 90 min. | `PitchRules.IsValidPrice`, `SlotDuration.IsSupported`; `ck_pitches_*`. |
| D-PITCH | Os slots só vêm de `SlotsOn` → `SlotGenerator`; o preço de um slot só vem de `PriceForSlot`. | Código (uma só função de cada). |
| D-OPENDAY | No máximo um intervalo por dia; horas de 30 em 30 min; fecho `00:00` = meia-noite, nunca depois. | `Pitch.ChangeSchedule`, `OpeningHours.Create`; `ck_pitch_opening_hours_*`. |
| D-PRICEBAND | Pelo menos um dia; sem sobreposição no mesmo campo (tocar é permitido); no máximo 20. | `Pitch.TryAddPriceBand`/`TryChangePriceBand`, `PitchPriceBand.Apply`; `ck_pitch_price_bands_*`. |
| D-BLOCK | `[StartsAt, EndsAt)` em UTC, fim depois do início; sem sobreposição com outro bloqueio nem com reservas confirmadas. | `Block.Create`; `BlockService` sob CC-LOCK; `ck_blocks_ends_after_starts`. |
| D-BOOKING | De um jogador **ou** de um cliente, nunca os dois nem nenhum. | `Booking.ForPlayer`/`ForCustomer`; `ck_bookings_player_or_customer`. |
| D-BOOKING | Nenhuma reserva confirmada se sobrepõe a outra do mesmo campo. | `bookings_no_overlap` (DB-NOOVERLAP), só para `Confirmed`. |
| D-BOOKING | Nunca apagada; muda de estado só pelos métodos da secção 5; guarda o preço do momento. | `Booking`; `ck_bookings_status`, `ck_bookings_price`. |
| D-USER | Um email = uma conta = um perfil; email normalizado único. | `AccountStore.CreateAsync` (conta e perfil numa transação); `ix_users_normalized_email`. |
| D-REFRESH | Só o SHA-256 é guardado; cada token usa-se uma vez; validade absoluta. | `RefreshToken.Issue`; atualização condicional em `SessionService.RefreshAsync`. |
| D-STATUSCHG | Só se acrescenta. | Sem update/delete no código; trigger `owner_status_changes_append_only` (DB-AUDITTRG). |
| D-OUTBOX | Sem endereços no payload; no máximo um resultado. | `BookingEmailOutbox`, `PasswordResetStore`; `ck_outbox_messages_one_outcome`. |

## 5. Máquinas de estados

As transições foram tiradas dos métodos das entidades e dos serviços que os chamam.

### Reserva

![Estados de uma reserva](../out/logical-state-booking.svg)

Fonte: [logical-state-booking.puml](logical-state-booking.puml).

`Confirmed` é o único estado com saídas. As duas formas de cancelar são finais. Uma falta pode voltar a `Confirmed` (`UndoNoShow`), mas só se nenhuma outra reserva confirmada tiver ocupado o slot entretanto: a falta não ocupa o slot, por isso a base de dados é que decide (23P01 → 409 `slot_taken`). `Booking.MarkNoShow(now)` é o mesmo que `RecordNoShow(now) == Marked` e só é usado pelo `DemoDataSeeder`.

### Mensagem do outbox

![Estados de uma mensagem do outbox](../out/logical-state-outbox.svg)

Fonte: [logical-state-outbox.puml](logical-state-outbox.puml).

Não há coluna de estado: o estado é qual das três datas (`SentAt`, `SkippedAt`, `FailedAt`) está preenchida, e `IsPending` quando nenhuma está. Uma falha de envio conta uma tentativa e adia a seguinte (`OutboxRetry`, intervalo a dobrar até ao máximo); na última tentativa permitida, a mensagem passa a falhada.

### Dono

![Estados de um dono](../out/logical-state-owner.svg)

Fonte: [logical-state-owner.puml](logical-state-owner.puml).

O estado é `users.deactivated_at`. A mudança é feita por `IOwnerStatusStore.TryChangeAsync` (`Inf/Admin/OwnerStatusStore`) com um `UPDATE` condicional, não pelo método `AppUser.Deactivate(now)`, que só os testes chamam. Os efeitos de estar desativado vêm das verificações que já existem: `AccountStore.AuthenticateAsync` (login), `SessionService.RefreshAsync` (refresh), `VisibleClubs` (páginas públicas) e `IAccountStore.IsActiveAsync` (novas reservas).

## 6. Código (C4 nível 4)

### O agregado Booking

![Código: Booking](../out/logical-code-booking.svg)

Fonte: [logical-code-booking.puml](logical-code-booking.puml). Ficheiro: `Dom/Bookings/Booking.cs`.

Os métodos que mudam o estado devolvem o resultado em vez de lançar: os serviços traduzem cada resultado num `ServiceError` (por exemplo `CancellationOutcome.OutsideWindow` → 422 `outside_cancellation_window`, com o `cancellationDeadline`). Os métodos estáticos `CancellationDeadline(startsAt, window)` e `CanBeCancelledByPlayer(status, startsAt, window, now)` existem para a lista das minhas reservas, que não carrega entidades, e são os mesmos que os métodos de instância usam.

### Geração de slots, «livre» e preço

![Código: slots](../out/logical-code-slots.svg)

Fonte: [logical-code-slots.puml](logical-code-slots.puml).

- `ClubTime` é a única conversão local↔UTC. `OpeningHours.UtcIntervalOn` usa-a para os instantes de abertura e fecho de um dia; `SlotGenerator.Generate` corta esse intervalo em slots de duração exata.
- `Pitch.SlotsOn` escolhe as horas do dia da semana e chama o gerador; `Pitch.SlotStartingAt` (o fim de uma reserva vem sempre daqui) e `Pitch.PriceForSlot` (a primeira faixa que `Covers` o dia e a hora locais do início, senão `PricePerSlot`) usam a hora local do início.
- `PitchDayCalendar.SlotsAsync` junta aos slots os bloqueios e as reservas confirmadas de todos os campos pedidos e devolve `DaySlot`s; `DaySlot.IsFreeAt` é a única definição de «livre».
- `BookingSlotCheck.CheckAsync` é o que se verifica ao reservar: a janela (`BookingWindow.Contains` com `ClubTime.Today`), a grelha (`SlotStartingAt`), «começado» (`TimeSlot.HasStartedBy`) e os bloqueios.

## Notas

- **Ficheiro.** Esta vista junta o que antes estava previsto em `03-components.md` e `04-domain.md` (que estavam vazios).
- **Ids novos.** As camadas (`L-`) e as partes dos componentes mais carregados (`API-BOOK-*`, `API-AUTH-*`, `API-NOTIF-*`) foram acrescentadas ao inventário com esta vista.
- **Desvios encontrados entre documentos e código** (todos corrigidos):
  - `docs/stack.md` dava como exemplo `booking.Cancel(motivo, agora)`; os métodos reais são `CancelByPlayer(window, now)` e `CancelByClub(reason, now)`. **Corrigido.**
  - O AGENTS.md dizia que uma conta desativada é `AppUser.Deactivate(now)`; em produção a desativação é o `UPDATE` condicional de `OwnerStatusStore`, e `AppUser.Deactivate` só é usado nos testes. **Corrigido.**
  - O comentário de `PitchPriceBand` referia `Pitch.AddPriceBand` e `Pitch.ChangePriceBand`; os métodos chamam-se `TryAddPriceBand` e `TryChangePriceBand`. **Corrigido** (pull request de endurecimento pré-deploy, T06).
