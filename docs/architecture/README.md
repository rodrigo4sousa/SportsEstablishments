# Arquitetura

Arquitetura da App de Reservas de Campos, em diagramas PlantUML e texto. Cada ligação de diagrama abre o SVG; a ligação `.puml` é a fonte. Os documentos descrevem o sistema como está no código e em produção (no ar desde 2026-11-09, T06): um Next.js com três áreas (jogador, dono, admin) sobre uma API ASP.NET Core e uma base PostgreSQL, com um worker de emails dentro da API.

As regras estão no [backlog](../backlog.md), as escolhas técnicas na [Stack](../stack.md) e o mapa do código no [AGENTS.md](../../AGENTS.md). Estes documentos não repetem as regras: mostram como as partes se ligam e apontam para o detalhe.

## Como estão organizados estes documentos

Duas formas de ver o mesmo sistema, porque respondem a perguntas diferentes. O **C4** aproxima-se por níveis (o sistema, os contentores, os componentes, o código). As **vistas 4+1** de Kruchten olham de uma preocupação de cada vez (lógica, processo, desenvolvimento, física e, ao centro, os cenários).

- [`inventory.md`](inventory.md) é o catálogo do que existe: cada elemento tem um id estável (`C-API`, `API-BOOK`, `D-BOOKING`, `PL-RENDER`, …) e o caminho no código. Os diagramas desenham-se a partir dele.
- [`c4/README.md`](c4/README.md) tem o texto do contexto, dos contentores e da implantação; [`4plus1/README.md`](4plus1/README.md) diz, por vista, para quem é e que decisão documenta, e liga ao texto de cada uma.
- [`adr/`](adr/README.md) tem as decisões, com contexto, alternativas e consequências. Uma ADR aceite não se reescreve: se a decisão mudar, escreve-se outra que a substitui.

Todos os diagramas seguem as mesmas regras:

- **Um nível de abstração por diagrama:** um nível do C4 ou uma vista 4+1.
- **Sem elementos inventados:** tudo o que se desenha existe no código ou na configuração. Um elemento novo entra primeiro no inventário, no mesmo pull request.
- **O planeado fica à parte:** desenha-se tracejado (a marca `planned` de [`style.puml`](style.puml)) e só se ainda não existe. Hoje nenhum diagrama a usa.
- **O mesmo nome é o mesmo elemento** em todos os diagramas: o nome visível começa pelo id do inventário (`C-API · API ASP.NET Core`) e o alias no PlantUML é o id com `_` (`C_API`).
- **Um estilo comum:** todos incluem [`style.puml`](style.puml) e começam por um comentário com o título, a versão, a data, o que não está desenhado e, nas vistas 4+1, para quem é e que decisão documenta.
- **Setas com verbo em pt-PT** do ponto de vista de quem chama, e o protocolo ou formato no argumento da tecnologia.
- **Cerca de 10 elementos por diagrama** (ver [Limitações conhecidas](#limitações-conhecidas)).

Um pull request que cria, remove ou muda de responsabilidade ou de caminho um elemento atualiza o inventário e os diagramas que o mostram.

## Índice

### C4 ([`c4/`](c4/))

O texto está em [`c4/README.md`](c4/README.md), exceto o dos componentes (nível 3), que está na [vista lógica](4plus1/logical.md).

| Diagrama | Fonte | Mostra |
|---|---|---|
| [1. Contexto](out/01-context.svg) | [`01-context.puml`](c4/01-context.puml) | Pessoas (visitante, jogador, dono, admin), o sistema como uma caixa, o servidor SMTP e as plataformas de alojamento |
| [2. Contentores: desenvolvimento](out/02-containers-dev.svg) | [`02-containers-dev.puml`](c4/02-containers-dev.puml) | Browser, Next.js, API com o worker de emails, PostgreSQL e Mailpit (docker compose) |
| [2. Contentores: produção](out/02-containers-prod.svg) | [`02-containers-prod.puml`](c4/02-containers-prod.puml) | Browser, Next.js na Vercel, API e worker no Render, PostgreSQL no Supabase, Brevo |
| [3. Componentes da API: visão geral](out/03-components-api-overview.svg) | [`03-components-api-overview.puml`](c4/03-components-api-overview.puml) | Os componentes por funcionalidade e as peças transversais |
| [3. Componentes da API: reservas](out/03-components-api-bookings.svg) | [`03-components-api-bookings.puml`](c4/03-components-api-bookings.puml) | Uma reserva, série semanal, as minhas reservas, o lado do dono, e a verificação do slot partilhada |
| [3. Componentes da API: contas](out/03-components-api-accounts.svg) | [`03-components-api-accounts.puml`](c4/03-components-api-accounts.puml) | Registo, login, sessões, recuperação de palavra-passe, seeds e comandos |
| [3. Componentes da API: notificações](out/03-components-api-notifications.svg) | [`03-components-api-notifications.puml`](c4/03-components-api-notifications.puml) | Outbox, worker, composição e envio dos emails |
| [3. Componentes do front end: rotas e guardas](out/03-components-web.svg) | [`03-components-web.puml`](c4/03-components-web.puml) | Rotas, guardas e os caminhos do front end até à API |
| [3. Componentes do front end: funcionalidades](out/03-components-web-features.svg) | [`03-components-web-features.puml`](c4/03-components-web-features.puml) | As áreas e as pastas de funcionalidades de cada uma |
| [Implantação: desenvolvimento](out/04-deployment-dev.svg) | [`04-deployment-dev.puml`](c4/04-deployment-dev.puml) | A máquina de desenvolvimento, a rede do compose, as portas e o volume da base de dados |
| [Implantação: produção](out/04-deployment-prod.svg) | [`04-deployment-prod.puml`](c4/04-deployment-prod.puml) | Vercel, Render, Supabase, Brevo e GitHub Actions |

### 4+1 ([`4plus1/`](4plus1/))

Para quem é cada vista e que decisão documenta: [`4plus1/README.md`](4plus1/README.md).

| Vista | Texto | Diagramas |
|---|---|---|
| Lógica | [`logical.md`](4plus1/logical.md) | [camadas do back end](out/logical-layers.svg) ([`.puml`](4plus1/logical-layers.puml)), [domínio: clubes, campos, bloqueios e reservas](out/logical-domain-booking.svg) ([`.puml`](4plus1/logical-domain-booking.puml)), [domínio: contas, sessões, histórico dos donos e outbox](out/logical-domain-accounts.svg) ([`.puml`](4plus1/logical-domain-accounts.puml)), [estados de uma reserva](out/logical-state-booking.svg) ([`.puml`](4plus1/logical-state-booking.puml)), [estados de uma mensagem do outbox](out/logical-state-outbox.svg) ([`.puml`](4plus1/logical-state-outbox.puml)), [estados de um dono](out/logical-state-owner.svg) ([`.puml`](4plus1/logical-state-owner.puml)), [código: o agregado Booking](out/logical-code-booking.svg) ([`.puml`](4plus1/logical-code-booking.puml)), [código: slots, «livre» e preço](out/logical-code-slots.svg) ([`.puml`](4plus1/logical-code-slots.puml)) |
| Processo | [`process.md`](4plus1/process.md) | [processos e fios](out/process-runtime.svg) ([`.puml`](4plus1/process-runtime.puml)), [pipeline de um pedido na API](out/process-pipeline.svg) ([`.puml`](4plus1/process-pipeline.puml)), [reservar um slot](out/process-book.svg) ([`.puml`](4plus1/process-book.puml)), [série semanal](out/process-series.svg) ([`.puml`](4plus1/process-series.puml)), [cancelar (jogador)](out/process-cancel-player.svg) ([`.puml`](4plus1/process-cancel-player.puml)), [cancelar (dono)](out/process-cancel-club.svg) ([`.puml`](4plus1/process-cancel-club.puml)), [login e logout](out/process-login.svg) ([`.puml`](4plus1/process-login.puml)), [refresh na API](out/process-refresh.svg) ([`.puml`](4plus1/process-refresh.puml)), [refresh no browser](out/process-refresh-client.svg) ([`.puml`](4plus1/process-refresh-client.puml)), [pedir nova palavra-passe](out/process-reset-request.svg) ([`.puml`](4plus1/process-reset-request.puml)), [definir a nova palavra-passe](out/process-reset-confirm.svg) ([`.puml`](4plus1/process-reset-confirm.puml)), [worker de emails](out/process-outbox.svg) ([`.puml`](4plus1/process-outbox.puml)), [avisos e lembretes ignorados](out/process-outbox-skip.svg) ([`.puml`](4plus1/process-outbox-skip.puml)), [página com guarda](out/process-guard.svg) ([`.puml`](4plus1/process-guard.puml)), [desativar um dono](out/process-owner-deactivation.svg) ([`.puml`](4plus1/process-owner-deactivation.puml)) |
| Desenvolvimento | [`development.md`](4plus1/development.md) | [estrutura do repositório](out/development-repo.svg) ([`.puml`](4plus1/development-repo.puml)), [projetos do back end](out/development-backend.svg) ([`.puml`](4plus1/development-backend.puml)), [injeção de dependências](out/development-di.svg) ([`.puml`](4plus1/development-di.puml)), [pastas do front end](out/development-frontend.svg) ([`.puml`](4plus1/development-frontend.puml)), [do controller aos tipos](out/development-api-types.svg) ([`.puml`](4plus1/development-api-types.puml)), [CI (ci.yml)](out/development-ci.svg) ([`.puml`](4plus1/development-ci.puml)), [deploy, dados de produção e keepalive (deploy.yml, prod-data.yml, keepalive.yml)](out/development-deploy.svg) ([`.puml`](4plus1/development-deploy.puml)) |
| Física | [`physical.md`](4plus1/physical.md) | [implantação UML da produção](out/physical.svg) ([`.puml`](4plus1/physical.puml)) |
| Cenários (+1) | [`scenarios.md`](4plus1/scenarios.md) | [casos de uso](out/scenarios-use-cases.svg) ([`.puml`](4plus1/scenarios-use-cases.puml)) |

### Decisões (ADR)

Estado em [`adr/`](adr/README.md). Nas ADRs 0001 a 0008 a decisão é mais antiga que o registo e vem da Stack ou do backlog, como cada uma indica.

| N.º | Decisão | Estado |
|---|---|---|
| [0001](adr/0001-uma-app-tres-areas.md) | Uma só aplicação com três áreas | Aceite |
| [0002](adr/0002-sessao-em-cookies-httponly.md) | Sessão em cookies httpOnly em vez de localStorage | Aceite |
| [0003](adr/0003-exclusion-constraint-reservas.md) | Exclusion constraint contra reservas duplicadas | Aceite |
| [0004](adr/0004-outbox-para-emails.md) | Outbox para os emails | Aceite |
| [0005](adr/0005-marketplace-unico.md) | Um marketplace em vez de um site por dono | Aceite |
| [0006](adr/0006-ids-guid-v7.md) | Ids públicos GUID v7 | Aceite |
| [0007](adr/0007-slots-calculados.md) | Slots calculados, não guardados | Aceite |
| [0008](adr/0008-chaves-data-protection-na-base-de-dados.md) | Chaves do Data Protection na base de dados | Aceite |
| [0009](adr/0009-alvo-de-implantacao.md) | Alvo de implantação | Aceite em 2026-11-09, depois do primeiro deploy real |

## Limitações conhecidas

Os diagramas mostram o código como está, com as falhas. Estes são factos do código que as vistas e as ADRs dizem tal como são; não são erros.

1. **A API também se alcança diretamente, em desenvolvimento.** Além de passar pelo Next.js, a API está na porta 5080 do compose (Swagger em `/swagger`), e `http://localhost:5080` é uma das origens aceites em Development. Os diagramas de contentores e de implantação de desenvolvimento mostram as duas entradas.
2. **O proxy reverso é o Next.js, não o Caddy.** O browser fala com C-WEB e é o Next.js (WEB-REWRITE) que reencaminha `/api/*` para C-API, em desenvolvimento e em produção. O IP do cliente está na [vista física, 4.4](4plus1/physical.md#44-o-ip-do-cliente).
3. **Entidades persistidas fora do Domain.** A conta (`AppUser`, do Identity), o refresh token e a mudança de estado de um dono vivem na Infrastructure, e a mensagem do outbox na Application. No Domain a conta existe só como regras (`Dom/Accounts`). O diagrama do domínio mostra-as com a camada a que pertencem.
4. **Uma exceção ao relógio injetado.** Toda a hora atual vem de `TimeProvider` (CC-TIME), exceto o gerador de tokens de recuperação de palavra-passe do Identity, que lê o relógio do sistema; por isso os testes fabricam tokens antigos no formato do Identity.
5. **A exclusion constraint só cobre reservas `Confirmed`.** `bookings_no_overlap` (DB-NOOVERLAP) tem `WHERE status = 'Confirmed'`: uma reserva cancelada ou uma falta não ocupa o slot, e anular uma falta só tem sucesso se mais nenhuma reserva confirmada se sobrepuser entretanto; senão a base de dados recusa (23P01 → 409 `slot_taken`).

**Tamanho dos diagramas.** A regra é cerca de 10 elementos; os diagramas de componentes da API vão até 16 porque já estão divididos por área. A contagem abaixo é aproximada (linhas de elementos nos `.puml`, passos nos diagramas de atividade). Passam de 12:

- `development-ci` (23 passos, em três jobs com no máximo 10 cada), `physical` (18) e `04-deployment-prod` (17);
- `03-components-api-overview` e `03-components-api-bookings` (16), `03-components-api-notifications` e `03-components-api-accounts` (15);
- `development-frontend` (14), `03-components-web-features` (13) e `process-pipeline` (13).

Os mais largos ficam ilegíveis quando reduzidos à largura de uma página; abre o SVG ao tamanho real.

**Implantação.** As secções de deploy da [Stack](../stack.md) (o Caddy, a máquina virtual, o `Caddyfile`, o GitHub Container Registry, o deploy por SSH) estão substituídas pela [ADR 0009](adr/0009-alvo-de-implantacao.md), que as nomeia; a Stack não foi alterada. Nesse ponto valem os diagramas e a ADR, não a Stack. O que as vistas de implantação **não** cobrem ou deixam em aberto:

- O IP do cliente em produção é de melhor esforço: a API acredita em `X-Forwarded-For` com `ForwardLimit=1` e é pública, por isso os limites por IP não são uma proteção forte. A cadeia de `X-Forwarded-For` do Render não está documentada ([vista física, 4.4](4plus1/physical.md#44-o-ip-do-cliente)).
- O worker corre dentro da API, que adormece sem tráfego no plano gratuito do Render: os emails e lembretes só saem enquanto a API estiver acordada.
- Não estão adotados (ADR 0009): domínio próprio, cópias de segurança automáticas, rede privada entre a Vercel e o Render.
- Os pontos que o primeiro deploy real não confirmou, e os riscos aceites, estão na [vista física, secção 5](4plus1/physical.md#5-riscos-e-pontos-em-aberto-para-a-t06).
- Os dados de planos e preços dos fornecedores mudam; os que estão na vista física têm data e fonte.

**Texto e código.** `docs/stack.md` mostra um `Caddyfile` na árvore do repositório que não existe. Tudo foi conferido à mão com o código; não há teste que prove que um diagrama corresponde ao código, nem que os ids existem no inventário. O que a verificação automática garante está em [Renderizar os diagramas](#renderizar-os-diagramas).

## Renderizar os diagramas

Os SVG de [`out/`](out/) são gerados a partir dos `.puml`; não se editam à mão. [`render.sh`](render.sh) (Git Bash, macOS, Linux) e [`render.ps1`](render.ps1) (Windows PowerShell) desenham todos com a imagem Docker `plantuml/plantuml`, **cuja versão está fixada em [`style.puml`](style.puml)** (linha `' plantuml-version: X.Y.Z`). O tipo de letra (DejaVu Sans) vem dessa imagem e não se monta nada do computador, por isso o resultado é o mesmo em qualquer máquina e no CI. É preciso ter o Docker a correr.

```bash
bash docs/architecture/render.sh
powershell -ExecutionPolicy Bypass -File docs\architecture\render.ps1
```

1. Edita o `.puml` (os de C4 usam `!include <C4/…>`, que o PlantUML traz, e depois `!include ../style.puml`) e, se há elementos novos, o inventário.
2. Desenha, e faz commit do `.puml` e do `.svg` juntos.
3. Põe o diagrama no [índice](#índice) deste ficheiro. Para mudar a versão do PlantUML, altera a linha de `style.puml` e volta a desenhar tudo.
4. Corre `scripts/check-architecture-docs.sh` (ou `.ps1`): falha se algum `.svg` de `out/` não tiver ligação neste ficheiro, se este ficheiro ou outro `.md` de `docs/` ou o `README.md` da raiz tiver uma ligação ou imagem relativa que não chega a um ficheiro, ou se algum `.puml` não incluir `../style.puml`. Não verifica as âncoras (`#secção`).

O job `diagrams` do CI («Docs (architecture diagrams)») corre `render.sh`, falha se algum `.svg` mudou ou falta (o `git status` de `out/` tem de ficar limpo) e corre o verificador.
